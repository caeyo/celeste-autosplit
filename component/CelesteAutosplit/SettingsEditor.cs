using LiveSplit.Model;
using LiveSplit.Options;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace CelesteAutosplit;

internal sealed class SettingsEditor : UserControl
{
    private const int ContentWidth = 454;
    private const int KindWidth = 180;
    private const int TextFieldWidth = 110;
    private const int ComboChrome = 24;
    private const int RowHeight = 21;
    private const int RowGap = 6;
    private const int EmSetCueBanner = 0x1501;
    private const int CbSetCueBanner = 0x1703;

    private static readonly object Keep = new();
    private static readonly string[] TopLevelOrder = ["schema_version", "cfg"];
    private static readonly string[] FlagSettings = ["set_game_time", "high_priority", "everest"];
    private static readonly string[] TriggerSettings = ["reset", "start"];

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

    private sealed class Choice
    {
        public string Label;
        public object Value;

        public override string ToString() => Label;
    }

    private sealed class FieldInput
    {
        public SchemaField Field;
        public Control Input;
    }

    private sealed class TriggerRow
    {
        public string Id;
        public SchemaSetting Setting;
        public int Index;
        public FlowLayoutPanel Panel;
        public ComboBox Kind;
        public FlowLayoutPanel Fields;
        public Label Problem;
        public List<FieldInput> Inputs = [];

        public bool IsSplit => Setting.Type == "list";
    }

    private sealed class Combo : ComboBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (DroppedDown)
            {
                base.OnMouseWheel(e);
                return;
            }

            if (e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }

