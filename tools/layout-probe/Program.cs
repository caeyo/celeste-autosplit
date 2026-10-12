using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace LayoutProbe
{
    /// <summary>
    /// Measures the memory layout the auto splitter reads, inside the runtime of the game flavour
    /// it runs with, and writes it as &lt;flavour&gt;.json. Manifest.cs says what to measure. It
    /// has to run in the game's own runtime, from the game's folder: 32-bit .NET Framework on
    /// Windows, and the Mono bundled with the game on Linux and macOS, through the game's
    /// MonoKickstart launcher (which runs the .exe named after itself).
    ///
    /// Options:
    ///   --out DIR         writes DIR/&lt;flavour&gt;.json once every check has passed.
    ///   --game PATH       the game's Celeste.exe, relative to the probe's directory (default:
    ///                     Celeste.exe next to the probe). Lets the probe itself be Celeste.exe,
    ///                     so its process has the game's name.
    ///   --hold SECONDS    once every check has passed, stays alive, so the process can be read
    ///                     from outside.
    ///
    /// The game's Linux launcher script changes directory first, so pass an absolute --out.
    /// </summary>
    internal static class Program
    {
        // The anchor's length isn't available, so a fixed amount is read, stopping early at an
        // unmapped page. This covers Celeste's constructor on every flavour: it's 0xb6 bytes on
        // Windows, 0x15a on Linux and 0x1e9 on macOS.
        private const int AnchorCodeLength = 768;

        private static string outDirectory;
        private static string gamePath;
        private static int holdSeconds;

        private static string os;
        private static string flavour;
        private static Assembly game;
        private static string gameVersion;
        private static Root[] roots;
        private static long stringLength;
        private static long stringChars;

        private static int Main(string[] args)
        {
            try
            {
                ParseArguments(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: LayoutProbe.exe [--out DIR] [--game PATH] [--hold SECONDS]");
                return 2;
            }

            // Each step needs the ones before it.
            bool ok = Step("runtime", DescribeRuntime)
                && Step("game", LoadGame)
                && Step("manifest", ResolveManifest)
                && Step("layout", MeasureLayout)
                && roots.All(root => Step("root " + root.Name, () => FindSlot(root)))
                && Step("live validation", ValidateLive)
                && Step("output", WriteLayout);

            Console.WriteLine();
            Console.WriteLine(ok ? "probe: ok" : "probe: failed");
            // Only a probe that passed holds, so that holding means its values can be read.
            if (ok && holdSeconds > 0)
            {
                Console.WriteLine("holding for {0} s", holdSeconds);
                Console.Out.Flush();
                Thread.Sleep(holdSeconds * 1000);
            }
            return ok ? 0 : 1;
        }

        private static void ParseArguments(string[] args)
        {
            string gameFile = "Celeste.exe";
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": outDirectory = Path.GetFullPath(args[++i]); break;
                    case "--game": gameFile = args[++i]; break;
                    case "--hold": holdSeconds = int.Parse(args[++i]); break;
                    default: throw new ArgumentException("unknown option " + args[i]);
                }
            }
            if (holdSeconds < 0 || holdSeconds > int.MaxValue / 1000)
            {
                throw new ArgumentException("--hold takes 0 to " + int.MaxValue / 1000 + " seconds");
            }
            gamePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, gameFile));
        }

        private static bool Step(string name, Action body)
        {
            Console.WriteLine();
            Console.WriteLine("== " + name);
            try
            {
                body();
                return true;
            }
            catch (Exception e)
            {
                // The probe's own failures explain themselves. Anything else gets its stack trace.
                Console.WriteLine("FAILED: " + (e is ProbeException ? e.Message : e.ToString()));
                return false;
            }
            finally
            {
                Console.Out.Flush();
            }
        }

        private static void DescribeRuntime()
        {
            os = DetectOs();
            Console.WriteLine("os: {0} ({1})", os, Environment.OSVersion);
            Console.WriteLine("pointer size: " + IntPtr.Size);
            Type mono = Type.GetType("Mono.Runtime");
            if (mono != null)
            {
                MethodInfo displayName = mono.GetMethod("GetDisplayName", Names.AnyStatic);
                Console.WriteLine("runtime: Mono " + (displayName == null ? "(unknown version)" : displayName.Invoke(null, null)));
            }
            else
            {
                Console.WriteLine("runtime: .NET Framework " + FileVersionInfo.GetVersionInfo(typeof(object).Assembly.Location).FileVersion);
            }
            Console.WriteLine("probe executable: " + Assembly.GetEntryAssembly().Location);
        }

        private static string DetectOs()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return "windows";
            }
            // Mono reports Unix on both Linux and macOS, so ask the kernel. The struct uname
            // fills starts with the kernel's name on both.
            IntPtr buffer = Marshal.AllocHGlobal(4096);
            try
            {
                if (uname(buffer) != 0)
                {
                    throw new ProbeException("uname failed");
                }
                string kernel = Marshal.PtrToStringAnsi(buffer);
                switch (kernel)
                {
                    case "Linux": return "linux";
                    case "Darwin": return "macos";
                    default: throw new ProbeException("unsupported kernel " + kernel);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static void LoadGame()
        {
            game = Assembly.LoadFrom(gamePath);
            AssemblyName[] references = game.GetReferencedAssemblies();
            Console.WriteLine("game: " + gamePath);
            Console.WriteLine("assembly: " + game.FullName);
            Console.WriteLine("mvid: " + game.ManifestModule.ModuleVersionId);
            Console.WriteLine("references: " + string.Join(", ", references.Select(a => a.Name + " " + a.Version).OrderBy(n => n).ToArray()));

            // Linux and macOS have one flavour each. On Windows, the game is built on XNA or FNA.
            flavour = os != "windows" ? os
                : references.Any(a => a.Name == "FNA") ? "windows-fna"
                : references.Any(a => a.Name == "Microsoft.Xna.Framework.Game") ? "windows-xna"
                : throw new ProbeException("the game references neither FNA nor XNA");
            Console.WriteLine("flavour: " + flavour);
        }

        private static void ResolveManifest()
        {
            roots = Manifest.Roots.Select(ResolveRoot).ToArray();
            CheckUnique(roots.Select(r => r.Name), "");
        }

        private static Root ResolveRoot(ManifestRoot entry)
        {
            var root = new Root
            {
                Name = entry.Field,
                Field = Names.StaticField(game, entry.Field),
                Anchor = Names.Method(game, entry.Anchor),
                Signatures = entry.Signatures,
            };
            CheckObjectType(root.Field.FieldType, root.Name);
            Console.WriteLine("root {0} : {1}, anchored by {2}", root.Name, Names.TypeName(root.Field.FieldType), entry.Anchor);
            root.Paths = entry.Paths.Select(p => ResolvePath(root, p)).ToArray();
            CheckUnique(root.Paths.Select(p => p.Name), " in the paths of " + root.Name);
            return root;
        }

        private static FieldPath ResolvePath(Root root, string name)
        {
            var fields = new List<FieldInfo>();
            Type type = root.Field.FieldType;
            foreach (string hop in name.Split('.'))
            {
                if (fields.Count > 0)
                {
                    CheckObjectType(type, name + " at " + fields.Last().Name);
                }
                FieldInfo field = Names.InstanceField(type, hop);
                fields.Add(field);
                type = field.FieldType;
            }
            if (type.IsValueType && type != typeof(bool) && type != typeof(int) && type != typeof(long))
            {
                throw new ProbeException(name + " ends in a " + type + ", which the probe can't check yet: add a raw read and sample values for it");
            }
            Console.WriteLine("  {0} : {1}", name, Names.TypeName(type));
            return new FieldPath { Name = name, Fields = fields.ToArray() };
        }

        /// <summary>Paths go through objects, whose fields the probe measures on an instance.</summary>
        private static void CheckObjectType(Type type, string where)
        {
            if (type.IsValueType || type.IsAbstract || type.IsArray)
            {
                throw new ProbeException(where + " is a " + type + ", and paths only go through objects of concrete classes");
            }
        }

        private static void CheckUnique(IEnumerable<string> names, string where)
        {
            string duplicate = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (duplicate != null)
            {
                throw new ProbeException("the manifest lists " + duplicate + " twice" + where);
            }
        }

        private static void MeasureLayout()
        {
            // A string's length and first character are fields, so they're measured like any
            // other. The characters must start where the runtime says string data does.
            const string sample = "Celeste";
            FieldInfo[] stringFields = typeof(string).GetFields(Names.DeclaredInstance);
            stringLength = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(int)), sample);
            stringChars = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(char)), sample);
            if (stringChars != RuntimeHelpers.OffsetToStringData)
            {
                throw new ProbeException("string characters measured at 0x" + stringChars.ToString("x") + ", but OffsetToStringData is 0x" + RuntimeHelpers.OffsetToStringData.ToString("x"));
            }
            Console.WriteLine("string: length at 0x{0:x}, chars at 0x{1:x}", stringLength, stringChars);

            // Offsets need an instance of each type along the paths, but not a constructed one.
            var instances = new Dictionary<Type, object>();
            foreach (Root root in roots)
            {
                foreach (FieldPath path in root.Paths)
                {
                    path.Offsets = new long[path.Fields.Length];
                    Type owner = root.Field.FieldType;
                    for (int i = 0; i < path.Fields.Length; i++)
                    {
                        path.Offsets[i] = Memory.FieldOffset(path.Fields[i], Instance(owner, instances));
                        owner = path.Fields[i].FieldType;
                    }
                }
            }

            foreach (KeyValuePair<Type, object> entry in instances)
            {
                PrintFields(entry.Key, entry.Value);
            }
            foreach (Root root in roots)
            {
                Console.WriteLine("paths from {0}:", root.Name);
                foreach (FieldPath path in root.Paths)
                {
                    Console.WriteLine("  {0}: {1}", path.Name, string.Join(", ", path.Offsets.Select(o => "0x" + o.ToString("x")).ToArray()));
                }
            }
        }

        private static object Instance(Type type, Dictionary<Type, object> instances)
        {
            if (!instances.TryGetValue(type, out object instance))
            {
                instance = FormatterServices.GetUninitializedObject(type);
                // The game's finalizers would clean up after these half-made objects.
                GC.SuppressFinalize(instance);
                instances.Add(type, instance);
            }
            return instance;
        }

        /// <summary>Every instance field of the type, for reference when adding paths.</summary>
        private static void PrintFields(Type type, object instance)
        {
            Console.WriteLine(string.Join(" : ", Hierarchy(type).Select(t => t.FullName + " [" + t.Assembly.GetName().Name + "]").ToArray()));
            var fields = Hierarchy(type)
                .SelectMany(t => t.GetFields(Names.DeclaredInstance))
                .Select(f => new { Field = f, Offset = Memory.FieldOffset(f, instance) })
                .OrderBy(e => e.Offset);
            foreach (var entry in fields)
            {
                Console.WriteLine("  0x{0:x3}  {1}.{2} : {3}", entry.Offset, entry.Field.DeclaringType.Name, entry.Field.Name, Names.TypeName(entry.Field.FieldType));
            }
        }

        /// <summary>The type and its base classes, most derived first, without Object.</summary>
        private static IEnumerable<Type> Hierarchy(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                yield return t;
            }
        }

        private static void FindSlot(Root root)
        {
            // Run the class constructors first, as the game has by the time it runs the anchor:
            // the JIT may compile static accesses differently before a class is initialized.
            RuntimeHelpers.RunClassConstructor(root.Field.DeclaringType.TypeHandle);
            RuntimeHelpers.RunClassConstructor(root.Anchor.DeclaringType.TypeHandle);
            root.Slot = Memory.StaticFieldAddress(root.Field);
            Console.WriteLine("slot: 0x{0:x}", root.Slot);

            // Once the method is compiled, both runtimes return its code here rather than a stub
            // that jumps to it. A stub would show in the dump, and the signature wouldn't match.
            RuntimeHelpers.PrepareMethod(root.Anchor.MethodHandle);
            root.Code = Memory.ToAddress(root.Anchor.MethodHandle.GetFunctionPointer());
            // Printed for debugging only: it holds addresses that change from run to run.
            byte[] code = Memory.ReadMapped(root.Code, AnchorCodeLength);
            Console.WriteLine("anchor code at 0x{0:x} ({1} bytes read):", root.Code, code.Length);
            MachineCode.HexDump(code, Console.WriteLine);

            List<SlotReference> references = MachineCode.FindSlotReferences(code, root.Slot);
            Console.WriteLine("slot's address in the code: " + (references.Count == 0 ? "nowhere" : string.Join(", ", references.Select(r => r.ToString()).ToArray())));
            // Only now, so that a new root's first run prints the code to write its signature from.
            if (!root.Signatures.TryGetValue(flavour, out root.Signature))
            {
                throw new ProbeException(root.Name + " has no signature for " + flavour + " yet: write one from the code above, in Manifest.cs and in memory.rs");
            }
            int[] matches = root.Signature.Code.Matches(code).ToArray();
            Console.WriteLine("signature {0}: {1}", root.Signature.Code, matches.Length == 0 ? "no match" : "matches at " + string.Join(", ", matches.Select(m => "+0x" + m.ToString("x")).ToArray()));
            if (matches.Length != 1)
            {
                throw new ProbeException(matches.Length == 0 ? "the signature doesn't match the anchor's code" : "the signature matches the anchor's code more than once");
            }
            root.Match = matches[0];

            // The store is the earliest match of any store pattern after the signature, and must
            // hold the slot's address.
            int after = root.Match + root.Signature.Code.Length;
            var store = root.Signature.Store
                .Select(p => new { Pattern = p, Match = p.Matches(code, after).DefaultIfEmpty(-1).First() })
                .Where(s => s.Match >= 0)
                .OrderBy(s => s.Match)
                .FirstOrDefault()
                ?? throw new ProbeException("no store pattern matches after the signature");
            root.Store = store.Pattern;
            root.StoreMatch = store.Match;
            Console.WriteLine("store {0}: the earliest match, at +0x{1:x}, {2} bytes after the signature", root.Store, root.StoreMatch, root.StoreMatch - after);
            ulong slot = root.Store.Slot(code, root.StoreMatch);
            if (slot != root.Slot)
            {
                throw new ProbeException("the store's address is 0x" + slot.ToString("x") + ", but the slot is at 0x" + root.Slot.ToString("x"));
            }
            Console.WriteLine("the store's address is the slot's");
            int end = root.StoreMatch + root.Store.Length - after;
            if (end > Manifest.StoreWindow)
            {
                throw new ProbeException("the store ends " + end + " bytes after the signature, but memory.rs only searches " + Manifest.StoreWindow + ": widen its STORE_WINDOW, and StoreWindow in Manifest.cs");
            }
        }

        private sealed class PathCheck
        {
            public Root Root;
            public FieldPath Path;
            /// <summary>The object holding the path's last field.</summary>
            public object Owner;
            public bool Writable;
            public int Number;
        }

        /// <summary>
        /// Constructs the game for real, then reads every path back raw, the way the auto splitter
        /// does: from the slot whose address the store after the signature holds.
        /// </summary>
        private static void ValidateLive()
        {
            // Main loads the settings before constructing the game, and the constructor reads
            // Settings.Instance.Fullscreen and VSync.
            FieldInfo settings = Names.StaticField(game, "Celeste.Settings::Instance");
            if (settings.GetValue(null) == null)
            {
                settings.SetValue(null, Activator.CreateInstance(settings.FieldType, true));
            }

            var timer = Stopwatch.StartNew();
            object celeste;
            try
            {
                celeste = Activator.CreateInstance(Names.Type(game, "Celeste.Celeste"), true);
            }
            catch (TargetInvocationException e)
            {
                throw new Exception("new Celeste() threw", e.InnerException);
            }
            // Nothing disposes it before the process exits.
            GC.SuppressFinalize(celeste);
            Console.WriteLine("constructed in {0} ms", timer.ElapsedMilliseconds);
            gameVersion = Names.InstanceField(celeste.GetType(), "Version").GetValue(celeste).ToString();
            Console.WriteLine("game version: " + gameVersion);

            // Every number and flag gets distinctive values, so that a wrong offset can't read back
            // one that happens to match. Strings get one only while null: the title keeps
            // "Celeste", which the auto splitter checks for. Other objects keep theirs.
            var checks = new List<PathCheck>();
            foreach (Root root in roots)
            {
                object value = root.Field.GetValue(null) ?? throw new ProbeException(root.Name + " is still null after new Celeste(), so it can't be read back");
                foreach (FieldPath path in root.Paths)
                {
                    object owner = value;
                    foreach (FieldInfo field in path.Fields.Take(path.Fields.Length - 1))
                    {
                        owner = field.GetValue(owner) ?? throw new ProbeException(path.Name + ": " + field.Name + " is null after new Celeste()");
                    }
                    Type type = path.Last.FieldType;
                    object current = path.Last.GetValue(owner);
                    bool writable = type.IsValueType || current == null;
                    if (writable && !type.IsValueType && type != typeof(string))
                    {
                        throw new ProbeException(path.Name + " is null after new Celeste(), and the probe can't make a " + type + " to read back");
                    }
                    checks.Add(new PathCheck { Root = root, Path = path, Owner = owner, Writable = writable, Number = checks.Count + 1 });
                }
            }

            var mismatches = new List<string>();
            for (int pass = 1; pass <= 2; pass++)
            {
                Console.WriteLine("pass {0}:", pass);
                foreach (PathCheck check in checks.Where(c => c.Writable))
                {
                    check.Path.Last.SetValue(check.Owner, SampleValue(check.Path.Last.FieldType, check.Number, pass));
                }
                // Right after a collection, the few small allocations below won't start another.
                // Another thread's could, but a moved object only makes a comparison fail, never
                // pass wrongly.
                GC.Collect();
                foreach (Root root in roots)
                {
                    // The slot and the object in it are checked before anything is read through
                    // them, so a wrong address fails here instead of crashing the process.
                    ulong slot = SlotFromCode(root);
                    if (slot != root.Slot)
                    {
                        Console.WriteLine("  {0}: the store's address is 0x{1:x}, but the slot is at 0x{2:x}", root.Name, slot, root.Slot);
                        mismatches.Add(root.Name);
                        continue;
                    }
                    ulong instance = Memory.ReadPointer(slot);
                    ulong expected = Memory.AddressOf(root.Field.GetValue(null));
                    if (instance != expected)
                    {
                        Console.WriteLine("  {0}: the slot holds 0x{1:x}, but the object is at 0x{2:x}", root.Name, instance, expected);
                        mismatches.Add(root.Name);
                        continue;
                    }
                    Console.WriteLine("  {0}: the store's address is the slot, which holds the object at 0x{1:x}", root.Name, instance);
                    foreach (PathCheck check in checks.Where(c => c.Root == root))
                    {
                        object value = Expected(check);
                        object raw = ReadPath(instance, check.Path);
                        bool same = Equals(value, raw);
                        Console.WriteLine("  {0} = {1}{2}", check.Path.Name, Describe(value), same ? "" : ", but reads " + Describe(raw) + " raw");
                        if (!same)
                        {
                            mismatches.Add(check.Path.Name);
                        }
                    }
                }
            }
            if (mismatches.Count > 0)
            {
                throw new ProbeException("raw reads disagree with reflection for " + string.Join(", ", mismatches.Distinct().ToArray()));
            }
            Console.WriteLine("every path reads back raw");
        }

        /// <summary>
        /// A distinctive value for the n-th path. The second pass negates numbers and flips flags,
        /// so each flag is seen both set and clear. Neighbouring flags differ, so two swapped
        /// ones would show.
        /// </summary>
        private static object SampleValue(Type type, int n, int pass)
        {
            int sign = pass == 1 ? 1 : -1;
            if (type == typeof(string)) return "path " + n + " pass " + pass;
            if (type == typeof(bool)) return (n + pass) % 2 == 0;
            if (type == typeof(int)) return sign * n * 0x01010101;
            if (type == typeof(long)) return sign * n * 0x0101010101010101L;
            throw new ProbeException("no sample value for " + type);
        }

        /// <summary>The path's value as reflection sees it. Objects other than strings are compared by address.</summary>
        private static object Expected(PathCheck check)
        {
            Type type = check.Path.Last.FieldType;
            object value = check.Path.Last.GetValue(check.Owner);
            return type.IsValueType || type == typeof(string) ? value : (object)Memory.AddressOf(value);
        }

        /// <summary>The slot's address, read raw from the store in the anchor's code.</summary>
        private static ulong SlotFromCode(Root root) =>
            root.Store.Slot(Memory.ReadMapped(root.Code + (ulong)root.StoreMatch, root.Store.Length), 0);

        /// <summary>Reads a path raw from its root's object, through the measured offsets.</summary>
        private static object ReadPath(ulong instance, FieldPath path)
        {
            ulong address = instance;
            for (int i = 0; i < path.Fields.Length - 1; i++)
            {
                address = Memory.ReadPointer(address + (ulong)path.Offsets[i]);
                if (address == 0)
                {
                    return null;
                }
            }
            address += (ulong)path.Offsets.Last();
            Type type = path.Last.FieldType;
            if (type == typeof(string)) return ReadString(Memory.ReadPointer(address));
            if (type == typeof(bool)) return Memory.ReadByte(address) != 0;
            if (type == typeof(int)) return Memory.ReadInt32(address);
            if (type == typeof(long)) return Memory.ReadInt64(address);
            return Memory.ReadPointer(address);
        }

        /// <summary>Reads a string raw, through the measured offsets of its length and characters.</summary>
        private static string ReadString(ulong address)
        {
            if (address == 0)
            {
                return null;
            }
            int length = Memory.ReadInt32(address + (ulong)stringLength);
            if (length < 0 || length > 1024)
            {
                return "(a string of length " + length + ")";
            }
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = Memory.ReadChar(address + (ulong)stringChars + (ulong)(2 * i));
            }
            return new string(chars);
        }

        private static string Describe(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return "\"" + s + "\"";
                case bool b: return b ? "true" : "false";
                case ulong address: return "the object at 0x" + address.ToString("x");
                default: return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static void WriteLayout()
        {
            string text = LayoutJson();
            Console.Write(text);
            if (outDirectory != null)
            {
                Directory.CreateDirectory(outDirectory);
                string file = Path.Combine(outDirectory, flavour + ".json");
                File.WriteAllText(file, text, new UTF8Encoding(false));
                Console.WriteLine("written to " + file);
            }
        }

        /// <summary>
        /// The layout as JSON, always in this format, so that regenerated layouts only differ
        /// where values do. Every path goes on one line.
        /// </summary>
        private static string LayoutJson()
        {
            IEnumerable<string> rootsJson = roots.Select(root =>
                "    " + Quote(root.Name) + ": {\n" +
                "      \"signature\": " + Quote(root.Signature.Code.ToString()) + ",\n" +
                "      \"store\": [" + string.Join(", ", root.Signature.Store.Select(p => Quote(p.ToString())).ToArray()) + "],\n" +
                "      \"paths\": {\n" +
                string.Join(",\n", root.Paths.Select(path =>
                    "        " + Quote(path.Name) + ": { " +
                    "\"offsets\": [" + string.Join(", ", path.Offsets.Select(o => o.ToString()).ToArray()) + "], " +
                    "\"types\": [" + string.Join(", ", path.Fields.Select(f => Quote(Names.TypeName(f.FieldType))).ToArray()) + "] }").ToArray()) + "\n" +
                "      }\n" +
                "    }");
            return "{\n" +
                "  \"flavour\": " + Quote(flavour) + ",\n" +
                "  \"os\": " + Quote(os) + ",\n" +
                "  \"game\": { \"version\": " + Quote(gameVersion) + ", \"mvid\": " + Quote(game.ManifestModule.ModuleVersionId.ToString()) + " },\n" +
                "  \"pointer_size\": " + IntPtr.Size + ",\n" +
                "  \"string\": { \"length\": " + stringLength + ", \"chars\": " + stringChars + " },\n" +
                "  \"roots\": {\n" +
                string.Join(",\n", rootsJson.ToArray()) + "\n" +
                "  }\n" +
                "}\n";
        }

        /// <summary>A JSON string. The layout's strings are .NET names, which have nothing to escape.</summary>
        private static string Quote(string s)
        {
            if (s.Any(c => c == '"' || c == '\\' || c < ' '))
            {
                throw new ProbeException("can't write " + s + " as JSON without escaping it");
            }
            return "\"" + s + "\"";
        }

        [DllImport("libc")]
        private static extern int uname(IntPtr buffer);
    }
}
