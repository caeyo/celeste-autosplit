using System;
using System.Globalization;
using System.Xml;

namespace CelesteAutosplit;

public static class LegacyMigration
{
    private enum SplitType
    {
        Manual,
        ChapterA,
        AreaComplete,
        AreaOnEnter,
        AreaOnExit,
        HeartGemAny,
        LevelEnter,
        LevelExit,
        Prologue,
        Chapter1Checkpoint1,
        Chapter1Checkpoint2,
        Chapter1,
        Chapter2Checkpoint1,
        Chapter2Checkpoint2,
        Chapter2,
        Chapter3Checkpoint1,
        Chapter3Checkpoint2,
        Chapter3Checkpoint3,
        Chapter3,
        Chapter4Checkpoint1,
        Chapter4Checkpoint2,
        Chapter4Checkpoint3,
        Chapter4,
        Chapter5Checkpoint1,
        Chapter5Checkpoint2,
        Chapter5Checkpoint3,
        Chapter5Checkpoint4,
        Chapter5,
        Chapter6Checkpoint1,
        Chapter6Checkpoint2,
        Chapter6Checkpoint3,
        Chapter6Checkpoint4,
        Chapter6Checkpoint5,
        Chapter6,
        Chapter7Checkpoint1,
        Chapter7Checkpoint2,
        Chapter7Checkpoint3,
        Chapter7Checkpoint4,
        Chapter7Checkpoint5,
        Chapter7Checkpoint6,
        Chapter7,
        Epilogue,
        Chapter8Checkpoint1,
        Chapter8Checkpoint2,
        Chapter8Checkpoint3,
        Chapter8,
        Chapter9Checkpoint1,
        Chapter9Checkpoint2,
        Chapter9Checkpoint3,
        Chapter9Checkpoint4,
        Chapter9Checkpoint5,
        Chapter9Checkpoint6,
        Chapter9Checkpoint7,
        Chapter9Checkpoint8,
        Chapter9,
        Chapter1Cassette,
        Chapter1HeartGem,
        Chapter2Cassette,
        Chapter2HeartGem,
        Chapter3Cassette,
        Chapter3HeartGem,
        Chapter4Cassette,
        Chapter4HeartGem,
        Chapter5Cassette,
        Chapter5HeartGem,
        Chapter6Cassette,
        Chapter6HeartGem,
        Chapter7Cassette,
        Chapter7HeartGem,
        Chapter8Cassette,
        Chapter8HeartGem,
    }

    private enum Area
    {
        Menu = -1,
        Prologue = 0,
        ForsakenCity = 1,
        OldSite = 2,
        CelestialResort = 3,
        GoldenRidge = 4,
        MirrorTemple = 5,
        Reflection = 6,
        TheSummit = 7,
        Epilogue = 8,
        Core = 9,
        Farewell = 10,
    }

    private enum AreaMode
    {
        None = -1,
        ASide = 0,
        BSide = 1,
        CSide = 2,
    }

    private readonly struct Split
    {
        public readonly string Text;
        public readonly SplitType Type;
        public readonly bool Parsed;
        public readonly string Value;

        public Split(string text)
        {
            Text = text;
            string[] fields = text.Split(',');
            Parsed = Enum.TryParse(fields[0], out Type);
            Value = fields.Length > 1 ? fields[1] : null;
        }
    }

    private sealed class Trigger
    {
        public string Kind;
        public long? Chapter;
        public long? Side;
        public string Timing;
        public long? Checkpoint;
        public string Room;
        public string Raw;
    }

