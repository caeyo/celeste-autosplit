using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;
using Xunit;

namespace CelesteAutosplit.Tests;

[Collection(LiveSplitCollection.Name)]
public sealed class EditorTests : IDisposable
{
    private const string Header = "<Version>1.0</Version><CustomSettings>";
    private const string Footer = "</CustomSettings>";

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
    public void A_preset_sets_its_values_and_keeps_unknown_keys()
    {
        IComponent component = Create(1);
        SetCfg(
            component,
            "<Setting id=\"future\" type=\"string\" value=\"keep\" />"
            + "<Setting id=\"start\" type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_enter\" /><Setting id=\"chapter\" type=\"i64\">3</Setting><Setting id=\"mystery\" type=\"bool\">True</Setting></Setting>"
            + "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"other\" type=\"i64\">5</Setting></Setting></Setting>");
        Control editor = Open(component);

        OnUi(() => Press(Find<Button>(editor, "preset.0")));

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
    }

    [Fact]
    public void Changing_a_kind_keeps_fields_both_take_and_unknown_keys_but_drops_the_rest()
    {
        IComponent component = Create(1);
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\">"
            + "<Setting id=\"kind\" type=\"string\" value=\"room_enter\" />"
            + "<Setting id=\"room\" type=\"string\" value=\"b-00\" />"
            + "<Setting id=\"chapter\" type=\"i64\">3</Setting>"
            + "<Setting id=\"mystery\" type=\"bool\">True</Setting>"
            + "</Setting></Setting>");
        Control editor = Open(component);

        OnUi(() => Select(Find<ComboBox>(editor, "split.0.kind"), "Chapter complete"));

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
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"timing_method\" type=\"string\" value=\"future\" /><Setting id=\"everest\" type=\"bool\">True</Setting></Setting>"
            + Footer;
        SetSettings(component, newer);

        Control editor = Open(component);

        Assert.Contains("newer", OnUi(() => Find<Label>(editor, "warnings").Text));
        Assert.DoesNotContain("isn't valid", OnUi(() => Find<Label>(editor, "warnings").Text));
        Assert.False(OnUi(() => Find<CheckBox>(editor, "setting.everest").Enabled));
        Assert.False(OnUi(() => Find<Button>(editor, "preset.0").Parent.Enabled));
        Assert.False(OnUi(() => Find<ComboBox>(editor, "start.kind").Enabled));

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = false);

        Assert.Equal(newer, GetSettings(component));
    }

    [Fact]
    public void Invalid_and_unknown_triggers_are_flagged()
    {
        IComponent component = Create(7);
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

        Assert.Contains("wasn't recognised", Problem(editor, 0));
        Assert.Contains("has no room", Problem(editor, 1));
        Assert.Contains("invalid checkpoint", Problem(editor, 2));
        Assert.Contains("invalid side", Problem(editor, 3));
        Assert.Contains("unknown kind", Problem(editor, 4));
        Assert.Contains("isn't a map", Problem(editor, 5));
        Assert.Equal("", Problem(editor, 6));
        Assert.Contains("invalid or never fire", OnUi(() => Find<Label>(editor, "warnings").Text));
    }

    [Fact]
    public void Everest_only_fields_are_kept_while_everest_is_off()
    {
        IComponent component = Create(1);
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\"><Setting type=\"map\">"
            + "<Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" />"
            + "<Setting id=\"sid\" type=\"string\" value=\"Celeste/1-ForsakenCity\" />"
            + "</Setting></Setting>");
        Control editor = Open(component);

        Assert.Empty(OnUi(() => editor.Controls.Find("split.0.field.sid", true)));

        OnUi(() => Find<CheckBox>(editor, "setting.high_priority").Checked = false);
        Assert.Contains("<Setting id=\"sid\" type=\"string\" value=\"Celeste/1-ForsakenCity\" />", GetSettings(component));

        OnUi(() => Find<CheckBox>(editor, "setting.everest").Checked = true);
        Assert.Equal("Celeste/1-ForsakenCity", OnUi(() => Find<TextBox>(editor, "split.0.field.sid").Text));
    }

    [Fact]
    public void Setting_a_segment_past_the_end_of_the_list_fills_the_ones_before_it_with_manual()
    {
        IComponent component = Create(3);
        Control editor = Open(component);

        OnUi(() => Select(Find<ComboBox>(editor, "split.2.kind"), "Chapter complete"));

        Assert.Equal(
            Header
            + "<Setting id=\"schema_version\" type=\"i64\">1</Setting>"
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"splits\" type=\"list\">"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"manual\" /></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"manual\" /></Setting>"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /></Setting>"
            + "</Setting></Setting>"
            + Footer,
            GetSettings(component));
    }

    [Fact]
    public void Each_segment_has_one_row_and_list_entries_past_the_last_segment_are_kept()
    {
        const string Extra = "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"heart\" /></Setting>";
        IComponent component = Create(1);
        SetCfg(
            component,
            "<Setting id=\"splits\" type=\"list\">"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"cassette\" /></Setting>"
            + Extra
            + "</Setting>");
        Control editor = Open(component);

        Assert.Empty(OnUi(() => editor.Controls.Find("split.1.kind", true)));

        OnUi(() => Select(Find<ComboBox>(editor, "split.0.kind"), "Chapter complete"));

        Assert.Equal(
            Header
            + "<Setting id=\"schema_version\" type=\"i64\">1</Setting>"
            + "<Setting id=\"cfg\" type=\"map\"><Setting id=\"splits\" type=\"list\">"
            + "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /></Setting>"
            + Extra
            + "</Setting></Setting>"
            + Footer,
            GetSettings(component));
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

    private IComponent Create(int segments = 0)
    {
        IComponent component = OnUi(() =>
        {
            LiveSplitState state = host.NewState();
            for (int i = 0; i < segments; i++)
            {
                state.Run.Add(new Segment($"Segment {i + 1}"));
            }

            return new CelesteAutosplitFactory().Create(state);
        });
        created.Add(component);
        return component;
    }

    private Control Open(IComponent component)
    {
        return OnUi(() =>
        {
            Control editor = component.GetSettingsControl(LayoutMode.Vertical);
            _ = editor.Handle;
            return editor;
        });
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

    private static void Press(Button button)
    {
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, [EventArgs.Empty]);
    }

    private string Problem(Control editor, int segment)
    {
        return OnUi(() => Find<Label>(editor, $"split.{segment}.problem").Text);
    }
}
