using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LayoutProbe
{
    /// <summary>A hand-written signature: hex bytes separated by spaces, with ?? for any byte.</summary>
    internal sealed class Signature
    {
        private readonly int?[] pattern;

        public Signature(string pattern)
        {
            this.pattern = pattern.Split(' ').Select(b => b == "??" ? (int?)null : Convert.ToInt32(b, 16)).ToArray();
        }

        public IEnumerable<int> Matches(byte[] code)
        {
            for (int start = 0; start + pattern.Length <= code.Length; start++)
            {
                int i = 0;
                while (i < pattern.Length && (pattern[i] == null || pattern[i] == code[start + i]))
                {
                    i++;
                }
                if (i == pattern.Length)
                {
                    yield return start;
                }
            }
        }

        /// <summary>The pattern in lower case, however it was written.</summary>
        public override string ToString() =>
            string.Join(" ", pattern.Select(b => b == null ? "??" : b.Value.ToString("x2")).ToArray());
    }

    /// <summary>Where a static slot's address appears in machine code, and its width.</summary>
    internal sealed class SlotReference
    {
        public int Offset;
        public int Width;

        /// <summary>
        /// absolute_u32 or absolute_u64: the slot's address itself. Code on these runtimes never
        /// reaches a static relative to the instruction: x86 has no RIP-relative addressing, and
        /// Mono's JIT writes the static's address into the instruction.
        /// </summary>
        public string Encoding => "absolute_u" + Width * 8;

        public override string ToString() => Encoding;
    }

    internal static class MachineCode
    {
        /// <summary>
        /// Every place the slot's address appears in the code: as a u32, and in 64-bit processes
        /// also as a u64.
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
