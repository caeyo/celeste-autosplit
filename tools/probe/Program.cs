using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;

namespace Probe
{
    /// <summary>
    /// Measures the memory layout the auto splitter reads, inside the runtime of the game flavour
    /// it runs with, and writes it as Rust, &lt;flavour&gt;.rs, for the auto splitter's memory.rs.
    /// Manifest.cs says what to measure. It also constructs the game, which the harness reads
    /// through the auto splitter, in replay mode. It has to run in the game's own runtime, from
    /// the game's folder: 32-bit .NET Framework on Windows, and the Mono bundled with the game on
    /// Linux and macOS, through the game's MonoKickstart launcher (which runs the .exe named after
    /// itself).
    ///
    /// Options:
    ///   --out DIR         writes DIR/&lt;flavour&gt;.rs once every check has passed.
    ///   --game PATH       the game's Celeste.exe, relative to the probe's directory (default:
    ///                     Celeste.exe next to the probe). Lets the probe itself be Celeste.exe,
    ///                     so its process has the game's name.
    ///   --replay          once every check has passed, writes distinctive values into the game
    ///                     and prints them, then writes the values it's sent, until its input
    ///                     ends: see Replay. The harness starts the probe this way.
    ///
    /// The game's Linux launcher script changes directory first, so pass an absolute --out path.
    /// </summary>
    internal static class Program
    {
        // The anchor's length isn't available, so a fixed amount is read, stopping early at an
        // unmapped page. This covers Celeste's constructor on every flavour: it's 0xb6 bytes on
        // Windows, 0x15a on Linux and 0x1e9 on macOS.
        private const int AnchorCodeLength = 768;

        private static string outDirectory;
        private static string gamePath;
        private static bool replay;

        private static string os;
        private static string flavour;
        private static Assembly game;
        private static string gameVersion;
        private static Root[] roots;
        private static long stringLength;
        private static long stringChars;
        /// <summary>Every path, once the game is constructed.</summary>
        private static List<HeldPath> held;