            for (Control parent = Parent; parent != null; parent = parent.Parent)
            {
                if (parent is ScrollableControl { AutoScroll: true } scroller)
                {
                    scroller.AutoScrollPosition = new Point(0, -scroller.AutoScrollPosition.Y - e.Delta);
                    return;
                }
            }
        }
    }

    private readonly LiveSplitState state;
    private readonly SettingsSchema schema;
    private readonly Func<XmlElement> read;
    private readonly Action<XmlElement> write;
    private readonly List<string> cfgOrder;
    private readonly SchemaSetting splitsSetting;
    private readonly SchemaSetting everestSetting;
    private readonly Dictionary<SchemaField, int> fieldWidths;

    private readonly ToolTip tips = new();
    private readonly Label status;
    private readonly Label warnings;
    private readonly FlowLayoutPanel presets;
    private readonly FlowLayoutPanel segments;
    private readonly Dictionary<SchemaSetting, Control> settingInputs = [];
    private readonly List<TriggerRow> rows = [];

    private List<string> segmentNames = [];
    private XmlElement settings;
    private XmlElement map;
    private XmlElement cfg;
    private bool readOnly;
    private bool loading;

    public SettingsEditor(LiveSplitState state, SettingsSchema schema, string statusText, Func<XmlElement> read, Action<XmlElement> write)
    {
        this.state = state;
        this.schema = schema;
        this.read = read;
        this.write = write;
        cfgOrder = schema.Settings.Select(setting => setting.Key).ToList();
        splitsSetting = schema.Settings.First(setting => setting.Type == "list");
        everestSetting = schema.Setting("everest");
        fieldWidths = schema.Fields.ToDictionary(field => field, MeasureField);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Name = "settingsEditor";

        FlowLayoutPanel column = NewLine(FlowDirection.TopDown);
        column.Padding = new Padding(3);
        Controls.Add(column);

        status = NewWrappedLabel("status", SystemColors.ControlText);
        status.Text = statusText;
        status.Visible = statusText != null;
        column.Controls.Add(status);

        warnings = NewWrappedLabel("warnings", Color.DarkRed);
        warnings.Visible = false;
        column.Controls.Add(warnings);

        FlowLayoutPanel flags = NewLine(FlowDirection.LeftToRight);
        flags.Margin = new Padding(0, 0, 0, RowGap);
        foreach (string key in FlagSettings)
        {
            flags.Controls.Add(NewCheckBox(schema.Setting(key)));
        }

        column.Controls.Add(flags);

        presets = NewLine(FlowDirection.LeftToRight);
        presets.Margin = new Padding(0, 0, 0, RowGap);
        presets.Controls.Add(new Label
        {
            Text = "Apply preset",
            AutoSize = true,
            MinimumSize = new Size(0, RowHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 3, 0),
        });
        for (int i = 0; i < schema.Presets.Count; i++)
        {
            presets.Controls.Add(NewPresetButton(i));
        }

        column.Controls.Add(presets);

        foreach (SchemaSetting setting in schema.Settings.Where(setting => setting.Type == "string" && setting.Values != null))
        {
            column.Controls.Add(NewRadioButtons(setting));
        }

        foreach (string key in TriggerSettings)
        {
            SchemaSetting setting = schema.Setting(key);
            TriggerRow row = NewRow(key, setting.Label, setting.Description, setting, 0);
            rows.Add(row);
            column.Controls.Add(NewRule());
            column.Controls.Add(row.Panel);
        }

        column.Controls.Add(NewRule());
        column.Controls.Add(new Label
        {
            Text = "Segments",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, RowGap),
        });
        segments = NewLine(FlowDirection.TopDown);
        column.Controls.Add(segments);
    }

    private static FlowLayoutPanel NewLine(FlowDirection direction)
    {
        return new FlowLayoutPanel
        {
            FlowDirection = direction,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
    }

    private static Label NewRule()
    {
        return new Label
        {
            AutoSize = false,
            Size = new Size(ContentWidth, 2),
            BorderStyle = BorderStyle.Fixed3D,
            Margin = new Padding(0, RowGap, 0, RowGap + 2),
        };
    }

    private static Label NewWrappedLabel(string name, Color color)
    {
        return new Label
        {
            Name = name,
            ForeColor = color,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = new Padding(0, 0, 0, RowGap),
        };
    }

    private static Combo NewCombo(string name, ComboBoxStyle style, int width)
    {
        return new Combo
        {
            Name = name,
            DropDownStyle = style,
            Width = width,
            Margin = new Padding(0, 0, 3, 0),
        };
    }

    private static void SetCue(Control input, string text)
    {
        int message = input is ComboBox ? CbSetCueBanner : EmSetCueBanner;
        IntPtr showWhenFocused = input is ComboBox ? IntPtr.Zero : (IntPtr)1;
        input.HandleCreated += (_, _) => SendMessage(input.Handle, message, showWhenFocused, text);
    }

    private static string AnyLabel(SchemaField field) => $"Any {field.Label.ToLowerInvariant()}";

    private int MeasureField(SchemaField field)
    {
        IEnumerable<string> labels = field.ValuesFrom switch
        {
            "chapters" => schema.Chapters.Select(chapter => chapter.Name),
            "checkpoints" => schema.Chapters.SelectMany(chapter => chapter.Checkpoints).SelectMany(checkpoint => checkpoint.Names),
            _ => field.Values?.Select(choice => choice.Label),
        };
        if (labels == null)
        {
            return TextFieldWidth;
        }

        return labels.Append(AnyLabel(field)).Max(label => TextRenderer.MeasureText(label, Font).Width) + ComboChrome;
    }

    private CheckBox NewCheckBox(SchemaSetting setting)
    {
        var box = new CheckBox
        {
            Name = "setting." + setting.Key,
            Text = setting.Label,
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0),
        };
        if (setting.Description != null)
        {
            tips.SetToolTip(box, setting.Description);
        }

        box.CheckedChanged += (_, _) => Guard(() => SettingChanged(setting, box.Checked));
        settingInputs[setting] = box;
        return box;
    }

    private Button NewPresetButton(int index)
    {
        SchemaPreset entry = schema.Presets[index];
        var button = new Button
        {
            Name = $"preset.{index}",
            Text = entry.Label,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 3, 0),
        };
        button.Click += (_, _) => Guard(() => ApplyPreset(entry));
        return button;
    }

    private FlowLayoutPanel NewRadioButtons(SchemaSetting setting)
    {
        FlowLayoutPanel group = NewLine(FlowDirection.LeftToRight);
        group.Controls.Add(new Label
        {
            Text = setting.Label,
            AutoSize = true,
            Margin = new Padding(0, 2, 6, 0),
        });
        foreach (SchemaChoice choice in setting.Values)
        {
            var radio = new RadioButton
            {
                Name = $"setting.{setting.Key}.{choice.Value}",
                Text = choice.Label,
                Tag = choice.Value,
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0),
            };
            radio.CheckedChanged += (_, _) => Guard(() =>
            {
                if (radio.Checked)
                {
                    SettingChanged(setting, radio.Tag);
                }
            });
            group.Controls.Add(radio);
        }

        settingInputs[setting] = group;
        return group;
    }

    private TriggerRow NewRow(string id, string label, string description, SchemaSetting setting, int index)
    {
        var row = new TriggerRow
        {
            Id = id,
            Setting = setting,
            Index = index,
            Panel = NewLine(FlowDirection.TopDown),
            Kind = NewCombo($"{id}.kind", ComboBoxStyle.DropDownList, KindWidth),
            Fields = NewLine(FlowDirection.LeftToRight),
            Problem = NewWrappedLabel($"{id}.problem", Color.DarkRed),
        };
        row.Panel.Margin = new Padding(0, 0, 0, RowGap + 4);
        row.Kind.SelectedIndexChanged += (_, _) => Guard(() => KindChanged(row));
        row.Fields.Margin = new Padding(0, RowGap, 0, 0);
        row.Fields.Visible = false;
        row.Problem.Margin = new Padding(0, RowGap, 0, 0);
        row.Problem.Visible = false;

        var caption = new Label
        {
            Text = label,
            AutoSize = false,
            AutoEllipsis = true,
            Size = new Size(ContentWidth - KindWidth - 3, RowHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
        };
        tips.SetToolTip(caption, description ?? label);

        FlowLayoutPanel header = NewLine(FlowDirection.LeftToRight);
        header.Controls.Add(caption);
        header.Controls.Add(row.Kind);
        row.Panel.Controls.Add(header);
        row.Panel.Controls.Add(row.Fields);
        row.Panel.Controls.Add(row.Problem);
        return row;
    }

    private FieldInput NewFieldInput(TriggerRow row, SchemaField field)
    {
        string name = $"{row.Id}.field.{field.Name}";
        int width = fieldWidths[field];
        Control input;
        if (field.ReadOnly)
        {
            input = new Label
            {
                Name = name,
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
            };
        }
        else if (field.ValuesFrom == "chapters")
        {
            Combo combo = NewCombo(name, ComboBoxStyle.DropDown, width);
            combo.SelectedIndexChanged += (_, _) => Guard(() => FieldChanged(row, field, combo));
            combo.TextChanged += (_, _) => Guard(() => FieldChanged(row, field, combo));
            SetCue(combo, field.Label);
            input = combo;
        }
        else if (field.Values != null || field.ValuesFrom != null)
        {
            Combo combo = NewCombo(name, ComboBoxStyle.DropDownList, width);
            combo.SelectedIndexChanged += (_, _) => Guard(() => FieldChanged(row, field, combo));
            SetCue(combo, field.Label);
            input = combo;
        }
        else
        {
            var text = new TextBox
            {
                Name = name,
                Width = width,
                Margin = new Padding(0, 0, 3, 0),
            };
            text.TextChanged += (_, _) => Guard(() => FieldChanged(row, field, text));
            SetCue(text, field.Label);
            input = text;
        }

        tips.SetToolTip(input, field.Description ?? field.Label);
        return new FieldInput { Field = field, Input = input };
    }

    private void Guard(Action action)
    {
        if (loading)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            status.Text = $"Something went wrong in the settings editor: {ex.GetType().Name}: {ex.Message}";
            status.Visible = true;
        }
    }

    public void Reload()
    {
        settings = read();
        map = settings["CustomSettings"];
        if (map == null)
        {
            map = settings.OwnerDocument.CreateElement("CustomSettings");
            settings.AppendChild(map);
        }

        cfg = SettingsXml.FindMap(map, "cfg");
        readOnly = schema.IsNewer(map);

        if (IsHandleCreated)
        {
            ShowSettings();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Guard(ShowSettings);
    }

    private void ShowSettings()
    {
        WithoutEvents(() =>
        {
            List<string> names = state.Run?.Select(segment => segment.Name).ToList() ?? [];
            bool changed = !names.SequenceEqual(segmentNames);
            if (changed)
            {
                ReplaceSplitRows(names);
            }

            RenderAll();
            if (changed)
            {
                segments.Controls.AddRange(rows.Where(row => row.IsSplit).Select(row => (Control)row.Panel).ToArray());
            }
        });
    }

    private void ReplaceSplitRows(List<string> names)
    {
        segmentNames = names;
        rows.RemoveAll(row => row.IsSplit);
        foreach (Control old in segments.Controls.Cast<Control>().ToList())
        {
            old.Dispose();
        }

        for (int i = 0; i < names.Count; i++)
        {
            rows.Add(NewRow($"split.{i}", names[i], names[i], splitsSetting, i));
        }
    }

    private XmlElement SplitsList()
    {
        XmlElement list = SettingsXml.Find(cfg, splitsSetting.Key);
        return SettingsXml.Type(list) == "list" ? list : null;
    }

    private bool Everest => SettingsXml.Value(SettingsXml.Find(cfg, everestSetting.Key)) is bool value
        ? value
        : everestSetting.Default is true;

    private XmlElement TriggerOf(TriggerRow row)
    {
        if (row.IsSplit)
        {
            return SettingsXml.Items(SplitsList()).ElementAtOrDefault(row.Index);
        }

        return SettingsXml.Find(cfg, row.Setting.Key);
    }

    private static string KindName(XmlElement trigger)
    {
        return SettingsXml.Value(SettingsXml.Find(trigger, "kind")) as string;
    }

    private static string DefaultKind(SchemaSetting setting)
    {
        if (setting.Default is Dictionary<string, object> trigger && trigger.TryGetValue("kind", out object name))
        {
            return (string)name;
        }

        return "manual";
    }

    private void WithoutEvents(Action action)
    {
        bool previous = loading;
        loading = true;
        try
        {
            action();
        }
        finally
        {
            loading = previous;
        }
    }

    private void RenderAll()
    {
        presets.Enabled = !readOnly;
        RenderSettings();
        RenderRows();
        RenderWarnings();
    }

    private void RenderRows()
    {
        segments.SuspendLayout();
        foreach (TriggerRow row in rows)
        {
            RenderRow(row, null);
        }

        segments.ResumeLayout();
    }

    private void RenderSettings()
    {
        foreach (KeyValuePair<SchemaSetting, Control> pair in settingInputs)
        {
            XmlElement element = SettingsXml.Find(cfg, pair.Key.Key);
            object value = SettingsXml.Value(element);
            pair.Value.Enabled = !readOnly;
            if (pair.Value is CheckBox box)
            {
                box.Checked = value is bool flag ? flag : pair.Key.Default is true;
            }
            else
            {
                object current = element == null ? pair.Key.Default : value;
                foreach (RadioButton radio in pair.Value.Controls.OfType<RadioButton>())
                {
                    radio.Checked = Equals(radio.Tag, current);
                }
            }
        }
    }

    private static string RawText(XmlElement element)
    {
        if (element == null)
        {
            return "";
        }

        return element.GetAttribute("value") is { Length: > 0 } text ? text : element.InnerText;
    }

    private string RowProblem(XmlElement trigger)
    {
        if (trigger == null)
        {
            return null;
        }

        string invalid = schema.Problem(trigger, Everest);
        if (invalid != null)
        {
            return $"This trigger {invalid}, so it behaves like manual.";
        }

        if (KindName(trigger) == "unknown")
        {
            string raw = SettingsXml.Value(SettingsXml.Find(trigger, "raw")) as string;
            return $"This legacy split wasn't recognised, so it never fires: {raw}";
        }

        return null;
    }

    private List<Choice> CheckpointChoices(XmlElement trigger)
    {
        object chapterId = SettingsXml.Value(SettingsXml.Find(trigger, "chapter"));
        object side = SettingsXml.Value(SettingsXml.Find(trigger, "side"));
        SchemaChapter chapter = chapterId is long id ? schema.Chapters.FirstOrDefault(entry => entry.Id == id) : null;
        if (chapter == null)
        {
            return [];
        }

        int sideIndex = side is long sideValue ? (int)sideValue : 0;
        return chapter.Checkpoints.Select((checkpoint, index) => new Choice
        {
            Label = checkpoint.Names.ElementAtOrDefault(sideIndex) ?? $"Checkpoint {index + 1}",
            Value = (long)(index + 1),
        }).ToList();
    }

    private void RenderWarnings()
    {
        var lines = new List<string>();
        if (readOnly)
        {
            lines.Add("These settings were saved by a newer version of the autosplitter, so they are read-only here. Update the component to edit them.");
        }

        foreach (SchemaSetting setting in settingInputs.Keys.Where(setting => setting.Values != null))
        {
            XmlElement element = SettingsXml.Find(cfg, setting.Key);
            if (element != null && !setting.Values.Any(choice => Equals(choice.Value, SettingsXml.Value(element))))
            {
                string fallback = setting.Values.First(choice => Equals(choice.Value, setting.Default)).Label;
                lines.Add($"{setting.Label} \"{RawText(element)}\" isn't valid, so {fallback} is used.");
            }
        }

        if (rows.Any(row => RowProblem(TriggerOf(row)) != null))
        {
            lines.Add("Some triggers below are invalid or never fire. Each one says why.");
        }

        warnings.Text = string.Join("\n", lines);
        warnings.Visible = lines.Count > 0;
    }

    private void RenderRow(TriggerRow row, Control except)
    {
        WithoutEvents(() =>
        {
            XmlElement trigger = TriggerOf(row);
            string kindName = trigger == null ? DefaultKind(row.Setting) : KindName(trigger);
            row.Kind.Enabled = !readOnly;
            if (except != row.Kind)
            {
                RenderKind(row, trigger, kindName);
            }

            SchemaTrigger known = schema.Trigger(kindName);
            List<SchemaField> shown = known == null
                ? []
                : known.Fields.Select(schema.Field).Where(field => !field.Everest || Everest).ToList();
            if (!row.Inputs.Select(input => input.Field).SequenceEqual(shown))
            {
                RebuildInputs(row, shown);
            }

            foreach (FieldInput input in row.Inputs)
            {
                if (input.Input != except)
                {
                    RenderField(input, known, trigger);
                }

                input.Input.Enabled = !readOnly && !IsReplaced(input.Field, known, trigger);
            }

            string message = RowProblem(trigger);
            row.Problem.Text = message ?? "";
            row.Problem.Visible = message != null;
        });
    }

    private void RebuildInputs(TriggerRow row, List<SchemaField> shown)
    {
        foreach (FieldInput old in row.Inputs)
        {
            old.Input.Dispose();
        }

        row.Inputs = shown.Select(field => NewFieldInput(row, field)).ToList();
        row.Fields.Controls.AddRange(row.Inputs.Select(input => input.Input).ToArray());
        row.Fields.Visible = shown.Count > 0;
    }

    private bool IsReplaced(SchemaField field, SchemaTrigger known, XmlElement trigger)
    {
        return schema.Fields.Any(other => other.Replaces == field.Name
            && known != null
            && known.Fields.Contains(other.Name)
            && (!other.Everest || Everest)
            && SettingsXml.Find(trigger, other.Name) != null);
    }

    private void RenderKind(TriggerRow row, XmlElement trigger, string kindName)
    {
        string context = row.Setting.Key;
        var items = schema.Triggers
            .Where(entry => entry.Offer.Contains(context) || entry.Kind == kindName)
            .Select(entry => new Choice { Label = entry.Label, Value = entry.Kind })
            .ToList();
        if (schema.Trigger(kindName) == null)
        {
            string label = kindName != null ? $"Unknown: {kindName}" : "(invalid)";
            items.Add(new Choice { Label = label, Value = kindName ?? Keep });
        }

        row.Kind.Items.Clear();
        row.Kind.Items.AddRange(items.Cast<object>().ToArray());
        row.Kind.SelectedItem = items.FirstOrDefault(item => Equals(item.Value, kindName ?? Keep));
    }

    private void RenderField(FieldInput input, SchemaTrigger known, XmlElement trigger)
    {
        XmlElement element = SettingsXml.Find(trigger, input.Field.Name);
        object value = SettingsXml.Value(element);
        switch (input.Input)
        {
            case Label label:
                label.Text = RawText(element);
                break;
            case TextBox text:
                string wanted = value as string ?? "";
                if (text.Text != wanted)
                {
                    text.Text = wanted;
                }

                break;
            case ComboBox combo:
                RenderChoices(combo, input.Field, known, trigger, element, value);
                break;
        }
    }

    private void RenderChoices(ComboBox combo, SchemaField field, SchemaTrigger known, XmlElement trigger, XmlElement element, object value)
    {
        var items = new List<Choice>();
        if (!known.Required.Contains(field.Name) && field.Default == null)
        {
            items.Add(new Choice { Label = AnyLabel(field), Value = null });
        }

        if (field.Values != null)
        {
            items.AddRange(field.Values.Select(entry => new Choice { Label = entry.Label, Value = entry.Value }));
        }
        else if (field.ValuesFrom == "chapters")
        {
            items.AddRange(schema.Chapters.Select(entry => new Choice { Label = entry.Name, Value = entry.Id }));
        }
        else if (field.ValuesFrom == "checkpoints")
        {
            items.AddRange(CheckpointChoices(trigger));
        }

        object current = element == null ? field.Default : value;
        Choice selected = items.FirstOrDefault(choice => Equals(choice.Value, current));
        if (selected == null && element != null && !(field.ValuesFrom == "chapters" && value is long id && id >= 0))
        {
            selected = new Choice { Label = $"{RawText(element)} (invalid)", Value = Keep };
            items.Add(selected);
        }

        combo.Items.Clear();
        combo.Items.AddRange(items.Cast<object>().ToArray());
        if (selected != null)
        {
            combo.SelectedItem = selected;
        }
        else
        {
            combo.SelectedIndex = -1;
            combo.Text = field.ValuesFrom == "chapters" && value is long ? value.ToString() : "";
        }
    }

    private void ApplyPreset(SchemaPreset entry)
    {
        EnsureCfg();
        foreach (SchemaSetting setting in schema.Settings)
        {
            if (!entry.Settings.TryGetValue(setting.Key, out object value))
            {
                continue;
            }

            if (setting.Type == "trigger")
            {
                ApplyTrigger(rows.First(row => row.Setting == setting), (Dictionary<string, object>)value);
            }
            else
            {
                SettingsXml.Set(cfg, setting.Key, value, cfgOrder);
            }
        }

        foreach (TriggerRow row in rows)
        {
            ApplyTriggerFields(entry, TriggerOf(row));
        }

        Commit();
        WithoutEvents(RenderAll);
    }

    private void ApplyTrigger(TriggerRow row, Dictionary<string, object> expected)
    {
        XmlElement trigger = EnsureTrigger(row);
        string kindName = (string)expected["kind"];
        SchemaTrigger known = schema.Trigger(kindName);
        List<string> order = TriggerOrder(known);
        foreach (SchemaField field in schema.Fields)
        {
            if (!expected.ContainsKey(field.Name))
            {
                SettingsXml.Remove(trigger, field.Name);
            }
        }

        foreach (KeyValuePair<string, object> pair in expected)
        {
            SettingsXml.Set(trigger, pair.Key, pair.Value, order);
        }
    }

    private void ApplyTriggerFields(SchemaPreset entry, XmlElement trigger)
    {
        if (trigger == null || KindName(trigger) is not string kindName || !entry.TriggerFields.TryGetValue(kindName, out Dictionary<string, object> values))
        {
            return;
        }

        List<string> order = TriggerOrder(schema.Trigger(kindName));
        foreach (KeyValuePair<string, object> pair in values)
        {
            SettingsXml.Set(trigger, pair.Key, pair.Value, order);
        }
    }

    private static List<string> TriggerOrder(SchemaTrigger trigger)
    {
        return new[] { "kind" }.Concat(trigger.Fields).ToList();
    }

    private void SettingChanged(SchemaSetting setting, object value)
    {
        EnsureCfg();
        SettingsXml.Set(cfg, setting.Key, value, cfgOrder);
        Commit();
        if (setting == everestSetting)
        {
            WithoutEvents(RenderRows);
        }
    }

    private void FieldChanged(TriggerRow row, SchemaField field, Control source)
    {
        object value;
        if (source is TextBox text)
        {
            value = text.Text.Length == 0 ? null : text.Text;
        }
        else if (source is ComboBox { DropDownStyle: ComboBoxStyle.DropDown } editable)
        {
            if (!TryParseChapter(editable, out value))
            {
                row.Problem.Text = $"\"{editable.Text}\" isn't a chapter.";
                row.Problem.Visible = true;
                return;
            }

            if (value == Keep)
            {
                RenderRow(row, source);
                return;
            }
        }
        else
        {
            var combo = (ComboBox)source;
            if (combo.SelectedItem is not Choice choice || choice.Value == Keep)
            {
                return;
            }

            value = choice.Value;
        }

        XmlElement existing = SettingsXml.Find(TriggerOf(row), field.Name);
        if (Equals(existing == null ? null : SettingsXml.Value(existing), value))
        {
            RenderRow(row, source);
            return;
        }

        XmlElement trigger = EnsureTrigger(row);
        SchemaTrigger known = schema.Trigger(KindName(trigger));
        if (known == null)
        {
            return;
        }

        SettingsXml.Set(trigger, field.Name, value, TriggerOrder(known));
        Commit();
        if (source is ComboBox changed)
        {
            DropStaleChoices(row, changed);
        }

        RenderRow(row, source);
    }

    private void DropStaleChoices(TriggerRow row, ComboBox combo)
    {
        WithoutEvents(() =>
        {
            foreach (Choice stale in combo.Items.Cast<Choice>().Where(IsStale).ToList())
            {
                combo.Items.Remove(stale);
            }
        });

        bool IsStale(Choice choice) => choice.Value == Keep || (combo == row.Kind && schema.Trigger((string)choice.Value) == null);
    }

    private bool TryParseChapter(ComboBox combo, out object value)
    {
        string text = combo.Text.Trim();
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        Choice match = combo.Items.Cast<Choice>().FirstOrDefault(choice => string.Equals(choice.Label, text, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            value = match.Value;
            return true;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long chapter))
        {
            value = chapter;
            return true;
        }

        return false;
    }

    private void KindChanged(TriggerRow row)
    {
        if (row.Kind.SelectedItem is not Choice choice || choice.Value == Keep)
        {
            return;
        }

        var newKind = (string)choice.Value;
        XmlElement before = TriggerOf(row);
        if ((before == null ? DefaultKind(row.Setting) : KindName(before)) == newKind)
        {
            return;
        }

        XmlElement trigger = EnsureTrigger(row);
        SchemaTrigger known = schema.Trigger(newKind);
        foreach (SchemaField field in schema.Fields)
        {
            if (!known.Fields.Contains(field.Name))
            {
                SettingsXml.Remove(trigger, field.Name);
            }
        }

        SettingsXml.Set(trigger, "kind", newKind, TriggerOrder(known));
        Commit();
        DropStaleChoices(row, row.Kind);
        RenderRow(row, row.Kind);
    }

    private void EnsureCfg()
    {
        cfg ??= SettingsXml.SetContainer(map, "cfg", "map", TopLevelOrder);
    }

    private XmlElement EnsureTrigger(TriggerRow row)
    {
        EnsureCfg();
        XmlElement trigger = TriggerOf(row);
        if (trigger == null)
        {
            if (row.IsSplit)
            {
                XmlElement list = EnsureSplits(row.Index + 1);
                trigger = SettingsXml.Items(list).ElementAt(row.Index);
            }
            else
            {
                trigger = SettingsXml.SetContainer(cfg, row.Setting.Key, "map", cfgOrder);
            }
        }
        else if (SettingsXml.Type(trigger) != "map")
        {
            SettingsXml.MakeContainer(trigger, "map");
        }

        if (KindName(trigger) == null)
        {
            SettingsXml.Set(trigger, "kind", row.IsSplit ? "manual" : DefaultKind(row.Setting), ["kind"]);
        }

        return trigger;
    }

    private XmlElement EnsureSplits(int count)
    {
        EnsureCfg();
        XmlElement list = SettingsXml.SetContainer(cfg, splitsSetting.Key, "list", cfgOrder);
        while (SettingsXml.Items(list).Count() < count)
        {
            XmlElement item = SettingsXml.NewItem(list.OwnerDocument);
            SettingsXml.Set(item, "kind", "manual", ["kind"]);
            list.AppendChild(item);
        }

        return list;
    }

    private void Commit()
    {
        if (readOnly)
        {
            return;
        }

        if (SettingsXml.Find(map, "schema_version") == null)
        {
            SettingsXml.Set(map, "schema_version", schema.Version, TopLevelOrder);
        }

        write(settings);
        cfg = SettingsXml.FindMap(map, "cfg");
        WithoutEvents(RenderWarnings);
    }
}
