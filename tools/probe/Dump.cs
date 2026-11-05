using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Probe
{
    internal static class Dump
    {
        private const int AnchorCodeLength = 768;

        private static readonly string[] Types =
        {
            "Celeste.Celeste",
            "Celeste.AutoSplitterInfo",
            "Celeste.SaveData",
            "Celeste.Session",
            "Celeste.AreaStats",
            "Celeste.AreaModeStats",
        };

        public static void Run(Assembly game)
        {
            Console.WriteLine("pointer size: " + IntPtr.Size);
            const string sample = "Celeste";
            FieldInfo[] stringFields = typeof(string).GetFields(Game.DeclaredInstance);
            long stringLength = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(int)), sample);
            long stringChars = Memory.FieldOffset(stringFields.Single(f => f.FieldType == typeof(char)), sample);
            Console.WriteLine("string: length at 0x{0:x}, chars at 0x{1:x}", stringLength, stringChars);

            foreach (string name in Types)
            {
                PrintFields(Game.Type(game, name));
            }

            for (int i = 0; i < Game.Anchors.GetLength(0); i++)
            {
                PrintAnchor(game, Game.Anchors[i, 0], Game.Anchors[i, 1]);
            }
        }

        private static void PrintFields(Type type)
        {
            object instance = FormatterServices.GetUninitializedObject(type);
            GC.SuppressFinalize(instance);
            Console.WriteLine(string.Join(" : ", Hierarchy(type).Select(t => t.FullName + " [" + t.Assembly.GetName().Name + "]").ToArray()));
            var fields = Hierarchy(type)
                .SelectMany(t => t.GetFields(Game.DeclaredInstance))
                .Select(f => new { Field = f, Offset = Memory.FieldOffset(f, instance) })
                .OrderBy(e => e.Offset);
            foreach (var entry in fields)
            {
                Console.WriteLine("  0x{0:x3}  {1}.{2} : {3}", entry.Offset, entry.Field.DeclaringType.Name, entry.Field.Name, TypeName(entry.Field.FieldType));
            }
        }

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
            string name = string.Join("+", type.GetGenericTypeDefinition().FullName.Split('+').Select(part => part.Split('`')[0]).ToArray());
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName).ToArray()) + ">";
        }

        private static void PrintAnchor(Assembly game, string anchorName, string slotName)
        {
            FieldInfo slotField = Game.StaticField(game, slotName);
            MethodBase anchor = Game.Method(game, anchorName);
            IntPtr code = Game.Prepare(slotField, anchor);
            Console.WriteLine("anchor {0}, slot {1}", anchorName, slotName);
            ulong slot = Memory.StaticFieldAddress(slotField);
            Console.WriteLine("slot: 0x{0:x}", slot);

            ulong address = Memory.ToAddress(code);
            byte[] bytes = Memory.ReadMapped(address, AnchorCodeLength);
            Console.WriteLine("anchor code at 0x{0:x} ({1} bytes read):", address, bytes.Length);
            MachineCode.HexDump(bytes);

            List<SlotReference> references = MachineCode.FindSlotReferences(bytes, slot);
            Console.WriteLine("slot's address in the code: " + (references.Count == 0 ? "nowhere" : string.Join(", ", references.Select(r => r.ToString()).ToArray())));
        }
    }
}
