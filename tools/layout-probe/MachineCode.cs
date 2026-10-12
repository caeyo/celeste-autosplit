using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LayoutProbe
{
    /// <summary>
    /// A hand-written byte pattern: hex bytes separated by spaces, with ?? for any byte, and SS
    /// for the bytes of a static slot's address, 4 or 8 of them in a row.
    /// </summary>
    internal sealed class Pattern
    {
        private const int AnyByte = -1;
        private const int SlotByte = -2;

        private readonly int[] bytes;

        /// <summary>Where the slot's address starts in the pattern, if it has one.</summary>
        public readonly int SlotOffset;

        /// <summary>The slot address's width: 4, 8, or 0 without one.</summary>
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

        /// <summary>The slot's address in a match of the pattern.</summary>
        public ulong Slot(byte[] code, int match) =>
            SlotWidth == 4 ? BitConverter.ToUInt32(code, match + SlotOffset) : BitConverter.ToUInt64(code, match + SlotOffset);

        /// <summary>The pattern in lower case, however it was written.</summary>
        public override string ToString() =>
            string.Join(" ", bytes.Select(b => b == AnyByte ? "??" : b == SlotByte ? "SS" : b.ToString("x2")).ToArray());
    }

    /// <summary>
    /// How the auto splitter finds a static slot in compiled code. The signature matches the
    /// anchor's code once. The store into the slot follows it, at a distance that can depend on
    /// the length of the instructions in between: it's the first of the store patterns to match
    /// after the signature, and its SS bytes are the slot's address.
    /// </summary>
    internal sealed class Signature
    {
        public readonly Pattern Code;
        public readonly Pattern[] Store;

        public Signature(string code, params string[] store)
        {
            Code = new Pattern(code);
            Store = store.Select(s => new Pattern(s)).ToArray();
            if (Code.SlotWidth != 0 || Store.Length == 0 || Store.Any(s => s.SlotWidth == 0))
            {
                throw new ArgumentException(code + ": the signature has no SS, and each store pattern has them");
            }
        }
    }

    /// <summary>Where a static slot's address appears in machine code.</summary>
    internal sealed class SlotReference
    {
        public int Offset;
        public int Width;

        public override string ToString() => "+0x" + Offset.ToString("x") + " (" + Width + " bytes)";
    }

    internal static class MachineCode
    {
        /// <summary>
        /// Every place the slot's address appears in the code: as a u32, and in 64-bit processes
        /// also as a u64. For reference when writing store patterns. On these runtimes it's never
        /// relative to the instruction: x86 has no RIP-relative addressing, and Mono's JIT writes
        /// a static's address into the instruction.
        /// </summary>
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
