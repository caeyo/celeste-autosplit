using System.Linq;
using System.Reflection;

namespace LayoutProbe
{
    /// <summary>A manifest root, resolved, and what the probe found for it.</summary>
    internal sealed class Root
    {
        /// <summary>The static field, as the manifest names it.</summary>
        public string Name;
        public FieldInfo Field;
        public MethodBase Anchor;
        public Signature Signature;
        public FieldPath[] Paths;

        /// <summary>The static field's address.</summary>
        public ulong Slot;
        /// <summary>The anchor's compiled code.</summary>
        public ulong Code;
        /// <summary>Where the signature matches in the code.</summary>
        public int Match;
        /// <summary>The slot's reference in the code that the auto splitter reads.</summary>
        public SlotReference Reference;

        public int Displacement => Reference.Offset - Match;
    }

    /// <summary>A manifest path from a root's object: an instance field per hop, and its measured offset.</summary>
    internal sealed class FieldPath
    {
        /// <summary>The field names joined with dots, as the manifest has them.</summary>
        public string Name;
        public FieldInfo[] Fields;
        public long[] Offsets;

        public FieldInfo Last => Fields.Last();
    }
}