    public static XmlNode Migrate(XmlNode settings)
    {
        if (settings.SelectSingleNode(".//CustomSettings") != null
            || (settings.SelectSingleNode(".//Splits") == null && settings.SelectSingleNode(".//AutoReset") == null))
        {
            return settings;
        }

        XmlNodeList splitNodes = settings.SelectNodes(".//Splits/Split");
        var splits = new Split[splitNodes.Count];
        for (int i = 0; i < splits.Length; i++)
        {
            splits[i] = new Split(splitNodes[i].InnerText);
        }

        bool autoReset = Bool(settings, "AutoReset", true);
        bool fileTimeOffset = Bool(settings, "FileTimeOffset", false);

        Classify(splits, out bool chapterSplits, out bool ilSplits);

        Trigger start;
        Trigger reset = autoReset ? new Trigger { Kind = "chapter_exit" } : new Trigger { Kind = "manual" };
        int firstSplit = 0;
        string timing = null;
        string timingMethod;
        if (splits.Length == 0)
        {
            start = new Trigger { Kind = "manual" };
            timingMethod = "file";
        }
        else if (!chapterSplits)
        {
            start = Map(splits[0], null);
            firstSplit = 1;
            timingMethod = "chapter_since_start";
        }
        else if (ilSplits)
        {
            start = new Trigger { Kind = "chapter_start" };
            timing = "chapter_complete";
            timingMethod = "chapter";
        }
        else
        {
            start = new Trigger { Kind = "new_file" };
            reset = new Trigger { Kind = "manual" };
            timing = "chapter_exit";
            timingMethod = "file";
        }

        if (fileTimeOffset)
        {
            timingMethod = "file_since_start";
        }

        var document = new XmlDocument();

        XmlElement cfg = Setting(document, "cfg", "map");
        cfg.AppendChild(TriggerElement(document, "start", start));
        cfg.AppendChild(TriggerElement(document, "reset", reset));
        XmlElement triggers = Setting(document, "splits", "list");
        for (int i = firstSplit; i < splits.Length; i++)
        {
            triggers.AppendChild(TriggerElement(document, null, Map(splits[i], timing)));
        }

        cfg.AppendChild(triggers);
        cfg.AppendChild(StringSetting(document, "timing_method", timingMethod));
        cfg.AppendChild(BoolSetting(document, "everest", false));
        cfg.AppendChild(BoolSetting(document, "high_priority", Bool(settings, "SetHighPriority", true)));
        cfg.AppendChild(BoolSetting(document, "set_game_time", Bool(settings, "SetGameTime", true)));

        XmlElement legacy = Setting(document, "legacy", "map");
        foreach (string name in new[] { "AutoReset", "SetHighPriority", "SetGameTime", "FileTimeOffset" })
        {
            XmlNode node = settings.SelectSingleNode(".//" + name);
            if (node != null)
            {
                legacy.AppendChild(StringSetting(document, name, node.InnerText));
            }
        }

        XmlElement legacySplits = Setting(document, "Splits", "list");
        foreach (Split split in splits)
        {
            legacySplits.AppendChild(StringSetting(document, null, split.Text));
        }

        legacy.AppendChild(legacySplits);

        XmlElement customSettings = document.CreateElement("CustomSettings");
        customSettings.AppendChild(IntSetting(document, "schema_version", 1));
        customSettings.AppendChild(cfg);
        customSettings.AppendChild(legacy);

        XmlElement version = document.CreateElement("Version");
        version.InnerText = "1.0";

        XmlElement root = document.CreateElement("AutoSplitterSettings");
        root.AppendChild(version);
        root.AppendChild(customSettings);
        return root;
    }

    private static bool Bool(XmlNode settings, string name, bool fallback)
    {
        return bool.TryParse(settings.SelectSingleNode(".//" + name)?.InnerText, out bool value) ? value : fallback;
    }

    private static void Classify(Split[] splits, out bool chapterSplits, out bool ilSplits)
    {
        int chapters = 0;
        int hearts = 0;
        int areas = 0;
        int cassettes = 0;
        foreach (Split split in splits)
        {
            string name = split.Type.ToString();
            if (name.Length == 8)
            {
                chapters++;
            }
            else if (name.Contains("HeartGem"))
            {
                hearts++;
            }
            else if (name.Contains("AreaComplete"))
            {
                areas++;
            }
            else if (name.Contains("Cassette"))
            {
                cassettes++;
            }
        }

        ilSplits = splits.Length == 0 || (chapters <= 1 && hearts <= 1 && areas <= 1 && cassettes <= 1);
        chapterSplits = chapters > 0 || hearts > 0 || areas > 0 || cassettes > 0;
    }

