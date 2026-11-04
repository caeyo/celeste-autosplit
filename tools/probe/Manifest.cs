using System.Collections.Generic;

namespace Probe
{
    /// <summary>
    /// What the auto splitter reads, by .NET names. Each root is a static field that the auto
    /// splitter finds through a hand-written signature in its anchor method's compiled code, and
    /// each of its paths is a chain of instance fields read from the root's object.
    /// </summary>
    internal static class Manifest
    {
        /// <summary>
        /// How many bytes after each signature match memory.rs searches for the store, which has
        /// to end within them. The probe writes it into the layouts, for memory.rs.
        /// </summary>
        public const int StoreWindow = 256;

        /// <summary>
        /// How many bytes before each signature match memory.rs searches for the store of the
        /// root's CodeField, which has to start within them. Few enough to stay in the anchor's
        /// code, since the signature is near its start. The probe writes it into the layouts, for
        /// memory.rs.
        /// </summary>
        public const int FieldStoreWindow = 128;

        // Celeste's constructor passes the game and window sizes (1920x1080, 960x540) to
        // Engine's, then stores itself in Celeste.Instance. The signatures match the size
        // arguments: pushed on Windows, in registers on Mono. Then the store:
        // - On Windows, the slot's address goes to the GC's write barrier: lea edx, [slot]; call.
        // - On Mono, it's loaded into rax and stored through: mov eax or rax, slot; mov [rax], r15.
        //   The 4-byte form is used when the address fits, which on Linux depends on where the
        //   heap is, so both forms are listed. Where the heap is also changes the length of other
        //   instructions between the signature and the store, so the store is searched for
        //   rather than expected at a fixed distance.
        // Once it has checked them, the probe writes them into each runtime's layout, and
        // memory.rs finds the slot from them the same way, within StoreWindow bytes of the
        // signature.
        // Its field initializers run before that, so the store of its new AutoSplitterInfo comes
        // before the signature, and holds the field's offset:
        // - On Windows, the field's address goes to the write barrier: lea edx, [esi+offset]; call.
        // - On Mono, it's computed for the write barrier: lea rax, [r15+offset].
        // These come before Roots, which uses them: static fields are set in order.
        private static readonly Signature CelesteClr32 = new Signature(
            "68 38 04 00 00 68 C0 03 00 00 68 1C 02 00 00 FF 35",
            "8d 96 SS SS SS SS e8",
            "8d 15 SS SS SS SS e8");
        private static readonly Signature CelesteMono64 = new Signature(
            "be 80 07 00 00 ba 38 04 00 00 b9 c0 03 00 00 41 b8 1c 02 00 00",
            "49 8d 87 SS SS SS SS",
            "b8 SS SS SS SS 4c 89 38",
            "48 b8 SS SS SS SS SS SS SS SS 4c 89 38");

        public static readonly ManifestRoot[] Roots =
        {
            new ManifestRoot
            {
                Field = "Celeste.Celeste::Instance",
                Anchor = "Celeste.Celeste::.ctor()",
                CodeField = "AutoSplitterInfo",
                Signatures =
                {
                    ["clr32"] = CelesteClr32,
                    ["mono64"] = CelesteMono64,
                },
                Paths =
                {
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
        /// The first field of the root's paths, whose offset memory.rs reads from the anchor's code
        /// rather than from the layout. It's declared after the fields of a base class in the
        /// game's framework, and Everest replaces the FNA that vanilla runs on, with a different
        /// one in different versions, so no layout can hold it for every game.
        /// </summary>
        public string CodeField;

        /// <summary>Per runtime, how to find the field's address in the anchor's code.</summary>
        public readonly Dictionary<string, Signature> Signatures = new Dictionary<string, Signature>();

        /// <summary>
        /// Instance field names from the root's object, joined with dots. Each name is looked up
        /// in the previous field's type, or its nearest base class with that field.
        /// </summary>
        public readonly List<string> Paths = new List<string>();
    }
}
