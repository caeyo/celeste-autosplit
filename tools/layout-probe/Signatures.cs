using System.Collections.Generic;

namespace LayoutProbe
{
    /// <summary>
    /// The probe's copy of the hand-written signatures in autosplitter/src/memory.rs, by root and
    /// flavour. Each flavour's own signature must match its root's anchor exactly once, followed
    /// by a reference to the root's static slot.
    /// </summary>
    internal static class Signatures
    {
        // Celeste's constructor passes the game and window sizes (1920x1080, 960x540) to Engine's,
        // then stores itself in Celeste.Instance. These match the size arguments: pushed on
        // Windows, in registers on Mono.
        private const string CelesteClr32 = "68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35";
        private const string CelesteMono64 = "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00";

        private static readonly Dictionary<string, Dictionary<string, string>> ByRoot = new Dictionary<string, Dictionary<string, string>>
        {
            ["Celeste.Celeste::Instance"] = new Dictionary<string, string>
            {
                ["windows-xna"] = CelesteClr32,
                ["windows-fna"] = CelesteClr32,
                ["linux"] = CelesteMono64,
                ["macos"] = CelesteMono64,
            },
        };

        public static Signature For(string root, string flavour)
        {
            if (!ByRoot.TryGetValue(root, out Dictionary<string, string> byFlavour) || !byFlavour.TryGetValue(flavour, out string pattern))
            {
                throw new ProbeException("no hand-written signature for " + root + " on " + flavour + ": add one to Signatures.cs and to memory.rs");
            }
            return new Signature(pattern);
        }
    }
}
