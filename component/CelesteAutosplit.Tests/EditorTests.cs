using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using Xunit;

namespace CelesteAutosplit.Tests;

[Collection(LiveSplitCollection.Name)]
public sealed class EditorTests : IDisposable
{
    private const string Header = "<Version>1.0</Version><CustomSettings>";
    private const string Footer = "</CustomSettings>";

    private const string Row1 = "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"chapter\" type=\"i64\">1</Setting></Setting>";
    private const string Row2 = "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"chapter\" type=\"i64\">2</Setting></Setting>";

    private readonly LiveSplitHost host;
    private readonly List<IComponent> created = [];

    public EditorTests(LiveSplitHost host)
    {
        this.host = host;
    }

    [Fact]
    public void A_change_on_an_empty_map_writes_only_schema_version_and_that_setting()
    {
        IComponent component = Create();
        Control editor = Open(component);

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = true);

        Assert.Equal(
            Header
            + "<Setting id=\"schema_version\" type=\"i64\">1</Setting>"
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"everest\" type=\"bool\">True</Setting></Setting>"
            + Footer,
            GetSettings(component));
    }

    [Fact]
    public void A_preset_sets_its_values_keeps_unknown_keys_and_is_shown_until_something_differs()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"future\" type=\"string\" value=\"keep\" />"
            + "<Setting id=\"start\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_enter\" /><Setting id=\"chapter\" type=\"i64\">3</Setting><Setting id=\"mystery\" type=\"bool\">True</Setting></Setting>"
            + "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"other\" type=\"i64\">5</Setting></Setting></Setting>");
        Control editor = Open(component);
        Assert.Equal("Custom", OnUi(() => Find<ComboBox>(editor, "preset").Text));

        OnUi(() => Select(Find<ComboBox>(editor, "preset"), "Full game"));

        Assert.Equal(
            Header
            + "<Setting id=\"schema_version\" type=\"i64\">1</Setting>"
            + "<Setting id=\"cfg\" type=\"map\">"
            + "<Setting id=\"future\" type=\"string\" value=\"keep\" />"
            + "<Setting id=\"start\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"new_file\" /><Setting id=\"mystery\" type=\"bool\">True</Setting></Setting>"
            + "<Setting id=\"reset\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"new_file\" /></Setting>"
            + "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"timing\" type=\"string\" value=\"chapter_exit\" /><Setting id=\"other\" type=\"i64\">5</Setting></Setting></Setting>"
            + "<Setting id=\"timing_method\" type=\"string\" value=\"file\" />"
            + "</Setting>"
            + Footer,
            GetSettings(component));
        Assert.Equal("Full game", OnUi(() => Find<ComboBox>(editor, "preset").Text));

        OnUi(() => Select(Find<ComboBox>(editor, "setting.timing_method"), "Chapter time"));

        Assert.Equal("Custom", OnUi(() => Find<ComboBox>(editor, "preset").Text));
    }

    [Fact]
    public void A_configuration_matching_a_preset_opens_as_that_preset_with_defaults_filling_absent_fields()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"start\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_start\" /></Setting>"
            + "<Setting id=\"reset\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_exit\" /></Setting>"
            + "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /></Setting></Setting>"
            + "<Setting id=\"timing_method\" type=\"string\" value=\"chapter\" />");

        Control editor = Open(component);

        Assert.Equal("Individual level", OnUi(() => Find<ComboBox>(editor, "preset").Text));
    }

    [Fact]
    public void Changing_a_kind_keeps_fields_both_take_and_unknown_keys_but_drops_the_rest()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\">"
            + "<Setting id=\"kind\" type=\"string\" value=\"room_enter\" />"
            + "<Setting id=\"room\" type=\"string\" value=\"b-00\" />"
            + "<Setting id=\"chapter\" type=\"i64\">3</Setting>"
            + "<Setting id=\"mystery\" type=\"bool\">True</Setting>"
            + "</Setting></Setting>");
        Control editor = Open(component);

        OnUi(() =>
        {
            SelectRow(editor, 2);
            Select(Find<ComboBox>(editor, "kind"), "Chapter complete");
        });

        Assert.Equal(
            Header
            + "<Setting id=\"schema_version\" type=\"i64\">1</Setting>"
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"splits\" type=\"list\"><Setting type=\"map\">"
            + "<Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" />"
            + "<Setting id=\"chapter\" type=\"i64\">3</Setting>"
            + "<Setting id=\"mystery\" type=\"bool\">True</Setting>"
            + "</Setting></Setting></Setting>"
            + Footer,
            GetSettings(component));
    }

    [Fact]
    public void A_settings_map_older_editors_cannot_read_is_read_only_and_written_nowhere()
    {
        IComponent component = Create();
        string newer = Header
            + "<Setting id=\"schema_version\" type=\"i64\">99</Setting>"
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"everest\" type=\"bool\">True</Setting></Setting>"
            + Footer;
        SetSettings(component, newer);

        Control editor = Open(component);

        Assert.Contains("newer", OnUi(() => Find<Label>(editor, "warnings").Text));
        Assert.False(OnUi(() => Find<CheckBox>(editor, "setting.everest").Enabled));
        Assert.False(OnUi(() => Find<ComboBox>(editor, "preset").Enabled));
        Assert.False(OnUi(() => Find<ComboBox>(editor, "kind").Enabled));

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = false);

        Assert.Equal(newer, GetSettings(component));
    }

    [Fact]
    public void Invalid_and_unknown_triggers_are_flagged()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\">"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"unknown\" /><Setting id=\"raw\" type=\"string\" value=\"Bogus,x\" /></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"room_enter\" /></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"checkpoint\" /><Setting id=\"chapter\" type=\"i64\">0</Setting><Setting id=\"checkpoint\" type=\"i64\">1</Setting></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"side\" type=\"i64\">7</Setting></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"no_such_kind\" /></Setting>"
            + "<Setting type=\"string\" value=\"not a map\" />"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"chapter\" type=\"i64\">1</Setting></Setting>"
            + "</Setting>");

        Control editor = Open(component);
        var rows = OnUi(() => Rows(editor));

        Assert.StartsWith("Start: ", rows[0]);
        Assert.StartsWith("Reset: ", rows[1]);
        for (int row = 2; row < 8; row++)
        {
            Assert.StartsWith("[!] ", rows[row]);
        }

        Assert.DoesNotContain("[!]", rows[8]);

        OnUi(() => SelectRow(editor, 2));
        Assert.Contains("wasn't recognised", OnUi(() => Find<Label>(editor, "problem").Text));
        Assert.Contains("Bogus,x", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 3));
        Assert.Contains("has no room", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 4));
        Assert.Contains("invalid checkpoint", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 5));
        Assert.Contains("invalid side", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 6));
        Assert.Contains("unknown kind", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 7));
        Assert.Contains("isn't a map", OnUi(() => Find<Label>(editor, "problem").Text));
        OnUi(() => SelectRow(editor, 8));
        Assert.Equal("", OnUi(() => Find<Label>(editor, "problem").Text));
    }

    [Theory]
    [InlineData("chapter_exit", null, true)]
    [InlineData("chapter_exit", 1L, true)]
    [InlineData("chapter_exit", 2L, false)]
    [InlineData("chapter_start", null, true)]
    [InlineData("chapter_start", 1L, false)]
    [InlineData("chapter_start", 2L, true)]
    [InlineData("new_file", null, false)]
    [InlineData("manual", null, false)]
    public void A_reset_the_run_passes_is_warned_about(string kind, long? chapter, bool warns)
    {
        IComponent component = Create();
        string chapterSetting = chapter == null ? "" : $"<Setting id=\"chapter\" type=\"i64\">{chapter}</Setting>";
        SetCfg(
            component,
            $"<Setting id=\"reset\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"{kind}\" />{chapterSetting}</Setting>"
            + $"<Setting id=\"splits\" type=\"list\">{Row1}{Row2}</Setting>");

        Control editor = Open(component);

        string text = OnUi(() => Find<Label>(editor, "warnings").Text);
        if (warns)
        {
            Assert.Contains("passes this reset", text);
        }
        else
        {
            Assert.Equal("", text);
        }
    }

    [Fact]
    public void A_reset_in_a_run_through_one_chapter_is_not_warned_about()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"reset\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_exit\" /></Setting>"
            + $"<Setting id=\"splits\" type=\"list\">{Row1}</Setting>");

        Control editor = Open(component);

        Assert.Equal("", OnUi(() => Find<Label>(editor, "warnings").Text));
    }

    [Fact]
    public void Everest_only_fields_are_kept_while_everest_is_off()
    {
        IComponent component = Create();
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\">"
            + "<Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" />"
            + "<Setting id=\"sid\" type=\"string\" value=\"Celeste/1-ForsakenCity\" />"
            + "</Setting></Setting>");
        Control editor = Open(component);

        OnUi(() => SelectRow(editor, 2));
        Assert.False(OnUi(() => Find<TextBox>(editor, "field.sid").Visible));

        OnUi(() => Find<CheckBox>(editor, "setting.high_priority").Checked = false);
        Assert.Contains("<Setting id=\"sid\" type=\"string\" value=\"Celeste/1-ForsakenCity\" />", GetSettings(component));

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = true);
        Assert.True(OnUi(() => Find<TextBox>(editor, "field.sid").Visible));
        Assert.Equal("Celeste/1-ForsakenCity", OnUi(() => Find<TextBox>(editor, "field.sid").Text));
    }

    [Fact]
    public void A_modded_chapter_id_can_be_typed_and_an_invalid_one_is_reported()
    {
        IComponent component = Create();
        SetCfg(component, "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /></Setting></Setting>");
        Control editor = Open(component);

        OnUi(() =>
        {
            SelectRow(editor, 2);
            Find<ComboBox>(editor, "field.chapter").Text = "12";
        });

        Assert.Contains("<Setting id=\"chapter\" type=\"i64\">12</Setting>", GetSettings(component));

        OnUi(() => Find<ComboBox>(editor, "field.chapter").Text = "abc");

        Assert.Contains("<Setting id=\"chapter\" type=\"i64\">12</Setting>", GetSettings(component));
        Assert.Contains("isn't a chapter", OnUi(() => Find<Label>(editor, "problem").Text));
    }

    [Fact]
    public void The_checkpoint_list_follows_the_chosen_chapter()
    {
        IComponent component = Create();
        SetCfg(component, "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"checkpoint\" /><Setting id=\"chapter\" type=\"i64\">1</Setting><Setting id=\"checkpoint\" type=\"i64\">2</Setting></Setting></Setting>");
        Control editor = Open(component);

        OnUi(() => SelectRow(editor, 2));
        Assert.Equal("Chasm", OnUi(() => Find<ComboBox>(editor, "field.checkpoint").Text));

        OnUi(() => Select(Find<ComboBox>(editor, "field.chapter"), "Old Site"));

        Assert.Equal(["Intervention", "Awake"], OnUi(() => Find<ComboBox>(editor, "field.checkpoint").Items.Cast<object>().Select(item => item.ToString()).ToList()));
        Assert.Contains("<Setting id=\"chapter\" type=\"i64\">2</Setting>", GetSettings(component));
    }

    [Fact]
    public void Splits_can_be_inserted_removed_and_moved()
    {
        IComponent component = Create();
        SetCfg(component, $"<Setting id=\"splits\" type=\"list\">{Row1}{Row2}</Setting>");
        Control editor = Open(component);
        const string Manual = "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"manual\" /></Setting>";

        OnUi(() =>
        {
            SelectRow(editor, 3);
            Find<Button>(editor, "insert").PerformClick();
        });
        Assert.Contains($"<Setting id=\"splits\" type=\"list\">{Row1}{Manual}{Row2}</Setting>", GetSettings(component));

        OnUi(() =>
        {
            SelectRow(editor, 4);
            Find<Button>(editor, "up").PerformClick();
        });
        Assert.Contains($"<Setting id=\"splits\" type=\"list\">{Row1}{Row2}{Manual}</Setting>", GetSettings(component));

        OnUi(() =>
        {
            SelectRow(editor, 4);
            Find<Button>(editor, "remove").PerformClick();
        });
        Assert.Contains($"<Setting id=\"splits\" type=\"list\">{Row1}{Row2}</Setting>", GetSettings(component));
    }

    [Fact]
    public void Rows_show_the_segments_of_the_run()
    {
        LiveSplitState state = OnUi(() =>
        {
            LiveSplitState newState = host.NewState();
            newState.Run.Add(new Segment("Forsaken City"));
            newState.Run.Add(new Segment("Old Site"));
            return newState;
        });
        IComponent component = Create(state);
        SetCfg(component, $"<Setting id=\"splits\" type=\"list\">{Row1}</Setting>");

        Control editor = Open(component);
        var rows = OnUi(() => Rows(editor));

        Assert.Equal(4, rows.Count);
        Assert.Equal("1. Forsaken City: Chapter complete (Forsaken City)", rows[2]);
        Assert.Equal("2. Old Site: Manual", rows[3]);
    }

    [Fact]
    public void Cancel_restores_what_was_there_and_the_editor_shows_it_again()
    {
        IComponent component = Create();
        SetCfg(component, "<Setting id=\"everest\" type=\"bool\">True</Setting>");
        Control editor = Open(component);
        XmlNode snapshot = OnUi(() => component.GetSettings(new XmlDocument()));
        string before = GetSettings(component);

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = false);
        Assert.NotEqual(before, GetSettings(component));

        OnUi(() => component.SetSettings(snapshot));
        Assert.Equal(before, GetSettings(component));

        Control reopened = Open(component);
        Assert.True(OnUi(() => Find<CheckBox>(reopened, "setting.everest").Checked));
    }

    [Fact]
    public void The_editor_reads_the_settings_each_time_it_opens()
    {
        IComponent component = Create();
        Control editor = Open(component);
        Assert.False(OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked));

        SetCfg(component, "<Setting id=\"everest\" type=\"bool\">True</Setting>");

        Control reopened = Open(component);
        Assert.True(OnUi(() => Find<CheckBox>(reopened, "setting.everest").Checked));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_first_settings_switch_to_game_time_only_if_set_game_time_is_on(bool? value, bool switches)
    {
        LiveSplitState state = OnUi(() => host.NewState());
        IComponent component = Create(state);
        string setting = value == null ? "" : $"<Setting id=\"set_game_time\" type=\"bool\">{value}</Setting>";

        SetCfg(component, setting);

        Assert.Equal(switches ? TimingMethod.GameTime : TimingMethod.RealTime, OnUi(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Later_settings_do_not_switch_to_game_time_again()
    {
        LiveSplitState state = OnUi(() => host.NewState());
        IComponent component = Create(state);
        SetCfg(component, "");
        OnUi(() => state.CurrentTimingMethod = TimingMethod.RealTime);

        SetCfg(component, "");

        Assert.Equal(TimingMethod.RealTime, OnUi(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Cancelling_the_dialog_is_not_the_first_settings_for_game_time()
    {
        LiveSplitState state = OnUi(() => host.NewState());
        IComponent component = Create(state);
        Open(component);
        XmlNode snapshot = OnUi(() => component.GetSettings(new XmlDocument()));

        OnUi(() => component.SetSettings(snapshot));

        Assert.Equal(TimingMethod.RealTime, OnUi(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Migrated_settings_switch_to_game_time_as_their_set_game_time_says()
    {
        LiveSplitState state = OnUi(() => host.NewState());
        IComponent component = Create(state);
        var document = new XmlDocument();
        document.LoadXml("<AutoSplitterSettings><AutoReset>True</AutoReset><SetGameTime>False</SetGameTime><Splits /></AutoSplitterSettings>");

        OnUi(() => component.SetSettings(document.DocumentElement));

        Assert.Equal(TimingMethod.RealTime, OnUi(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void A_legacy_component_in_the_layout_is_warned_about_and_its_settings_can_be_imported()
    {
        LiveSplitState state = OnUi(() =>
        {
            LiveSplitState newState = host.NewState();
            newState.Layout = new LiveSplit.UI.Layout();
            newState.Run.GameName = "Celeste";
            return newState;
        });
        var legacy = new LegacyStandIn(LegacyFiles.Settings("1a-rooms"));
        OnUi(() => state.Layout.LayoutComponents.Add(new LayoutComponent("LiveSplit.Celeste.dll", legacy)));
        var component = (CelesteAutosplitComponent)Create(state);

        Control editor = Open(component);
        Assert.Contains("LiveSplit.Celeste.dll", OnUi(() => Find<Label>(editor, "warnings").Text));

        OnUi(() => component.ImportLegacySettings(legacy));

        string expected = LegacyFiles.Expected("1a-rooms").InnerXml;
        Assert.Equal(expected, GetSettings(component));
        Assert.Equal(expected, OnUi(() => state.Run.AutoSplitterSettings.InnerXml));
        Assert.Equal("Celeste", OnUi(() => state.Run.AutoSplitterSettings.GetAttribute("gameName")));
        Assert.True(OnUi(() => state.Run.HasChanged));
    }

    private sealed class LegacyStandIn : LogicComponent
    {
        private readonly XmlElement settings;

        public LegacyStandIn(XmlElement settings)
        {
            this.settings = settings;
        }

        public override string ComponentName => "Legacy";
        public override Control GetSettingsControl(LayoutMode mode) => null;
        public override XmlNode GetSettings(XmlDocument document) => document.ImportNode(settings, true);
        public override void SetSettings(XmlNode settings) { }
        public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode) { }
        public override void Dispose() { }
    }

    public void Dispose()
    {
        OnUi(() =>
        {
            foreach (IComponent component in created)
            {
                component.Dispose();
            }
        });
    }

    private IComponent Create(LiveSplitState state = null)
    {
        IComponent component = OnUi(() => new CelesteAutosplitFactory().Create(state ?? host.NewState()));
        created.Add(component);
        return component;
    }

    private Control Open(IComponent component)
    {
        return OnUi(() => component.GetSettingsControl(LayoutMode.Vertical));
    }

    private void SetCfg(IComponent component, string cfg)
    {
        SetSettings(component, Header + "<Setting id=\"schema_version\" type=\"i64\">1</Setting><Setting id=\"cfg\" type=\"map\">" + cfg + "</Setting>" + Footer);
    }

    private void SetSettings(IComponent component, string customSettings)
    {
        var document = new XmlDocument();
        document.LoadXml("<AutoSplitterSettings>" + customSettings + "</AutoSplitterSettings>");
        XmlElement element = document.DocumentElement;
        OnUi(() => component.SetSettings(element));
    }

    private string GetSettings(IComponent component)
    {
        return OnUi(() => component.GetSettings(new XmlDocument()).InnerXml);
    }

    private T OnUi<T>(Func<T> func) => host.OnUiThread(func);

    private void OnUi(Action action) => host.OnUiThread(action);

    private static T Find<T>(Control editor, string name) where T : Control
    {
        return (T)editor.Controls.Find(name, true).Single();
    }

    private static void Select(ComboBox combo, string label)
    {
        combo.SelectedIndex = combo.Items.Cast<object>().ToList().FindIndex(item => item.ToString() == label);
    }

    private static void SelectRow(Control editor, int index)
    {
        Find<ListBox>(editor, "triggers").SelectedIndex = index;
    }

    private static List<string> Rows(Control editor)
    {
        return Find<ListBox>(editor, "triggers").Items.Cast<object>().Select(item => item.ToString()).ToList();
    }
}
