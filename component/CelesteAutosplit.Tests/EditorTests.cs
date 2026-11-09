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

    private IComponent Create()
    {
        IComponent component = OnUi(() => new CelesteAutosplitFactory().Create(host.NewState()));
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