        private static int Main(string[] args)
        {
            try
            {
                ParseArguments(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine("usage: Probe.exe [--out DIR] [--game PATH] [--replay]");
                return 2;
            }

            // Each step needs the ones before it.
            bool ok = Step("runtime", DescribeRuntime)
                && Step("game", LoadGame)
                && Step("manifest", ResolveManifest)
                && Step("layout", MeasureLayout)
                && roots.All(root => Step("root " + root.Name, () => FindSlot(root)))
                && Step("construction", Construct)
                && Step("output", WriteLayout);

            Console.WriteLine();
            Console.WriteLine(ok ? "probe: ok" : "probe: failed");
            // Only a probe that passed replays, so that the game it holds is the one it measured.
            if (ok && replay && !Step("replay", Replay))
            {
                Console.WriteLine("probe: failed");
                ok = false;
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
                    case "--replay": replay = true; break;
                    default: throw new ArgumentException("unknown option " + args[i]);
                }
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
                AnchorName = entry.Anchor,
                Anchor = Names.Method(game, entry.Anchor),
                Signatures = entry.Signatures,
            };
            CheckObjectType(root.Field.FieldType, root.Name);
            Console.WriteLine("root {0} : {1}, anchored by {2}", root.Name, Names.TypeName(root.Field.FieldType), root.AnchorName);
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
            // Only values the probe can write, so that the harness checks every path.
            if (!IsValue(type))
            {
                throw new ProbeException(name + " ends in a " + Names.TypeName(type) + ", but the probe can only write a string, bool, int or long: another type needs sample values and parsing here, and checking in the harness");
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
            // SaveData has none, so it doesn't matter that the game may not have initialized it
            // when it first runs AutoSplitterInfo.Update.
            RuntimeHelpers.RunClassConstructor(root.Field.DeclaringType.TypeHandle);
            RuntimeHelpers.RunClassConstructor(root.Anchor.DeclaringType.TypeHandle);
            ulong slot = Memory.StaticFieldAddress(root.Field);
            Console.WriteLine("slot: 0x{0:x}", slot);

            // Once the method is compiled, both runtimes return its code here rather than a stub
            // that jumps to it. A stub would show in the dump, and the signature wouldn't match.
            RuntimeHelpers.PrepareMethod(root.Anchor.MethodHandle);
            ulong address = Memory.ToAddress(root.Anchor.MethodHandle.GetFunctionPointer());
            // Printed for debugging only: it holds addresses that change from run to run.
            byte[] code = Memory.ReadMapped(address, AnchorCodeLength);
            Console.WriteLine("anchor code at 0x{0:x} ({1} bytes read):", address, code.Length);
            MachineCode.HexDump(code, Console.WriteLine);

            List<SlotReference> references = MachineCode.FindSlotReferences(code, slot);
            Console.WriteLine("slot's address in the code: " + (references.Count == 0 ? "nowhere" : string.Join(", ", references.Select(r => r.ToString()).ToArray())));
            // Only now, so that a new root's first run prints the code to write its signature from.
            if (!root.Signatures.TryGetValue(flavour, out root.Signature))
            {
                throw new ProbeException(root.Name + " has no signature for " + flavour + " yet: write one from the code above, in Manifest.cs");
            }
            int[] matches = root.Signature.Code.Matches(code).ToArray();
            Console.WriteLine("signature {0}: {1}", root.Signature.Code, matches.Length == 0 ? "no match" : "matches at " + string.Join(", ", matches.Select(m => "+0x" + m.ToString("x")).ToArray()));
            if (matches.Length != 1)
            {
                throw new ProbeException(matches.Length == 0 ? "the signature doesn't match the anchor's code" : "the signature matches the anchor's code more than once");
            }

            // The reference is the earliest match of any reference pattern after the signature,
            // and must hold the slot's address.
            int after = matches[0] + root.Signature.Code.Length;
            var reference = root.Signature.References
                .Select(p => new { Pattern = p, Match = p.Matches(code, after).DefaultIfEmpty(-1).First() })
                .Where(r => r.Match >= 0)
                .OrderBy(r => r.Match)
                .FirstOrDefault()
                ?? throw new ProbeException("no reference pattern matches after the signature");
            Console.WriteLine("reference {0}: the earliest match, at +0x{1:x}, {2} bytes after the signature", reference.Pattern, reference.Match, reference.Match - after);
            ulong referenced = reference.Pattern.Slot(code, reference.Match);
            if (referenced != slot)
            {
                throw new ProbeException("the reference's address is 0x" + referenced.ToString("x") + ", but the slot is at 0x" + slot.ToString("x"));
            }
            Console.WriteLine("the reference's address is the slot's");
            int end = reference.Match + reference.Pattern.Length - after;
            if (end > Manifest.ReferenceWindow)
            {
                throw new ProbeException("the reference ends " + end + " bytes after the signature, but memory.rs only searches " + Manifest.ReferenceWindow + ": widen ReferenceWindow, in Manifest.cs");
            }
        }

        /// <summary>A path in the constructed game, with the object that holds its last field.</summary>
        private sealed class HeldPath
        {
            public Root Root;
            public FieldPath Path;
            public object Owner;
        }

        /// <summary>
        /// Constructs the game for real, as the game's Main does, and finds the object that holds
        /// each path's last field.
        /// </summary>
        private static void Construct()
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

            held = new List<HeldPath>();
            foreach (Root root in roots)
            {
                root.Object = root.Field.GetValue(null);
                if (root.Object == null)
                {
                    SetUp(root);
                }
                foreach (FieldPath path in root.Paths)
                {
                    object owner = root.Object;
                    foreach (FieldInfo field in path.Fields.Take(path.Fields.Length - 1))
                    {
                        owner = field.GetValue(owner) ?? throw new ProbeException(path.Name + ": " + field.Name + " is null after new Celeste()");
                    }
                    held.Add(new HeldPath { Root = root, Path = path, Owner = owner });
                }
            }
        }

        /// <summary>
        /// Sets up the object of a root that the game leaves null until later, as it does
        /// SaveData.Instance until a file starts: uninitialized, and stored in the static, so that
        /// the probe can write values into it, for the auto splitter to read.
        /// </summary>
        private static void SetUp(Root root)
        {
            root.Object = FormatterServices.GetUninitializedObject(root.Field.FieldType);
            GC.SuppressFinalize(root.Object);
            root.SetUp = true;
            root.Field.SetValue(null, root.Object);
            Console.WriteLine("{0} is null after new Celeste(), so the probe sets up an uninitialized {1}", root.Name, Names.TypeName(root.Field.FieldType));
        }

        /// <summary>
        /// A distinctive value for the n-th path, so that reading the wrong field, or the wrong
        /// width, can't give one that happens to match. Neighbouring flags differ, so two swapped
        /// ones would show. The harness then negates the numbers and flips the flags, so that each
        /// flag is seen both set and clear.
        /// </summary>
        private static object SampleValue(Type type, int n)
        {
            if (type == typeof(string)) return "path " + n;
            if (type == typeof(bool)) return n % 2 == 1;
            if (type == typeof(int)) return n * 0x01010101;
            return n * 0x0101010101010101L;
        }

