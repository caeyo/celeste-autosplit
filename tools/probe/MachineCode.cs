using System;
using System.Collections.Generic;
using System.Text;

namespace Probe
{
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

        public static void HexDump(byte[] bytes)
        {
            for (int line = 0; line < bytes.Length; line += 16)
            {
                var text = new StringBuilder();
                for (int i = line; i < Math.Min(line + 16, bytes.Length); i++)
                {
                    text.Append(bytes[i].ToString("x2")).Append(' ');
                }
                Console.WriteLine("  {0:x4}: {1}", line, text);
            }
        }
    }
}
