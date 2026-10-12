using System.Collections.Generic;

namespace LayoutProbe
{
    /// <summary>
    /// What the auto splitter reads, by .NET names. Each root is a static field that the auto
    /// splitter finds through a hand-written signature in its anchor method's compiled code, and
    /// each of its paths is a chain of instance fields read from the root's object.
    /// </summary>
    internal static class Manifest
    {
        // Celeste's constructor passes the game and window sizes (1920x1080, 960x540) to
        // Engine's, then stores itself in Celeste.Instance. These match the size arguments:
        // pushed on Windows, in registers on Mono. They're memory.rs's WINDOWS_CLR32_SIG and
        // MONO64_SIG.
        private const string CelesteClr32 = "68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35";
        private const string CelesteMono64 = "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00";

        public static readonly ManifestRoot[] Roots =
        {
            new ManifestRoot
            {
                Field = "Celeste.Celeste::Instance",
                Anchor = "Celeste.Celeste::.ctor()",
                Signatures =
                {
                    ["windows-xna"] = CelesteClr32,
                    ["windows-fna"] = CelesteClr32,
                    ["linux"] = CelesteMono64,
                    ["macos"] = CelesteMono64,
                },
                Paths =
                {
                    "Title",
                    "AutoSplitterInfo.Chapter",
                    "AutoSplitterInfo.Mode",
                    "AutoSplitterInfo.Level",
                    "AutoSplitterInfo.TimerActive",
                    "AutoSplitterInfo.ChapterStarted",
                    "AutoSplitterInfo.ChapterComplete",
                    "AutoSplitterInfo.ChapterTime",
                    "AutoSplitterInfo.ChapterStrawberries",
                    "AutoSplitterInfo.ChapterCassette",
                    "AutoSplitterInfo.ChapterHeart",
                    "AutoSplitterInfo.FileTime",
                    "AutoSplitterInfo.FileStrawberries",
                    "AutoSplitterInfo.FileCassettes",
                    "AutoSplitterInfo.FileHearts",
                },
            },
        };
    }

    internal sealed class ManifestRoot
    {
        /// <summary>The static field, as Namespace.Type::Field.</summary>
        public string Field;

        /// <summary>
        /// The method whose compiled code holds the field's address, as
        /// Namespace.Type::Name(ParameterType, ...).
        /// </summary>
        public string Anchor;

        /// <summary>
        /// Per flavour, the hand-written signature that finds the anchor's code: hex bytes, with
        /// ?? for any byte. It must match the code once, followed by a reference to the field.
        /// </summary>
        public readonly Dictionary<string, string> Signatures = new Dictionary<string, string>();

        /// <summary>
        /// Instance field names from the root's object, joined with dots. Each name is looked up
        /// in the previous field's type, or its nearest base class with that field.
        /// </summary>
        public readonly List<string> Paths = new List<string>();
    }
}