        private static void WriteLayout()
        {
            string text = LayoutRust();
            Console.Write(text);
            if (outDirectory != null)
            {
                Directory.CreateDirectory(outDirectory);
                string file = Path.Combine(outDirectory, flavour + ".rs");
                File.WriteAllText(file, text, new UTF8Encoding(false));
                Console.WriteLine("written to " + file);
            }
        }

        /// <summary>
        /// Replay mode, for the harness. First writes distinctive values into every number and flag,
        /// and into strings that are still null, then prints "probe: values", and its flavour, the
        /// values the paths hold, and the roots it set up. Then writes what it's sent into the game,
        /// until its input ends. It comes in sets of lines, with an empty line at the end of each:
        /// - path=value writes the value through the path. The value is read as the path's type:
        ///   true or false, a whole number, or for a string, the rest of the line.
        /// - root=null stores null in the root's static, and root=object stores the root's object
        ///   in it again.
        /// Once a set is written, it prints "probe: written".
        /// </summary>
        private static void Replay()
        {
            // The lines only name paths, and two roots can have paths with the same name.
            var paths = new Dictionary<string, HeldPath>();
            foreach (HeldPath path in held)
            {
                if (paths.ContainsKey(path.Path.Name))
                {
                    throw new ProbeException("two roots have the path " + path.Path.Name + ", which the replay can't tell apart");
                }
                paths.Add(path.Path.Name, path);
                // Every number and flag gets a distinctive value, and a string only while it's
                // null: the title keeps "Celeste", which the auto splitter checks for.
                FieldInfo last = path.Path.Last;
                if (last.FieldType.IsValueType || last.GetValue(path.Owner) == null)
                {
                    last.SetValue(path.Owner, SampleValue(last.FieldType, paths.Count));
                }
            }

            Console.WriteLine("probe: values " + ValuesJson());
            Console.Out.Flush();
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var set = new List<Action>();
            for (string line = input.ReadLine(); line != null; line = input.ReadLine())
            {
                if (line.Length > 0)
                {
                    set.Add(ParseLine(line, paths));
                    continue;
                }
                foreach (Action write in set)
                {
                    write();
                }
                set.Clear();
                Console.WriteLine("probe: written");
                Console.Out.Flush();
            }
        }

        /// <summary>A line of a set, as what writes it.</summary>
        private static Action ParseLine(string line, Dictionary<string, HeldPath> paths)
        {
            int separator = line.IndexOf('=');
            if (separator < 0)
            {
                throw new ProbeException("can't write " + line + ": expected path=value, or root=null or object");
            }
            string name = line.Substring(0, separator);
            string text = line.Substring(separator + 1);
            Root root = roots.FirstOrDefault(r => r.Name == name);
            if (root != null)
            {
                switch (text)
                {
                    case "null": return () => root.Field.SetValue(null, null);
                    case "object": return () => root.Field.SetValue(null, root.Object);
                    default: throw new ProbeException("can't write " + line + ": a root takes null or object");
                }
            }
            if (!paths.TryGetValue(name, out HeldPath path))
            {
                throw new ProbeException("can't write " + line + ": " + name + " is none of the manifest's paths or roots");
            }
            Type type = path.Path.Last.FieldType;
            object value;
            try
            {
                value = ParseValue(type, text);
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                throw new ProbeException("can't write " + line + ": the path's type is " + Names.TypeName(type));
            }
            return () => path.Path.Last.SetValue(path.Owner, value);
        }

        private static object ParseValue(Type type, string text)
        {
            if (type == typeof(string)) return text;
            if (type == typeof(bool)) return bool.Parse(text);
            if (type == typeof(int)) return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// As JSON on one line, for the harness to compare the auto splitter with: the flavour, the
        /// values the paths hold now, as reflection reads them, keyed by root and then path, and the
        /// roots the probe set up.
        /// </summary>
        private static string ValuesJson()
        {
            IEnumerable<string> rootsJson = roots.Select(root =>
                Quote(root.Name) + ":{" +
                string.Join(",", held
                    .Where(p => p.Root == root)
                    .Select(p => Quote(p.Path.Name) + ":" + ValueJson(p.Path.Last.GetValue(p.Owner)))
                    .ToArray()) +
                "}");
            IEnumerable<string> setUpJson = roots.Where(r => r.SetUp).Select(r => Quote(r.Name));
            return "{\"flavour\":" + Quote(flavour)
                + ",\"roots\":{" + string.Join(",", rootsJson.ToArray()) + "}"
                + ",\"setUp\":[" + string.Join(",", setUpJson.ToArray()) + "]}";
        }

