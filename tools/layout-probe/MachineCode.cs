using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LayoutProbe
{
    /// <summary>A hand-written signature, and where the static slot's address follows it.</summary>
    internal sealed class Signature
    {
        public readonly string Name;
        public readonly int?[] Pattern;
        public readonly int SlotDisplacement;
        public readonly int SlotWidth;

        public Signature(string name, string pattern, int slotDisplacement, int slotWidth)
        {
            Name = name;
            Pattern = pattern.Split(' ').Select(b => b == "??" ? (int?)null : Convert.ToInt32(b, 16)).ToArray();
            SlotDisplacement = slotDisplacement;
            SlotWidth = slotWidth;
        }

        public IEnumerable<int> Matches(byte[] code)
        {
            for (int start = 0; start + Pattern.Length <= code.Length; start++)
            {
                int i = 0;
                while (i < Pattern.Length && (Pattern[i] == null || Pattern[i] == code[start + i]))
                {
                    i++;
                }
                if (i == Pattern.Length)
                {
                    yield return start;
                }
            }
        }
    }

    /// <summary>Where a static slot's address appears in machine code, and how it's encoded.</summary>
    internal sealed class SlotReference
    {
        public int Offset;
        public int Width;
        public bool RipRelative;

        public override string ToString() => (RipRelative ? "rip-relative " : "absolute ") + "u" + Width * 8;
    }

    internal static class MachineCode
    {
        /// <summary>
        /// Follows jumps from a method's entry point to its compiled code. On .NET Framework,
        /// GetFunctionPointer returns a precode stub that jumps to the code once it's compiled.
        /// </summary>
        public static ulong FollowJumps(ulong entry, Action<string> log)
        {
            ulong address = entry;
            for (int hop = 0; hop < 8; hop++)
            {
                byte[] b = Memory.ReadMapped(address, 16);
                ulong next;
                if (b.Length >= 5 && b[0] == 0xE9)
                {
                    // jmp rel32
                    next = address + 5 + (ulong)(long)BitConverter.ToInt32(b, 1);
                }
                else if (b.Length >= 10 && b[0] == 0xB8 && b[5] == 0xE9)
                {
                    // mov eax, imm32; jmp rel32: an x86 stub precode
                    next = address + 10 + (ulong)(long)BitConverter.ToInt32(b, 6);
                }
                else if (b.Length >= 6 && b[0] == 0xFF && b[1] == 0x25)
                {
                    // jmp [mem]: an absolute address on x86, RIP-relative on x64
                    ulong target = IntPtr.Size == 4
                        ? BitConverter.ToUInt32(b, 2)
                        : address + 6 + (ulong)(long)BitConverter.ToInt32(b, 2);
                    next = Memory.ReadPointer(target);
                }
                else
                {
                    return address;
                }
                if (IntPtr.Size == 4)
                {
                    next &= uint.MaxValue;
                }
                log(string.Format("jump at 0x{0:x}: {1}-> 0x{2:x}", address, Hex(b, 0, Math.Min(b.Length, 10)), next));
                address = next;
            }
            return address;
        }

        /// <summary>
        /// Every place the slot's address appears in the code: as an absolute u32 or u64, or as
        /// a rel32 displacement from the end of the instruction (assumed to end right after it).
        /// </summary>
        public static List<SlotReference> FindSlotReferences(byte[] code, ulong codeAddress, ulong slot)
        {
            var references = new List<SlotReference>();
            for (int i = 0; i + 4 <= code.Length; i++)
            {
                if (slot <= uint.MaxValue && BitConverter.ToUInt32(code, i) == slot)
                {
                    references.Add(new SlotReference { Offset = i, Width = 4 });
                }
                if (i + 8 <= code.Length && BitConverter.ToUInt64(code, i) == slot)
                {
                    references.Add(new SlotReference { Offset = i, Width = 8 });
                }
                if (IntPtr.Size == 8 && codeAddress + (ulong)(i + 4) + (ulong)(long)BitConverter.ToInt32(code, i) == slot)
                {
                    references.Add(new SlotReference { Offset = i, Width = 4, RipRelative = true });
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