    private static Trigger Map(Split split, string timing)
    {
        if (!split.Parsed)
        {
            return Unknown(split);
        }

        return split.Type switch
        {
            SplitType.Manual => new Trigger { Kind = "manual" },
            SplitType.ChapterA => Complete(null, timing),
            SplitType.Prologue => Complete(0, timing),
            SplitType.Chapter1 => Complete(1, timing),
            SplitType.Chapter2 => Complete(2, timing),
            SplitType.Chapter3 => Complete(3, timing),
            SplitType.Chapter4 => Complete(4, timing),
            SplitType.Chapter5 => Complete(5, timing),
            SplitType.Chapter6 => Complete(6, timing),
            SplitType.Chapter7 => Complete(7, timing),
            SplitType.Epilogue => Complete(8, timing),
            SplitType.Chapter8 => Complete(9, timing),
            SplitType.Chapter9 => Complete(10, timing),
            SplitType.Chapter1Checkpoint1 => Checkpoint(1, 1),
            SplitType.Chapter1Checkpoint2 => Checkpoint(1, 2),
            SplitType.Chapter2Checkpoint1 => Checkpoint(2, 1),
            SplitType.Chapter2Checkpoint2 => Checkpoint(2, 2),
            SplitType.Chapter3Checkpoint1 => Checkpoint(3, 1),
            SplitType.Chapter3Checkpoint2 => Checkpoint(3, 2),
            SplitType.Chapter3Checkpoint3 => Checkpoint(3, 3),
            SplitType.Chapter4Checkpoint1 => Checkpoint(4, 1),
            SplitType.Chapter4Checkpoint2 => Checkpoint(4, 2),
            SplitType.Chapter4Checkpoint3 => Checkpoint(4, 3),
            SplitType.Chapter5Checkpoint1 => Checkpoint(5, 1),
            SplitType.Chapter5Checkpoint2 => Checkpoint(5, 2),
            SplitType.Chapter5Checkpoint3 => Checkpoint(5, 3),
            SplitType.Chapter5Checkpoint4 => Checkpoint(5, 4),
            SplitType.Chapter6Checkpoint1 => Checkpoint(6, 1),
            SplitType.Chapter6Checkpoint2 => Checkpoint(6, 2),
            SplitType.Chapter6Checkpoint3 => Checkpoint(6, 3),
            SplitType.Chapter6Checkpoint4 => Checkpoint(6, 4),
            SplitType.Chapter6Checkpoint5 => Checkpoint(6, 5),
            SplitType.Chapter7Checkpoint1 => Checkpoint(7, 1),
            SplitType.Chapter7Checkpoint2 => Checkpoint(7, 2),
            SplitType.Chapter7Checkpoint3 => Checkpoint(7, 3),
            SplitType.Chapter7Checkpoint4 => Checkpoint(7, 4),
            SplitType.Chapter7Checkpoint5 => Checkpoint(7, 5),
            SplitType.Chapter7Checkpoint6 => Checkpoint(7, 6),
            SplitType.Chapter8Checkpoint1 => Checkpoint(9, 1),
            SplitType.Chapter8Checkpoint2 => Checkpoint(9, 2),
            SplitType.Chapter8Checkpoint3 => Checkpoint(9, 3),
            SplitType.Chapter9Checkpoint1 => Checkpoint(10, 1),
            SplitType.Chapter9Checkpoint2 => Checkpoint(10, 2),
            SplitType.Chapter9Checkpoint3 => Checkpoint(10, 3),
            SplitType.Chapter9Checkpoint4 => Checkpoint(10, 4),
            SplitType.Chapter9Checkpoint5 => Checkpoint(10, 5),
            SplitType.Chapter9Checkpoint6 => Checkpoint(10, 6),
            SplitType.Chapter9Checkpoint7 => Checkpoint(10, 7),
            SplitType.Chapter9Checkpoint8 => Checkpoint(10, 8),
            SplitType.HeartGemAny => new Trigger { Kind = "heart" },
            SplitType.Chapter1HeartGem => new Trigger { Kind = "heart", Chapter = 1 },
            SplitType.Chapter2HeartGem => new Trigger { Kind = "heart", Chapter = 2 },
            SplitType.Chapter3HeartGem => new Trigger { Kind = "heart", Chapter = 3 },
            SplitType.Chapter4HeartGem => new Trigger { Kind = "heart", Chapter = 4 },
            SplitType.Chapter5HeartGem => new Trigger { Kind = "heart", Chapter = 5 },
            SplitType.Chapter6HeartGem => new Trigger { Kind = "heart", Chapter = 6 },
            SplitType.Chapter7HeartGem => new Trigger { Kind = "heart", Chapter = 7 },
            SplitType.Chapter8HeartGem => new Trigger { Kind = "heart", Chapter = 9 },
            SplitType.Chapter1Cassette => new Trigger { Kind = "cassette", Chapter = 1 },
            SplitType.Chapter2Cassette => new Trigger { Kind = "cassette", Chapter = 2 },
            SplitType.Chapter3Cassette => new Trigger { Kind = "cassette", Chapter = 3 },
            SplitType.Chapter4Cassette => new Trigger { Kind = "cassette", Chapter = 4 },
            SplitType.Chapter5Cassette => new Trigger { Kind = "cassette", Chapter = 5 },
            SplitType.Chapter6Cassette => new Trigger { Kind = "cassette", Chapter = 6 },
            SplitType.Chapter7Cassette => new Trigger { Kind = "cassette", Chapter = 7 },
            SplitType.Chapter8Cassette => new Trigger { Kind = "cassette", Chapter = 9 },
            SplitType.AreaComplete or SplitType.AreaOnEnter or SplitType.AreaOnExit => AreaTrigger(split, timing),
            SplitType.LevelEnter or SplitType.LevelExit => RoomTrigger(split),
            _ => Unknown(split),
        };
    }

