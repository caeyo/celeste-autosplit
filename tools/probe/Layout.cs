using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Probe
{
    /// <summary>A manifest root, resolved, and what the probe found for it.</summary>
    internal sealed class Root
    {
        /// <summary>The static field, as the manifest names it.</summary>
        public string Name;
        public FieldInfo Field;
        /// <summary>The anchor, as the manifest names it.</summary>
        public string AnchorName;
        public MethodBase Anchor;
        /// <summary>Per flavour, as the manifest has them.</summary>
        public Dictionary<string, Signature> Signatures;
        public FieldPath[] Paths;

        /// <summary>The flavour's signature, once it has matched the anchor's code.</summary>
        public Signature Signature;
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
