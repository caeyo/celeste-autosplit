using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Probe
{
    internal sealed class Pattern
    {
        private const int AnyByte = -1;
        private const int SlotByte = -2;

        private readonly int[] bytes;

        public readonly int SlotOffset;

        public readonly int SlotWidth;

        public Pattern(string pattern)
        {
            bytes = pattern.Split(' ').Select(b => b == "??" ? AnyByte : b == "SS" ? SlotByte : Convert.ToInt32(b, 16)).ToArray();
            SlotOffset = Array.IndexOf(bytes, SlotByte);
            SlotWidth = bytes.Count(b => b == SlotByte);
            if (SlotWidth != 0 && (SlotWidth != 4 && SlotWidth != 8 || bytes.Skip(SlotOffset).Take(SlotWidth).Any(b => b != SlotByte)))
            {
                throw new ArgumentException(pattern + ": SS should be 4 or 8 bytes in a row");
            }
        }

        public int Length => bytes.Length;

        public IEnumerable<int> Matches(byte[] code, int from = 0)
        {
            for (int start = from; start + bytes.Length <= code.Length; start++)
            {
                int i = 0;
                while (i < bytes.Length && (bytes[i] < 0 || bytes[i] == code[start + i]))
                {
                    i++;
                }
                if (i == bytes.Length)
                {
                    yield return start;
                }
            }
        }

        public ulong Slot(byte[] code, int match) =>
            SlotWidth == 4 ? BitConverter.ToUInt32(code, match + SlotOffset) : BitConverter.ToUInt64(code, match + SlotOffset);

        public override string ToString() =>
            string.Join(" ", bytes.Select(b => b == AnyByte ? "??" : b == SlotByte ? "SS" : b.ToString("x2")).ToArray());
    }

    internal sealed class Signature
    {
        public readonly Pattern Code;
        public readonly Pattern FieldStore;
        public readonly Pattern[] References;

        public Signature(string code, string fieldStore, params string[] references)
        {
            Code = new Pattern(code);
            FieldStore = fieldStore == null ? null : new Pattern(fieldStore);
            References = references.Select(r => new Pattern(r)).ToArray();
            if (Code.SlotWidth != 0 || FieldStore != null && FieldStore.SlotWidth != 4 || References.Length == 0 || References.Any(r => r.SlotWidth == 0))
            {
                throw new ArgumentException(code + ": the signature has no SS, the field's store has 4, and each reference pattern has them");
            }
        }
    }

    internal sealed class SlotReference
    {
        public int Offset;
        public int Width;

        public override string ToString() => "+0x" + Offset.ToString("x") + " (" + Width + " bytes)";
    }

    internal static class MachineCode
    {
        public static List<SlotReference> FindSlotReferences(byte[] code, ulong slot)
        {
            var references = new List<SlotReference>();
            for (int i = 0; i + 4 <= code.Length; i++)
            {
                if (slot <= uint.MaxValue && BitConverter.ToUInt32(code, i) == slot)
                {
                    references.Add(new SlotReference { Offset = i, Width = 4 });
                }
                if (IntPtr.Size == 8 && i + 8 <= code.Length && BitConverter.ToUInt64(code, i) == slot)
                {
                    references.Add(new SlotReference { Offset = i, Width = 8 });
                }
            }
            return references;
        }

        public static void HexDump(byte[] bytes, Action<string> log)
        {
            for (int line = 0; line < bytes.Length; line += 16)
            {
                log(string.Format("  {0:x4}: {1}", line, Hex(bytes, line, Math.Min(16, bytes.Length - line))));
            }
        }

        public static string Hex(byte[] bytes, int start, int count)
        {
            var text = new StringBuilder(count * 3);
            for (int i = start; i < start + count; i++)
            {
                text.Append(bytes[i].ToString("x2")).Append(' ');
            }
            return text.ToString();
        }
    }
}