    private static Trigger Complete(long? chapter, string timing)
    {
        return new Trigger { Kind = "chapter_complete", Chapter = chapter, Timing = timing };
    }

    private static Trigger Checkpoint(long chapter, long checkpoint)
    {
        return new Trigger { Kind = "checkpoint", Chapter = chapter, Checkpoint = checkpoint };
    }

    private static Trigger Unknown(Split split)
    {
        return new Trigger { Kind = "unknown", Raw = split.Text };
    }

    private static Trigger RoomTrigger(Split split)
    {
        if (string.IsNullOrEmpty(split.Value))
        {
            return Unknown(split);
        }

        return new Trigger { Kind = split.Type == SplitType.LevelEnter ? "room_enter" : "room_exit", Room = split.Value };
    }

    private static Trigger AreaTrigger(Split split, string timing)
    {
        string[] parts = (split.Value ?? "").Split('-');
        if (!Enum.TryParse(parts[0], true, out Area area))
        {
            return Unknown(split);
        }

        long? side = null;
        if (parts.Length > 1)
        {
            if (!Enum.TryParse(parts[1], true, out AreaMode mode) || mode < AreaMode.ASide || mode > AreaMode.CSide)
            {
                return Unknown(split);
            }

            side = (long)mode;
        }

        if (area == Area.Menu)
        {
            if (side != null || split.Type == SplitType.AreaComplete)
            {
                return Unknown(split);
            }

            return new Trigger { Kind = split.Type == SplitType.AreaOnEnter ? "chapter_exit" : "chapter_enter" };
        }

        return split.Type switch
        {
            SplitType.AreaComplete => new Trigger { Kind = "chapter_complete", Chapter = (long)area, Side = side, Timing = timing },
            SplitType.AreaOnEnter => new Trigger { Kind = "chapter_enter", Chapter = (long)area, Side = side },
            _ => new Trigger { Kind = "chapter_exit", Chapter = (long)area, Side = side },
        };
    }

    private static XmlElement TriggerElement(XmlDocument document, string id, Trigger trigger)
    {
        XmlElement element = Setting(document, id, "map");
        element.AppendChild(StringSetting(document, "kind", trigger.Kind));
        AppendOptional(document, element, "chapter", trigger.Chapter);
        AppendOptional(document, element, "side", trigger.Side);
        AppendOptional(document, element, "timing", trigger.Timing);
        AppendOptional(document, element, "checkpoint", trigger.Checkpoint);
        AppendOptional(document, element, "room", trigger.Room);
        AppendOptional(document, element, "raw", trigger.Raw);
        return element;
    }

    private static void AppendOptional(XmlDocument document, XmlElement parent, string id, long? value)
    {
        if (value != null)
        {
            parent.AppendChild(IntSetting(document, id, value.Value));
        }
    }

    private static void AppendOptional(XmlDocument document, XmlElement parent, string id, string value)
    {
        if (value != null)
        {
            parent.AppendChild(StringSetting(document, id, value));
        }
    }

    private static XmlElement Setting(XmlDocument document, string id, string type)
    {
        XmlElement element = document.CreateElement("Setting");
        if (id != null)
        {
            element.SetAttribute("id", id);
        }

        element.SetAttribute("type", type);
        return element;
    }

    private static XmlElement StringSetting(XmlDocument document, string id, string value)
    {
        XmlElement element = Setting(document, id, "string");
        element.SetAttribute("value", value);
        return element;
    }

    private static XmlElement IntSetting(XmlDocument document, string id, long value)
    {
        XmlElement element = Setting(document, id, "i64");
        element.InnerText = value.ToString(CultureInfo.InvariantCulture);
        return element;
    }

    private static XmlElement BoolSetting(XmlDocument document, string id, bool value)
    {
        XmlElement element = Setting(document, id, "bool");
        element.InnerText = value ? "True" : "False";
        return element;
    }
}