        private static bool IsValue(Type type) =>
            type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long);

        private static string ValueJson(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string s: return Quote(s);
                case bool b: return b ? "true" : "false";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// The layout as Rust, which memory.rs uses, always in this format, so that regenerated
        /// layouts only differ where values do. Lines end in \n on every OS.
        /// </summary>
        private static string LayoutRust()
        {
            var text = new StringBuilder();
            void Line(string line) => text.Append(line).Append('\n');

            Line("// Generated by the probe, through the Layouts workflow: don't edit it by hand. See");
            Line("// layouts/README.md.");
            Line("//");
            Line("// Flavour: " + flavour);
            Line("// Celeste.exe: version " + gameVersion + ", module version ID " + game.ManifestModule.ModuleVersionId);
            Line("");
            Line("/// As `asr::get_os` names it.");
            Line("pub const OS: &str = \"" + os + "\";");
            Line("/// In bytes.");
            Line("pub const POINTER_SIZE: u64 = " + IntPtr.Size + ";");
            Line("/// Where a string holds its length, an `i32`.");
            Line("pub const STRING_LENGTH: u64 = " + Hex(stringLength) + ";");
            Line("/// Where a string's UTF-16 characters start.");
            Line("pub const STRING_CHARS: u64 = " + Hex(stringChars) + ";");
            foreach (Root root in roots)
            {
                Line("");
                Line("/// `" + root.Name + "`: its slot is found from the compiled code of its anchor,");
                Line("/// `" + root.AnchorName + "`.");
                Line("pub mod " + ConstantName(root.Field.DeclaringType.Name + "." + root.Field.Name).ToLowerInvariant() + " {");
                Line("    /// Matches the code once: hex bytes, with `??` for any byte.");
                Line("    pub const SIGNATURE: &str = \"" + root.Signature.Code + "\";");
                Line("    /// A reference to the slot: the earliest match of any of these after the signature, with `SS`");
                Line("    /// for the slot's address.");
                Line("    pub const REFERENCES: &[&str] = &[");
                foreach (Pattern reference in root.Signature.References)
                {
                    Line("        \"" + reference + "\",");
                }
                Line("    ];");
                Line("    /// How many bytes after the signature are searched for the reference, which ends within them.");
                Line("    pub const REFERENCE_WINDOW: u64 = " + Manifest.ReferenceWindow + ";");
                Line("");
                Line("    // Each path's offsets from the object in the slot, and the name and type of each field");
                Line("    // along it.");
                Line("");
                foreach (FieldPath path in root.Paths)
                {
                    Line("    /// " + string.Join(", ", path.Fields.Select(f => f.Name + ": " + Names.TypeName(f.FieldType)).ToArray()));
                    Line("    pub const " + ConstantName(path.Name) + ": [u64; " + path.Offsets.Length + "] = [" + string.Join(", ", path.Offsets.Select(Hex).ToArray()) + "];");
                }
                Line("}");
            }
            return text.ToString();
        }

        /// <summary>
        /// The Rust constant named after dotted .NET names: AutoSplitterInfo.ChapterTime is
        /// AUTO_SPLITTER_INFO_CHAPTER_TIME. An acronym stays one word: a field named SIDStrPtr
        /// would be SID_STR_PTR.
        /// </summary>
        private static string ConstantName(string name)
        {
            var text = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '.')
                {
                    text.Append('_');
                    continue;
                }
                // A word starts at a capital after a lower-case letter or a digit, or at a
                // capital before a lower-case letter, after another capital.
                if (i > 0 && char.IsUpper(c)
                    && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])
                        || char.IsUpper(name[i - 1]) && i + 1 < name.Length && char.IsLower(name[i + 1])))
                {
                    text.Append('_');
                }
                text.Append(char.ToUpperInvariant(c));
            }
            return text.ToString();
        }

        private static string Hex(long value) => "0x" + value.ToString("x");

        /// <summary>
        /// A JSON string. The probe's strings are .NET names and short game values, which shouldn't
        /// need escaping, so it refuses one that would rather than escape it.
        /// </summary>
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
