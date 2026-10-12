using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading;

namespace LayoutProbe
{
    /// <summary>
    /// Spike of the layout probe. Prints the memory layout the auto splitter reads, for the
    /// flavour of the game it runs with. It has to run inside that flavour's own runtime, from
    /// the game's directory: 32-bit .NET Framework on Windows, and the Mono bundled with the
    /// game on Linux and macOS, through the game's MonoKickstart launcher (which runs the .exe
    /// named after itself).
    ///
    /// Options:
    ///   --game PATH   the game's Celeste.exe, relative to the probe's directory (default:
    ///                 Celeste.exe next to the probe). Lets the probe itself be Celeste.exe,
    ///                 so its process has the game's name.
    ///   --construct   then try `new Celeste()`, and read the result back the way the auto
    ///                 splitter would.
    ///   --hold SECS   then stay alive, so the process can be looked at from outside.
    /// </summary>
    internal static class Program
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags DeclaredInstance = AnyInstance | BindingFlags.DeclaredOnly;

        // The hand-written signatures in autosplitter/src/memory.rs. Each is followed, at the
        // given displacement from the start of the match, by the Celeste.Instance slot's
        // address, of the given width.
        private static readonly Signature[] Signatures =
        {
            new Signature("windows", "68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35", 99, 4),
            new Signature("linux", "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00", 132, 4),
            new Signature("macos", "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00", 167, 8),
        };

        // Enough to cover the constructor up to its Instance store on every flavour.
        private const int CodeDumpLength = 768;

        private static int failures;

        private static string gamePath;
        private static Assembly game;
        private static Type celesteType;
        private static FieldInfo instanceField;
        private static FieldInfo titleField;
        private static FieldInfo infoField;
        private static long titleOffset;
        private static long infoOffset;
        private static long stringLengthOffset;
        private static long stringCharsOffset;
        private static ulong slot;

