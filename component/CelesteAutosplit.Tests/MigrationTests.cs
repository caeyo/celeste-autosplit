using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

namespace CelesteAutosplit.Tests;

public class MigrationTests
{
    [Theory]
    [MemberData(nameof(LegacyFiles.Names), MemberType = typeof(LegacyFiles))]
    public void Legacy_files_migrate_to_their_expected_settings(string name)
    {
        Assert.Equal(LegacyFiles.Expected(name).InnerXml, LegacyMigration.Migrate(LegacyFiles.Settings(name)).InnerXml);
    }

    [Theory]
    [MemberData(nameof(LegacyFiles.Names), MemberType = typeof(LegacyFiles))]
    public void Migrating_a_legacy_file_twice_gives_the_same_output(string name)
    {
        XmlNode settings = LegacyFiles.Settings(name);

        Assert.Equal(LegacyMigration.Migrate(settings).InnerXml, LegacyMigration.Migrate(settings).InnerXml);
    }

    [Theory]
    [InlineData("Manual", "kind=manual")]
    [InlineData("ChapterA", "kind=chapter_complete timing=chapter_exit")]
    [InlineData("AreaComplete", "kind=unknown raw=AreaComplete")]
    [InlineData("AreaOnEnter", "kind=unknown raw=AreaOnEnter")]
    [InlineData("AreaOnExit", "kind=unknown raw=AreaOnExit")]
    [InlineData("HeartGemAny", "kind=heart")]
    [InlineData("LevelEnter", "kind=unknown raw=LevelEnter")]
    [InlineData("LevelExit", "kind=unknown raw=LevelExit")]
    [InlineData("Prologue", "kind=chapter_complete chapter=0 timing=chapter_exit")]
    [InlineData("Chapter1Checkpoint1", "kind=checkpoint chapter=1 checkpoint=1")]
    [InlineData("Chapter1Checkpoint2", "kind=checkpoint chapter=1 checkpoint=2")]
    [InlineData("Chapter1", "kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("Chapter2Checkpoint1", "kind=checkpoint chapter=2 checkpoint=1")]
    [InlineData("Chapter2Checkpoint2", "kind=checkpoint chapter=2 checkpoint=2")]
    [InlineData("Chapter2", "kind=chapter_complete chapter=2 timing=chapter_exit")]
    [InlineData("Chapter3Checkpoint1", "kind=checkpoint chapter=3 checkpoint=1")]
    [InlineData("Chapter3Checkpoint2", "kind=checkpoint chapter=3 checkpoint=2")]
    [InlineData("Chapter3Checkpoint3", "kind=checkpoint chapter=3 checkpoint=3")]
    [InlineData("Chapter3", "kind=chapter_complete chapter=3 timing=chapter_exit")]
    [InlineData("Chapter4Checkpoint1", "kind=checkpoint chapter=4 checkpoint=1")]
    [InlineData("Chapter4Checkpoint2", "kind=checkpoint chapter=4 checkpoint=2")]
    [InlineData("Chapter4Checkpoint3", "kind=checkpoint chapter=4 checkpoint=3")]
    [InlineData("Chapter4", "kind=chapter_complete chapter=4 timing=chapter_exit")]
    [InlineData("Chapter5Checkpoint1", "kind=checkpoint chapter=5 checkpoint=1")]
    [InlineData("Chapter5Checkpoint2", "kind=checkpoint chapter=5 checkpoint=2")]
    [InlineData("Chapter5Checkpoint3", "kind=checkpoint chapter=5 checkpoint=3")]
    [InlineData("Chapter5Checkpoint4", "kind=checkpoint chapter=5 checkpoint=4")]
    [InlineData("Chapter5", "kind=chapter_complete chapter=5 timing=chapter_exit")]
    [InlineData("Chapter6Checkpoint1", "kind=checkpoint chapter=6 checkpoint=1")]
    [InlineData("Chapter6Checkpoint2", "kind=checkpoint chapter=6 checkpoint=2")]
    [InlineData("Chapter6Checkpoint3", "kind=checkpoint chapter=6 checkpoint=3")]
    [InlineData("Chapter6Checkpoint4", "kind=checkpoint chapter=6 checkpoint=4")]
    [InlineData("Chapter6Checkpoint5", "kind=checkpoint chapter=6 checkpoint=5")]
    [InlineData("Chapter6", "kind=chapter_complete chapter=6 timing=chapter_exit")]
    [InlineData("Chapter7Checkpoint1", "kind=checkpoint chapter=7 checkpoint=1")]
    [InlineData("Chapter7Checkpoint2", "kind=checkpoint chapter=7 checkpoint=2")]
    [InlineData("Chapter7Checkpoint3", "kind=checkpoint chapter=7 checkpoint=3")]
    [InlineData("Chapter7Checkpoint4", "kind=checkpoint chapter=7 checkpoint=4")]
    [InlineData("Chapter7Checkpoint5", "kind=checkpoint chapter=7 checkpoint=5")]
    [InlineData("Chapter7Checkpoint6", "kind=checkpoint chapter=7 checkpoint=6")]
    [InlineData("Chapter7", "kind=chapter_complete chapter=7 timing=chapter_exit")]
    [InlineData("Epilogue", "kind=chapter_complete chapter=8 timing=chapter_exit")]
    [InlineData("Chapter8Checkpoint1", "kind=checkpoint chapter=9 checkpoint=1")]
    [InlineData("Chapter8Checkpoint2", "kind=checkpoint chapter=9 checkpoint=2")]
    [InlineData("Chapter8Checkpoint3", "kind=checkpoint chapter=9 checkpoint=3")]
    [InlineData("Chapter8", "kind=chapter_complete chapter=9 timing=chapter_exit")]
    [InlineData("Chapter9Checkpoint1", "kind=checkpoint chapter=10 checkpoint=1")]
    [InlineData("Chapter9Checkpoint2", "kind=checkpoint chapter=10 checkpoint=2")]
    [InlineData("Chapter9Checkpoint3", "kind=checkpoint chapter=10 checkpoint=3")]
    [InlineData("Chapter9Checkpoint4", "kind=checkpoint chapter=10 checkpoint=4")]
    [InlineData("Chapter9Checkpoint5", "kind=checkpoint chapter=10 checkpoint=5")]
    [InlineData("Chapter9Checkpoint6", "kind=checkpoint chapter=10 checkpoint=6")]
    [InlineData("Chapter9Checkpoint7", "kind=checkpoint chapter=10 checkpoint=7")]
    [InlineData("Chapter9Checkpoint8", "kind=checkpoint chapter=10 checkpoint=8")]
    [InlineData("Chapter9", "kind=chapter_complete chapter=10 timing=chapter_exit")]
    [InlineData("Chapter1Cassette", "kind=cassette chapter=1")]
    [InlineData("Chapter1HeartGem", "kind=heart chapter=1")]
    [InlineData("Chapter2Cassette", "kind=cassette chapter=2")]
    [InlineData("Chapter2HeartGem", "kind=heart chapter=2")]
    [InlineData("Chapter3Cassette", "kind=cassette chapter=3")]
    [InlineData("Chapter3HeartGem", "kind=heart chapter=3")]
    [InlineData("Chapter4Cassette", "kind=cassette chapter=4")]
    [InlineData("Chapter4HeartGem", "kind=heart chapter=4")]
    [InlineData("Chapter5Cassette", "kind=cassette chapter=5")]
    [InlineData("Chapter5HeartGem", "kind=heart chapter=5")]
    [InlineData("Chapter6Cassette", "kind=cassette chapter=6")]
    [InlineData("Chapter6HeartGem", "kind=heart chapter=6")]
    [InlineData("Chapter7Cassette", "kind=cassette chapter=7")]
    [InlineData("Chapter7HeartGem", "kind=heart chapter=7")]
    [InlineData("Chapter8Cassette", "kind=cassette chapter=9")]
    [InlineData("Chapter8HeartGem", "kind=heart chapter=9")]
    public void Each_legacy_type_maps_to_its_trigger(string type, string expected)
    {
        Assert.Equal(expected, FirstSplit(type));
    }

    [Theory]
    [InlineData("0", "kind=manual")]
    [InlineData("5", "kind=heart")]
    [InlineData("+5", "kind=heart")]
    [InlineData(" 5 ", "kind=heart")]
    [InlineData("8", "kind=chapter_complete chapter=0 timing=chapter_exit")]
    [InlineData("70", "kind=heart chapter=9")]
    [InlineData("71", "kind=unknown raw=71")]
    [InlineData("200", "kind=unknown raw=200")]
    [InlineData("-1", "kind=unknown raw=-1")]
    [InlineData("99999999999", "kind=unknown raw=99999999999")]
    [InlineData("3", "kind=unknown raw=3")]
    [InlineData("chapter1", "kind=unknown raw=chapter1")]
    [InlineData("Foo", "kind=unknown raw=Foo")]
    [InlineData("", "kind=unknown raw=")]
    [InlineData(" Chapter1 ", "kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("Manual,x", "kind=manual")]
    public void Integers_and_garbage_parse_as_legacy_parses_them(string text, string expected)
    {
        Assert.Equal(expected, FirstSplit(text));
    }

    [Theory]
    [InlineData("AreaOnEnter,Menu", "kind=chapter_exit")]
    [InlineData("AreaOnExit,Menu", "kind=chapter_enter")]
    [InlineData("AreaOnEnter,menu", "kind=chapter_exit")]
    [InlineData("AreaOnEnter, Menu ", "kind=chapter_exit")]
    [InlineData("AreaOnEnter,Menu-ASide", "kind=unknown raw=AreaOnEnter,Menu-ASide")]
    [InlineData("AreaOnExit,Menu-0", "kind=unknown raw=AreaOnExit,Menu-0")]
    [InlineData("AreaComplete,Menu", "kind=unknown raw=AreaComplete,Menu")]
    [InlineData("AreaOnEnter,ForsakenCity", "kind=chapter_enter chapter=1")]
    [InlineData("AreaOnEnter,forsakencity-bside", "kind=chapter_enter chapter=1 side=1")]
    [InlineData("AreaOnEnter, Core - 2 ", "kind=chapter_enter chapter=9 side=2")]
    [InlineData("AreaOnExit,TheSummit-CSide", "kind=chapter_exit chapter=7 side=2")]
    [InlineData("AreaOnExit,7", "kind=chapter_exit chapter=7")]
    [InlineData("AreaOnEnter,200", "kind=chapter_enter chapter=200")]
    [InlineData("AreaOnEnter,+5", "kind=chapter_enter chapter=5")]
    [InlineData("AreaComplete,Farewell", "kind=chapter_complete chapter=10 timing=chapter_complete")]
    [InlineData("AreaComplete,Core-CSide", "kind=chapter_complete chapter=9 side=2 timing=chapter_complete")]
    [InlineData("AreaOnEnter,Core-ASide-foo", "kind=chapter_enter chapter=9 side=0")]
    [InlineData("AreaOnEnter,Core-3", "kind=unknown raw=AreaOnEnter,Core-3")]
    [InlineData("AreaOnEnter,Core-None", "kind=unknown raw=AreaOnEnter,Core-None")]
    [InlineData("AreaOnEnter,Core-", "kind=unknown raw=AreaOnEnter,Core-")]
    [InlineData("AreaOnEnter,Core-X", "kind=unknown raw=AreaOnEnter,Core-X")]
    [InlineData("AreaOnEnter,-1", "kind=unknown raw=AreaOnEnter,-1")]
    [InlineData("AreaOnEnter,", "kind=unknown raw=AreaOnEnter,")]
    [InlineData("AreaOnEnter,Nowhere", "kind=unknown raw=AreaOnEnter,Nowhere")]
    [InlineData("AreaComplete,99999999999", "kind=unknown raw=AreaComplete,99999999999")]
    [InlineData("LevelEnter,a-01", "kind=room_enter room=a-01")]
    [InlineData("LevelExit,Room", "kind=room_exit room=Room")]
    [InlineData("LevelEnter,", "kind=unknown raw=LevelEnter,")]
    [InlineData("LevelEnter,a,b", "kind=room_enter room=a")]
    [InlineData("LevelEnter, a", "kind=room_enter room= a")]
    [InlineData("LevelEnter,a&b<c\"d", "kind=room_enter room=a&b<c\"d")]
    [InlineData("Foo<&>", "kind=unknown raw=Foo<&>")]
    [InlineData("Prologue,extra", "kind=chapter_complete chapter=0 timing=chapter_exit")]
    public void Odd_area_and_room_values_map_as_legacy_reads_them(string text, string expected)
    {
        Assert.Equal(expected, FirstSplit(text));
    }

    [Theory]
    [InlineData("", "True", "False", "kind=manual", "kind=chapter_exit", "file", "")]
    [InlineData("", "False", "False", "kind=manual", "kind=manual", "file", "")]
    [InlineData("", "True", "True", "kind=manual", "kind=chapter_exit", "file_since_start", "")]
    [InlineData("", "False", "True", "kind=manual", "kind=manual", "file_since_start", "")]
    [InlineData("Prologue|Chapter1", "True", "False", "kind=new_file", "kind=manual", "file", "kind=chapter_complete chapter=0 timing=chapter_exit|kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("Prologue|Chapter1", "False", "False", "kind=new_file", "kind=manual", "file", "kind=chapter_complete chapter=0 timing=chapter_exit|kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("Prologue|Chapter1", "True", "True", "kind=new_file", "kind=manual", "file_since_start", "kind=chapter_complete chapter=0 timing=chapter_exit|kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("Prologue|Chapter1", "False", "True", "kind=new_file", "kind=manual", "file_since_start", "kind=chapter_complete chapter=0 timing=chapter_exit|kind=chapter_complete chapter=1 timing=chapter_exit")]
    [InlineData("LevelEnter,a|Chapter1", "True", "False", "kind=chapter_start", "kind=chapter_exit", "chapter", "kind=room_enter room=a|kind=chapter_complete chapter=1 timing=chapter_complete")]
    [InlineData("LevelEnter,a|Chapter1", "False", "False", "kind=chapter_start", "kind=manual", "chapter", "kind=room_enter room=a|kind=chapter_complete chapter=1 timing=chapter_complete")]
    [InlineData("LevelEnter,a|Chapter1", "True", "True", "kind=chapter_start", "kind=chapter_exit", "file_since_start", "kind=room_enter room=a|kind=chapter_complete chapter=1 timing=chapter_complete")]
    [InlineData("LevelEnter,a|Chapter1", "False", "True", "kind=chapter_start", "kind=manual", "file_since_start", "kind=room_enter room=a|kind=chapter_complete chapter=1 timing=chapter_complete")]
    [InlineData("LevelExit,a|LevelEnter,b", "True", "False", "kind=room_exit room=a", "kind=chapter_exit", "chapter_since_start", "kind=room_enter room=b")]
    [InlineData("LevelExit,a|LevelEnter,b", "False", "False", "kind=room_exit room=a", "kind=manual", "chapter_since_start", "kind=room_enter room=b")]
    [InlineData("LevelExit,a|LevelEnter,b", "True", "True", "kind=room_exit room=a", "kind=chapter_exit", "file_since_start", "kind=room_enter room=b")]
    [InlineData("LevelExit,a|LevelEnter,b", "False", "True", "kind=room_exit room=a", "kind=manual", "file_since_start", "kind=room_enter room=b")]
    public void Each_case_crossed_with_the_booleans(string list, string autoReset, string fileTimeOffset, string start, string reset, string timingMethod, string splits)
    {
        XmlNode cfg = Cfg(Legacy(list.Length == 0 ? [] : list.Split('|'), ("AutoReset", autoReset), ("FileTimeOffset", fileTimeOffset)));

        Assert.Equal(start, Trigger(cfg, "start"));
        Assert.Equal(reset, Trigger(cfg, "reset"));
        Assert.Equal(timingMethod, Value(cfg, "timing_method"));
        Assert.Equal(splits, string.Join("|", Splits(cfg)));
        Assert.Equal("False", Value(cfg, "everest"));
    }

    [Theory]
    [InlineData("12345678", "kind=chapter_start", "kind=unknown raw=12345678")]
    [InlineData("12345678|Prologue", "kind=new_file", "kind=unknown raw=12345678|kind=chapter_complete chapter=0 timing=chapter_exit")]
    [InlineData("200", "kind=unknown raw=200", "")]
    [InlineData("ChapterA", "kind=chapter_start", "kind=chapter_complete timing=chapter_complete")]
    [InlineData("HeartGemAny", "kind=chapter_start", "kind=heart")]
    [InlineData("Chapter1HeartGem|Chapter2HeartGem", "kind=new_file", "kind=heart chapter=1|kind=heart chapter=2")]
    [InlineData("Chapter1Cassette", "kind=chapter_start", "kind=cassette chapter=1")]
    [InlineData("Chapter1Cassette|Chapter2Cassette", "kind=new_file", "kind=cassette chapter=1|kind=cassette chapter=2")]
    [InlineData("AreaComplete,Core", "kind=chapter_start", "kind=chapter_complete chapter=9 timing=chapter_complete")]
    [InlineData("AreaComplete,Core|AreaComplete,Farewell", "kind=new_file", "kind=chapter_complete chapter=9 timing=chapter_exit|kind=chapter_complete chapter=10 timing=chapter_exit")]
    [InlineData("AreaOnEnter,Core|AreaOnExit,Core", "kind=chapter_enter chapter=9", "kind=chapter_exit chapter=9")]
    [InlineData("Manual", "kind=manual", "")]
    public void The_list_is_classified_exactly_as_legacy_classifies_it(string list, string start, string splits)
    {
        XmlNode cfg = Cfg(Legacy(list.Split('|')));

        Assert.Equal(start, Trigger(cfg, "start"));
        Assert.Equal(splits, string.Join("|", Splits(cfg)));
    }

    [Theory]
    [InlineData(" True\r\n", "True")]
    [InlineData("true", "True")]
    [InlineData("FALSE", "False")]
    [InlineData("yes", "True")]
    [InlineData("", "True")]
    public void Booleans_parse_trimmed_and_fall_back_to_legacy_defaults(string text, string expected)
    {
        XmlNode cfg = Cfg(Legacy([], ("SetHighPriority", text)));

        Assert.Equal(expected, Value(cfg, "high_priority"));
        Assert.Equal("True", Value(cfg, "set_game_time"));
    }

    [Fact]
    public void An_invalid_FileTimeOffset_falls_back_to_false()
    {
        XmlNode cfg = Cfg(Legacy(["Chapter1"], ("FileTimeOffset", "yes")));

        Assert.Equal("chapter", Value(cfg, "timing_method"));
    }

    [Fact]
    public void SetGameTime_false_is_honoured()
    {
        XmlNode cfg = Cfg(Legacy([], ("SetGameTime", "False")));

        Assert.Equal("False", Value(cfg, "set_game_time"));
    }

    [Fact]
    public void A_node_with_only_AutoReset_is_legacy()
    {
        XmlDocument document = NewDocument();
        XmlElement node = document.CreateElement("AutoSplitterSettings");
        XmlElement autoReset = document.CreateElement("AutoReset");
        autoReset.InnerText = "False";
        node.AppendChild(autoReset);

        XmlNode migrated = LegacyMigration.Migrate(node);

        Assert.NotSame(node, migrated);
        Assert.Equal("kind=manual", Trigger(migrated.SelectSingleNode("CustomSettings/Setting[@id='cfg']"), "reset"));
    }

    [Fact]
    public void Legacy_keeps_each_boolean_only_when_present_and_the_splits_verbatim()
    {
        XmlNode legacy = Legacy(["Chapter1", " LevelEnter,x "], ("SetGameTime", " False "));

        XmlNode map = LegacyMigration.Migrate(legacy).SelectSingleNode("CustomSettings/Setting[@id='legacy']");

        Assert.Equal(
            "<Setting id=\"SetGameTime\" type=\"string\" value=\" False \" />" +
            "<Setting id=\"Splits\" type=\"list\"><Setting type=\"string\" value=\"Chapter1\" /><Setting type=\"string\" value=\" LevelEnter,x \" /></Setting>",
            map.InnerXml);
    }

    [Theory]
    [InlineData("<AutoSplitterSettings><Version>1.0</Version><CustomSettings><Setting id=\"schema_version\" type=\"i64\">1</Setting></CustomSettings></AutoSplitterSettings>")]
    [InlineData("<AutoSplitterSettings><Splits /><CustomSettings /></AutoSplitterSettings>")]
    [InlineData("<AutoSplitterSettings />")]
    [InlineData("<Settings><Version>1.0</Version></Settings>")]
    public void Other_nodes_pass_through_untouched(string xml)
    {
        XmlDocument document = NewDocument();
        document.LoadXml(xml);

        Assert.Same(document.DocumentElement, LegacyMigration.Migrate(document.DocumentElement));
    }

    private static string FirstSplit(string text)
    {
        return Splits(Cfg(Legacy([text, "Prologue"]))).First();
    }

    private static XmlNode Legacy(string[] splits, params (string Name, string Text)[] booleans)
    {
        XmlDocument document = NewDocument();
        XmlElement node = document.CreateElement("AutoSplitterSettings");
        foreach ((string name, string text) in booleans)
        {
            XmlElement element = document.CreateElement(name);
            element.InnerText = text;
            node.AppendChild(element);
        }

        XmlElement list = document.CreateElement("Splits");
        foreach (string split in splits)
        {
            XmlElement element = document.CreateElement("Split");
            element.InnerText = split;
            list.AppendChild(element);
        }

        node.AppendChild(list);
        return node;
    }

    private static XmlNode Cfg(XmlNode legacy)
    {
        return LegacyMigration.Migrate(legacy).SelectSingleNode("CustomSettings/Setting[@id='cfg']");
    }

    private static XmlDocument NewDocument()
    {
        return new XmlDocument { PreserveWhitespace = true };
    }

    private static string Trigger(XmlNode cfg, string id)
    {
        return Describe((XmlElement)cfg.SelectSingleNode($"Setting[@id='{id}']"));
    }

    private static IEnumerable<string> Splits(XmlNode cfg)
    {
        return cfg.SelectNodes("Setting[@id='splits']/Setting").Cast<XmlElement>().Select(Describe);
    }

    private static string Value(XmlNode cfg, string id)
    {
        XmlElement setting = (XmlElement)cfg.SelectSingleNode($"Setting[@id='{id}']");
        return setting.HasAttribute("value") ? setting.GetAttribute("value") : setting.InnerText;
    }

    private static string Describe(XmlElement trigger)
    {
        return string.Join(" ", trigger.ChildNodes.Cast<XmlElement>().Select(field =>
            field.GetAttribute("id") + "=" + (field.HasAttribute("value") ? field.GetAttribute("value") : field.InnerText)));
    }
}

public static class LegacyFiles
{
    private static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legacy-splits");

    public static IEnumerable<object[]> Names()
    {
        return Directory.GetFiles(Folder, "*.lss").Select(file => new object[] { Path.GetFileNameWithoutExtension(file) });
    }

    public static XmlElement Settings(string name)
    {
        var document = new XmlDocument();
        document.Load(Path.Combine(Folder, name + ".lss"));
        return document.DocumentElement["AutoSplitterSettings"];
    }

    public static XmlElement Expected(string name)
    {
        var document = new XmlDocument();
        document.Load(Path.Combine(Folder, name + ".expected.xml"));
        return document.DocumentElement;
    }
}
