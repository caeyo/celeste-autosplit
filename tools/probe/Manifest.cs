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
        public static readonly ManifestRoot[] Roots =
        {
            new ManifestRoot
            {
                Field = "Celeste.Celeste::Instance",
                Anchor = "Celeste.Celeste::.ctor()",
                CodeField = "AutoSplitterInfo",
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
            new ManifestRoot
            {
                Field = "Celeste.SaveData::Instance",
                Anchor = "Celeste.AutoSplitterInfo::Update()",
                Paths =
                {
                    "TotalDeaths",
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
        /// one in different versions, so no layout can hold it for every game. Without one, each
        /// path is a single field of the root's object.
        /// </summary>
        public string CodeField;

        /// <summary>
        /// Instance field names from the root's object, joined with dots. Each name is looked up
        /// in the previous field's type, or its nearest base class with that field.
        /// </summary>
        public readonly List<string> Paths = new List<string>();
    }
}