        private static int Main(string[] args)
        {
            string gameFile = "Celeste.exe";
            bool construct = false;
            int hold = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game": gameFile = args[++i]; break;
                    case "--construct": construct = true; break;
                    case "--hold": hold = int.Parse(args[++i]); break;
                    default: throw new ArgumentException("unknown option " + args[i]);
                }
            }
            gamePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, gameFile));

            Section("runtime", PrintRuntime);
            if (!Section("game", LoadGame))
            {
                return 1;
            }
            Section("layout", PrintLayout);
            Section("static root Celeste.Instance", PrintRoot);
            if (construct)
            {
                Section("construction", Construct);
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "probe: ok" : "probe: " + failures + " section(s) failed");
            if (hold > 0)
            {
                Console.WriteLine("holding for {0} s", hold);
                Console.Out.Flush();
                Thread.Sleep(hold * 1000);
            }
            return failures == 0 ? 0 : 1;
        }

        private static bool Section(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine("== " + name);
            bool ok = true;
            try
            {
                body();
            }
            catch (Exception e)
            {
                failures++;
                ok = false;
                Console.WriteLine("FAILED: " + e);
            }
            Console.Out.Flush();
            return ok;
        }

        private static void PrintRuntime()
        {
            Console.WriteLine("os: " + Environment.OSVersion);
            Console.WriteLine("pointer size: " + IntPtr.Size);
            Type mono = Type.GetType("Mono.Runtime");
            if (mono != null)
            {
                MethodInfo displayName = mono.GetMethod("GetDisplayName", AnyStatic);
                Console.WriteLine("runtime: Mono " + (displayName == null ? "(unknown version)" : displayName.Invoke(null, null)));
            }
            else
            {
                Console.WriteLine("runtime: .NET Framework " + FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location).FileVersion);
            }
            Console.WriteLine("probe: " + Assembly.GetEntryAssembly().Location);
        }

        private static void LoadGame()
        {
            // When the probe is Celeste.exe, that's what the game's assembly name finds next
            // to it, so loads by that name get the game's assembly instead.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
                game != null && new AssemblyName(e.Name).Name == game.GetName().Name ? game : null;

            game = Assembly.LoadFrom(gamePath);
            Console.WriteLine("game: " + gamePath);
            Console.WriteLine("assembly: " + game.FullName);
            Console.WriteLine("mvid: " + game.ManifestModule.ModuleVersionId);
            Console.WriteLine("references: " + string.Join(", ", game.GetReferencedAssemblies().Select(a => a.Name + " " + a.Version).OrderBy(n => n).ToArray()));

            celesteType = NeedType("Celeste.Celeste");
            instanceField = NeedField(celesteType, "Instance", AnyStatic);
            titleField = NeedField(NeedType("Monocle.Engine"), "Title", AnyInstance);
            infoField = NeedField(celesteType, "AutoSplitterInfo", AnyInstance);
            Console.WriteLine("hierarchy: " + string.Join(" : ", Hierarchy(celesteType).Select(t => t.FullName + " [" + t.Assembly.GetName().Name + "]").ToArray()));
        }

        private static void PrintLayout()
        {
            // Offsets need an instance, but not a constructed one.
            object celeste = FormatterServices.GetUninitializedObject(celesteType);
            // Game's finalizer would dispose this half-made object.
            GC.SuppressFinalize(celeste);

            Console.WriteLine("Celeste instance fields:");
            PrintFields(celeste, Hierarchy(celesteType).SelectMany(t => t.GetFields(DeclaredInstance)));
            titleOffset = Memory.FieldOffset(titleField, celeste);
            infoOffset = Memory.FieldOffset(infoField, celeste);
            Console.WriteLine("title field (Monocle.Engine.Title): 0x{0:x}", titleOffset);
            Console.WriteLine("AutoSplitterInfo field: 0x{0:x} (0x{1:x} after the title)", infoOffset, infoOffset - titleOffset);

            object info = FormatterServices.GetUninitializedObject(infoField.FieldType);
            FieldInfo[] infoFields = infoField.FieldType.GetFields(DeclaredInstance);
            Console.WriteLine("AutoSplitterInfo fields:");
            PrintFields(info, infoFields);
            CheckRawReads(info, infoFields);

            const string sample = "Celeste";
            FieldInfo[] stringFields = typeof(string).GetFields(DeclaredInstance);
            Console.WriteLine("String fields:");
            PrintFields(sample, stringFields);
            stringLengthOffset = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(int)), sample);
            stringCharsOffset = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(char)), sample);
            Console.WriteLine("string length: 0x{0:x}, chars: 0x{1:x} (OffsetToStringData: 0x{2:x})", stringLengthOffset, stringCharsOffset, RuntimeHelpers.OffsetToStringData);
        }

        private static void PrintFields(object instance, IEnumerable<FieldInfo> fields)
        {
            foreach (var entry in fields.Select(f => new { Field = f, Offset = Memory.FieldOffset(f, instance) }).OrderBy(e => e.Offset))
            {
                Console.WriteLine("  0x{0:x3}  {1}.{2} : {3}", entry.Offset, entry.Field.DeclaringType.Name, entry.Field.Name, TypeName(entry.Field.FieldType));
            }
        }

        /// <summary>
        /// Checks the measured offsets independently of how they were measured: values written
        /// through reflection must read back raw at them.
        /// </summary>
        private static void CheckRawReads(object instance, FieldInfo[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(instance, SampleValue(fields[i].FieldType, i + 1));
            }
            // Right after a collection the few small allocations below can't start another,
            // so nothing moves while raw addresses are compared.
            GC.Collect();
            ulong address = Memory.AddressOf(instance);
            var mismatches = new List<string>();
            foreach (FieldInfo field in fields)
            {
                ulong at = address + (ulong)Memory.FieldOffset(field, instance);
                bool same;
                switch (field.GetValue(instance))
                {
                    case string s: same = Memory.ReadPointer(at) == Memory.AddressOf(s); break;
                    case long l: same = Memory.ReadInt64(at) == l; break;
                    case int n: same = Memory.ReadInt32(at) == n; break;
                    case bool b: same = Memory.ReadByte(at) == (b ? 1 : 0); break;
                    default: throw new NotSupportedException(field.FieldType.FullName);
                }
                if (!same)
                {
                    mismatches.Add(field.Name);
                }
            }
            if (mismatches.Count > 0)
            {
                throw new Exception("raw reads disagree with reflection for " + string.Join(", ", mismatches.ToArray()));
            }
            Console.WriteLine("raw reads at these offsets agree with reflection");
        }

        private static object SampleValue(Type type, int n)
        {
            if (type == typeof(string)) return "sample " + n;
            if (type == typeof(long)) return 0x0102030405060708L * n;
            if (type == typeof(int)) return 0x01020304 * n;
            // Alternating, so that two neighbouring flags swapped would show.
            if (type == typeof(bool)) return n % 2 == 1;
            throw new NotSupportedException(type.FullName);
        }

        private static void PrintRoot()
        {
            // Run the class constructor first, as Main has by the time it constructs the game
            // (it sets a static of Celeste's first). The JIT may compile static accesses
            // differently before a class is initialized.
            RuntimeHelpers.RunClassConstructor(celesteType.TypeHandle);
            slot = Memory.StaticFieldAddress(instanceField);
            Console.WriteLine("slot: 0x{0:x}", slot);

            ConstructorInfo constructor = Constructor();
            RuntimeHelpers.PrepareMethod(constructor.MethodHandle);
            ulong entry = Memory.ToAddress(constructor.MethodHandle.GetFunctionPointer());
            Console.WriteLine("constructor entry: 0x{0:x}", entry);
            ulong code = MachineCode.FollowJumps(entry, Console.WriteLine);
            byte[] bytes = Memory.ReadMapped(code, CodeDumpLength);
            Console.WriteLine("constructor code at 0x{0:x} ({1} bytes read):", code, bytes.Length);
            MachineCode.HexDump(bytes, Console.WriteLine);

            List<SlotReference> references = MachineCode.FindSlotReferences(bytes, code, slot);
            Console.WriteLine("slot references: " + (references.Count == 0 ? "none" : string.Join(", ", references.Select(r => "+0x" + r.Offset.ToString("x") + " " + r).ToArray())));
            foreach (Signature signature in Signatures)
            {
                List<int> matches = signature.Matches(bytes).ToList();
                if (matches.Count == 0)
                {
                    Console.WriteLine("signature {0}: no match", signature.Name);
                }
                foreach (int match in matches)
                {
                    bool agrees = references.Any(r => r.Offset - match == signature.SlotDisplacement && r.Width == signature.SlotWidth && !r.RipRelative);
                    Console.WriteLine(
                        "signature {0}: match at +0x{1:x}, slot at match {2}; hand-written: +{3} absolute u{4} ({5})",
                        signature.Name,
                        match,
                        string.Join(", ", references.Select(r => (r.Offset - match).ToString("+0;-0") + " " + r).ToArray()),
                        signature.SlotDisplacement,
                        signature.SlotWidth * 8,
                        agrees ? "agrees" : "DISAGREES");
                }
            }
        }

        private static void Construct()
        {
            // Main loads the settings before constructing the game, and the constructor reads
            // Settings.Instance.Fullscreen and VSync.
            FieldInfo settings = NeedField(NeedType("Celeste.Settings"), "Instance", AnyStatic);
            if (settings.GetValue(null) == null)
            {
                settings.SetValue(null, Activator.CreateInstance(settings.FieldType, true));
            }

            var timer = Stopwatch.StartNew();
            object celeste;
            try
            {
                celeste = Constructor().Invoke(new object[0]);
            }
            catch (TargetInvocationException e)
            {
                throw new Exception("new Celeste() threw", e.InnerException);
            }
            // Nothing disposes it before the process exits.
            GC.SuppressFinalize(celeste);
            Console.WriteLine("constructed in {0} ms", timer.ElapsedMilliseconds);
            Console.WriteLine("Celeste.Instance is the new object: " + ReferenceEquals(instanceField.GetValue(null), celeste));
            Console.WriteLine("title: \"{0}\"", titleField.GetValue(celeste));

            // Read it back with raw memory reads only, as the auto splitter does.
            ulong instance = Memory.ReadPointer(slot);
            ulong address = Memory.AddressOf(celeste);
            Console.WriteLine("slot holds 0x{0:x}, the object is at 0x{1:x}: {2}", instance, address, instance == address ? "same" : "DIFFERENT");
            ulong title = Memory.ReadPointer(instance + (ulong)titleOffset);
            Console.WriteLine("title read raw: \"{0}\"", ReadString(title));
            ulong info = Memory.ReadPointer(instance + (ulong)infoOffset);
            ulong infoAddress = Memory.AddressOf(infoField.GetValue(celeste));
            Console.WriteLine("AutoSplitterInfo read raw: 0x{0:x}, the object is at 0x{1:x}: {2}", info, infoAddress, info == infoAddress ? "same" : "DIFFERENT");
        }

        private static string ReadString(ulong address)
        {
            if (address == 0)
            {
                return null;
            }
            int length = Memory.ReadInt32(address + (ulong)stringLengthOffset);
            var chars = new char[Math.Max(0, Math.Min(length, 256))];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = Memory.ReadChar(address + (ulong)stringCharsOffset + (ulong)(2 * i));
            }
            return new string(chars);
        }

        private static ConstructorInfo Constructor() =>
            celesteType.GetConstructor(AnyInstance, null, Type.EmptyTypes, null) ?? throw new MissingMethodException("Celeste.Celeste", ".ctor");

        private static Type NeedType(string name) =>
            game.GetType(name) ?? throw new TypeLoadException("no type " + name);

        private static FieldInfo NeedField(Type type, string name, BindingFlags flags) =>
            type.GetField(name, flags) ?? throw new MissingFieldException(type.FullName, name);

        /// <summary>The type and its base classes, most derived first, without Object.</summary>
        private static IEnumerable<Type> Hierarchy(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                yield return t;
            }
        }

        private static string TypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.FullName ?? type.Name;
            }
            string name = type.GetGenericTypeDefinition().FullName;
            return name.Substring(0, name.IndexOf('`')) + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName).ToArray()) + ">";
        }
    }
}
