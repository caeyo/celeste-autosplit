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
    internal static class Program
    {
        private const int AnchorCodeLength = 768;

        private static string gamePath;
        private static bool replay;

        private static string os;
        private static string flavour;
        private static string runtime;
        private static Assembly game;
        private static Root[] roots;
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
                Console.Error.WriteLine("usage: Probe.exe [--game PATH] [--replay]");
                return 2;
            }

            bool ok = Step("runtime", DescribeRuntime)
                && Step("game", LoadGame)
                && Step("manifest", ResolveManifest)
                && Step("layout", MeasureLayout)
                && roots.All(root => Step("root " + root.Name, () => FindSlot(root)))
                && Step("construction", Construct);

            Console.WriteLine();
            Console.WriteLine(ok ? "probe: ok" : "probe: failed");
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

            flavour = os != "windows" ? os
                : references.Any(a => a.Name == "FNA") ? "windows-fna"
                : references.Any(a => a.Name == "Microsoft.Xna.Framework.Game") ? "windows-xna"
                : throw new ProbeException("the game references neither FNA nor XNA");
            Console.WriteLine("flavour: " + flavour);
            runtime = os == "windows" ? "clr32" : "mono64";
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
                CodeField = entry.CodeField,
                Signatures = entry.Signatures,
            };
            CheckObjectType(root.Field.FieldType, root.Name);
            Console.WriteLine("root {0} : {1}, anchored by {2}", root.Name, Names.TypeName(root.Field.FieldType), root.AnchorName);
            root.Paths = entry.Paths.Select(p => ResolvePath(root, p)).ToArray();
            CheckUnique(root.Paths.Select(p => p.Name), " in the paths of " + root.Name);
            FieldPath other = root.Paths.FirstOrDefault(p => root.CodeField == null ? p.Fields.Length != 1 : p.Fields.Length != 2 || p.Fields[0].Name != root.CodeField);
            if (other != null)
            {
                throw new ProbeException(other.Name + " isn't a field of " + (root.CodeField ?? root.Name) + ": a layout holds one offset per path");
            }
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
            if (!IsValue(type))
            {
                throw new ProbeException(name + " ends in a " + Names.TypeName(type) + ", but the probe can only write a string, bool, int or long: another type needs sample values and parsing here, and checking in the harness");
            }
            Console.WriteLine("  {0} : {1}", name, Names.TypeName(type));
            return new FieldPath { Name = name, Fields = fields.ToArray() };
        }

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
            const string sample = "Celeste";
            FieldInfo[] stringFields = typeof(string).GetFields(Names.DeclaredInstance);
            long stringLength = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(int)), sample);
            long stringChars = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(char)), sample);
            if (stringChars != RuntimeHelpers.OffsetToStringData)
            {
                throw new ProbeException("string characters measured at 0x" + stringChars.ToString("x") + ", but OffsetToStringData is 0x" + RuntimeHelpers.OffsetToStringData.ToString("x"));
            }
            Console.WriteLine("string: length at 0x{0:x}, chars at 0x{1:x}", stringLength, stringChars);

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
                GC.SuppressFinalize(instance);
                instances.Add(type, instance);
            }
            return instance;
        }

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

        private static IEnumerable<Type> Hierarchy(Type type)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                yield return t;
            }
        }

        private static void FindSlot(Root root)
        {
            RuntimeHelpers.RunClassConstructor(root.Field.DeclaringType.TypeHandle);
            RuntimeHelpers.RunClassConstructor(root.Anchor.DeclaringType.TypeHandle);
            ulong slot = Memory.StaticFieldAddress(root.Field);
            Console.WriteLine("slot: 0x{0:x}", slot);

            RuntimeHelpers.PrepareMethod(root.Anchor.MethodHandle);
            ulong address = Memory.ToAddress(root.Anchor.MethodHandle.GetFunctionPointer());
            byte[] code = Memory.ReadMapped(address, AnchorCodeLength);
            Console.WriteLine("anchor code at 0x{0:x} ({1} bytes read):", address, code.Length);
            MachineCode.HexDump(code);

            List<SlotReference> references = MachineCode.FindSlotReferences(code, slot);
            Console.WriteLine("slot's address in the code: " + (references.Count == 0 ? "nowhere" : string.Join(", ", references.Select(r => r.ToString()).ToArray())));
            if (!root.Signatures.TryGetValue(runtime, out root.Signature))
            {
                throw new ProbeException(root.Name + " has no signature for " + runtime + " yet: write one from the code above, in Manifest.cs");
            }
            int[] matches = root.Signature.Code.Matches(code).ToArray();
            Console.WriteLine("signature {0}: {1}", root.Signature.Code, matches.Length == 0 ? "no match" : "matches at " + string.Join(", ", matches.Select(m => "+0x" + m.ToString("x")).ToArray()));
            if (matches.Length != 1)
            {
                throw new ProbeException(matches.Length == 0 ? "the signature doesn't match the anchor's code" : "the signature matches the anchor's code more than once");
            }

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

            if (root.CodeField == null)
            {
                return;
            }

            int fieldStore = root.Signature.FieldStore.Matches(code)
                .Where(m => m + root.Signature.FieldStore.Length <= matches[0])
                .DefaultIfEmpty(-1)
                .Last();
            if (fieldStore < 0)
            {
                throw new ProbeException("the store of " + root.CodeField + " doesn't match before the signature");
            }
            int distance = matches[0] - fieldStore;
            Console.WriteLine("store of {0} {1}: the last match, at +0x{2:x}, {3} bytes before the signature", root.CodeField, root.Signature.FieldStore, fieldStore, distance);
            ulong offset = root.Signature.FieldStore.Slot(code, fieldStore);
            long measured = root.Paths.First(p => p.Fields[0].Name == root.CodeField).Offsets[0];
            if ((long)offset != measured)
            {
                throw new ProbeException("the store's offset is 0x" + offset.ToString("x") + ", but " + root.CodeField + " is at 0x" + measured.ToString("x"));
            }
            Console.WriteLine("the store's offset is the field's");
            if (distance > Manifest.FieldStoreWindow)
            {
                throw new ProbeException("the store of " + root.CodeField + " starts " + distance + " bytes before the signature, but memory.rs only searches " + Manifest.FieldStoreWindow + ": widen FieldStoreWindow, in Manifest.cs");
            }
        }

        private sealed class HeldPath
        {
            public Root Root;
            public FieldPath Path;
            public object Owner;
        }

        private static void Construct()
        {
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
            GC.SuppressFinalize(celeste);
            Console.WriteLine("constructed in {0} ms", timer.ElapsedMilliseconds);
            Console.WriteLine("game version: " + Names.InstanceField(celeste.GetType(), "Version").GetValue(celeste));

            held = new List<HeldPath>();
            foreach (Root root in roots)
            {
                object value = root.Field.GetValue(null);
                if (value == null)
                {
                    value = FormatterServices.GetUninitializedObject(root.Field.FieldType);
                    GC.SuppressFinalize(value);
                    root.Field.SetValue(null, value);
                    Console.WriteLine("{0} is null after new Celeste(), so the probe sets up an uninitialized {1}", root.Name, Names.TypeName(root.Field.FieldType));
                }
                foreach (FieldPath path in root.Paths)
                {
                    object owner = value;
                    foreach (FieldInfo field in path.Fields.Take(path.Fields.Length - 1))
                    {
                        owner = field.GetValue(owner) ?? throw new ProbeException(path.Name + ": " + field.Name + " is null after new Celeste()");
                    }
                    held.Add(new HeldPath { Root = root, Path = path, Owner = owner });
                }
            }
        }

        private static object SampleValue(Type type, int n)
        {
            if (type == typeof(string)) return "path " + n;
            if (type == typeof(bool)) return n % 2 == 1;
            if (type == typeof(int)) return n * 0x01010101;
            return n * 0x0101010101010101L;
        }

        private static void Replay()
        {
            var paths = new Dictionary<string, HeldPath>();
            foreach (HeldPath path in held)
            {
                if (paths.ContainsKey(path.Path.Name))
                {
                    throw new ProbeException("two roots have the path " + path.Path.Name + ", which the replay can't tell apart");
                }
                paths.Add(path.Path.Name, path);
                FieldInfo last = path.Path.Last;
                if (last.FieldType.IsValueType || last.GetValue(path.Owner) == null)
                {
                    last.SetValue(path.Owner, SampleValue(last.FieldType, paths.Count));
                }
            }

            Console.WriteLine("probe: values " + ValuesJson());
            Console.Out.Flush();
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var set = new List<KeyValuePair<HeldPath, object>>();
            for (string line = input.ReadLine(); line != null; line = input.ReadLine())
            {
                if (line.Length > 0)
                {
                    set.Add(ParseLine(line, paths));
                    continue;
                }
                foreach (KeyValuePair<HeldPath, object> entry in set)
                {
                    entry.Key.Path.Last.SetValue(entry.Key.Owner, entry.Value);
                }
                set.Clear();
                Console.WriteLine("probe: written");
                Console.Out.Flush();
            }
        }

        private static KeyValuePair<HeldPath, object> ParseLine(string line, Dictionary<string, HeldPath> paths)
        {
            int separator = line.IndexOf('=');
            if (separator < 0 || !paths.TryGetValue(line.Substring(0, separator), out HeldPath path))
            {
                throw new ProbeException("can't write " + line + ": expected path=value, for one of the manifest's paths");
            }
            string text = line.Substring(separator + 1);
            Type type = path.Path.Last.FieldType;
            try
            {
                return new KeyValuePair<HeldPath, object>(path, ParseValue(type, text));
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                throw new ProbeException("can't write " + line + ": the path's type is " + Names.TypeName(type));
            }
        }

        private static object ParseValue(Type type, string text)
        {
            if (type == typeof(string)) return text;
            if (type == typeof(bool)) return bool.Parse(text);
            if (type == typeof(int)) return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        private static string ValuesJson()
        {
            IEnumerable<string> rootsJson = roots.Select(root =>
                Quote(root.Name) + ":{" +
                string.Join(",", held
                    .Where(p => p.Root == root)
                    .Select(p => Quote(p.Path.Name) + ":" + ValueJson(p.Path.Last.GetValue(p.Owner)))
                    .ToArray()) +
                "}");
            return "{\"flavour\":" + Quote(flavour) + ",\"roots\":{" + string.Join(",", rootsJson.ToArray()) + "}}";
        }

        private static bool IsValue(Type type) =>
            type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long);

        private static string ValueJson(object value)
        {
            switch (value)
            {
                case string s: return Quote(s);
                case bool b: return b ? "true" : "false";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

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
