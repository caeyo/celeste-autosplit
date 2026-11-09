using LiveSplit.Model;
using LiveSplit.Options;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace CelesteAutosplit;

internal sealed class SettingsEditor : UserControl
{
    private const string Flag = "[!] ";
    private const string CustomPreset = "Custom";
    private const int CaptionWidth = 104;
    private const int InputWidth = 340;
    private const int FullWidth = CaptionWidth + InputWidth;

    private static readonly object Keep = new();
    private static readonly string[] TopLevelOrder = ["schema_version", "cfg"];

    private sealed class Choice
    {
        public string Label;
        public object Value;

        public override string ToString() => Label;
    }

    private sealed class FieldInput
    {
        public SchemaField Field;
        public Label Caption;
        public Control Input;
    }

    private sealed class Row
    {
        public string Name;
        public SchemaSetting Setting;
        public int Index;

        public bool IsSplit => Setting.Type == "list";
    }

    private readonly LiveSplitState state;
    private readonly SettingsSchema schema;
    private readonly Func<XmlElement> read;
    private readonly Action<XmlElement> write;
    private readonly Func<string> legacyWarning;
    private readonly List<string> cfgOrder;
    private readonly SchemaSetting splitsSetting;
    private readonly SchemaSetting everestSetting;

    private readonly TableLayoutPanel table;
    private readonly ToolTip tips = new();
    private readonly Label status;
    private readonly Label warnings;
    private readonly ComboBox preset;
    private readonly ListBox triggers;
    private readonly Button insertButton;
    private readonly Button removeButton;
    private readonly Button upButton;
    private readonly Button downButton;
    private readonly ComboBox kind;
    private readonly Label problem;
    private readonly List<FieldInput> fieldInputs = [];
    private readonly Dictionary<SchemaSetting, Control> settingInputs = [];
    private readonly List<Row> rows = [];

    private XmlElement settings;
    private XmlElement map;
    private XmlElement cfg;
    private bool readOnly;
    private bool loading;
    private int rowIndex;

    public SettingsEditor(LiveSplitState state, SettingsSchema schema, string statusText, Func<XmlElement> read, Action<XmlElement> write, Func<string> legacyWarning)
    {
        this.state = state;
        this.schema = schema;
        this.read = read;
        this.write = write;
        this.legacyWarning = legacyWarning;
        cfgOrder = schema.Settings.Select(setting => setting.Key).ToList();
        splitsSetting = schema.Settings.First(setting => setting.Type == "list");
        everestSetting = schema.Setting("everest");

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Name = "settingsEditor";

        table = new TableLayoutPanel
        {
            ColumnCount = 2,
            Location = new Point(0, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(3),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CaptionWidth));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, InputWidth));
        Controls.Add(table);

        status = NewWrappedLabel("status", SystemColors.ControlText);
        status.Text = statusText;
        AddSpanning(status);

        warnings = NewWrappedLabel("warnings", Color.DarkRed);
        warnings.Visible = false;
        AddSpanning(warnings);

        preset = NewCombo("preset", ComboBoxStyle.DropDownList);
        preset.SelectedIndexChanged += (_, _) => Guard(PresetChanged);
        AddRow(NewCaption("Preset", null), preset);

        foreach (SchemaSetting setting in schema.Settings)
        {
            AddSettingInput(setting);
        }

        triggers = new ListBox
        {
            Name = "triggers",
            Width = FullWidth,
            Height = 128,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        triggers.SelectedIndexChanged += (_, _) => Guard(RowSelected);
        AddSpanning(triggers);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        insertButton = NewButton("insert", "Insert", InsertSplit, buttons);
        removeButton = NewButton("remove", "Remove", RemoveSplit, buttons);
        upButton = NewButton("up", "Move up", () => MoveSplit(-1), buttons);
        downButton = NewButton("down", "Move down", () => MoveSplit(1), buttons);
        AddSpanning(buttons);

        kind = NewCombo("kind", ComboBoxStyle.DropDownList);
        kind.SelectedIndexChanged += (_, _) => Guard(KindChanged);
        AddRow(NewCaption("Kind", null), kind);

        foreach (SchemaField field in schema.Fields)
        {
            AddFieldInput(field);
        }

        problem = NewWrappedLabel("problem", Color.DarkRed);
        problem.Visible = false;
        AddSpanning(problem);
    }

    private static Label NewWrappedLabel(string name, Color color)
    {
        return new Label
        {
            Name = name,
            ForeColor = color,
            AutoSize = true,
            MaximumSize = new Size(FullWidth, 0),
        };
    }

    private Label NewCaption(string text, string description)
    {
        var caption = new Label
        {
            Text = text,
            AutoSize = false,
            Size = new Size(CaptionWidth - 6, 21),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        if (description != null)
        {
            tips.SetToolTip(caption, description);
        }

        return caption;
    }

    private ComboBox NewCombo(string name, ComboBoxStyle style)
    {
        return new ComboBox
        {
            Name = name,
            DropDownStyle = style,
            Width = InputWidth - 6,
        };
    }

    private Button NewButton(string name, string text, Action action, Control parent)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            AutoSize = true,
        };
        button.Click += (_, _) => Guard(action);
        parent.Controls.Add(button);
        return button;
    }

    private void AddRow(Control caption, Control input)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(caption, 0, row);
        table.Controls.Add(input, 1, row);
    }

    private void AddSpanning(Control control)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, row);
        table.SetColumnSpan(control, 2);
    }

    private void AddSettingInput(SchemaSetting setting)
    {
        if (setting.Type == "bool")
        {
            var box = new CheckBox
            {
                Name = "setting." + setting.Key,
                Text = setting.Label,
                AutoSize = true,
            };
            if (setting.Description != null)
            {
                tips.SetToolTip(box, setting.Description);
            }

            box.CheckedChanged += (_, _) => Guard(() => SettingChanged(setting, box.Checked));
            settingInputs[setting] = box;
            AddSpanning(box);
        }
        else if (setting.Type == "string" && setting.Values != null)
        {
            ComboBox combo = NewCombo("setting." + setting.Key, ComboBoxStyle.DropDownList);
            combo.SelectedIndexChanged += (_, _) => Guard(() =>
            {
                if (combo.SelectedItem is Choice { Value: not null } choice && choice.Value != Keep)
                {
                    SettingChanged(setting, choice.Value);
                }
            });
            settingInputs[setting] = combo;
            AddRow(NewCaption(setting.Label, setting.Description), combo);
        }
    }

    private void AddFieldInput(SchemaField field)
    {
        Control input;
        if (field.ReadOnly)
        {
            input = new Label
            {
                Name = "field." + field.Name,
                AutoSize = true,
                MaximumSize = new Size(InputWidth - 6, 0),
            };
        }
        else if (field.ValuesFrom == "chapters")
        {
            ComboBox combo = NewCombo("field." + field.Name, ComboBoxStyle.DropDown);
            combo.SelectedIndexChanged += (_, _) => Guard(() => FieldChanged(field, combo));
            combo.TextChanged += (_, _) => Guard(() => FieldChanged(field, combo));
            input = combo;
        }
        else if (field.Values != null || field.ValuesFrom != null)
        {
            ComboBox combo = NewCombo("field." + field.Name, ComboBoxStyle.DropDownList);
            combo.SelectedIndexChanged += (_, _) => Guard(() => FieldChanged(field, combo));
            input = combo;
        }
        else
        {
            var text = new TextBox
            {
                Name = "field." + field.Name,
                Width = InputWidth - 6,
            };
            text.TextChanged += (_, _) => Guard(() => FieldChanged(field, text));
            input = text;
        }

        var row = new FieldInput
        {
            Field = field,
            Caption = NewCaption(field.Label, field.Description),
            Input = input,
        };
        if (field.Description != null)
        {
            tips.SetToolTip(input, field.Description);
        }

        fieldInputs.Add(row);
        AddRow(row.Caption, row.Input);
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

        rowIndex = Math.Max(0, Math.Min(rowIndex, RowCount() - 1));
        Render();
    }

    private int RowCount() => schema.Settings.Count(setting => setting.Type == "trigger") + SplitRowCount();

    private int SplitRowCount()
    {
        return Math.Max(SettingsXml.Items(SplitsList()).Count(), state.Run?.Count ?? 0);
    }

    private XmlElement SplitsList()
    {
        XmlElement list = SettingsXml.Find(cfg, splitsSetting.Key);
        return SettingsXml.Type(list) == "list" ? list : null;
    }

    private bool Everest => SettingsXml.Value(SettingsXml.Find(cfg, everestSetting.Key)) is bool value
        ? value
        : everestSetting.Default is true;

    private void BuildRows()
    {
        rows.Clear();
        foreach (SchemaSetting setting in schema.Settings.Where(setting => setting.Type == "trigger"))
        {
            rows.Add(new Row { Name = setting.Label, Setting = setting });
        }

        int segments = state.Run?.Count ?? 0;
        int count = SplitRowCount();
        for (int i = 0; i < count; i++)
        {
            string name = i < segments ? state.Run[i].Name : "(no segment)";
            rows.Add(new Row { Name = $"{i + 1}. {name}", Setting = splitsSetting, Index = i });
        }
    }

    private Row SelectedRow => rowIndex >= 0 && rowIndex < rows.Count ? rows[rowIndex] : null;

    private XmlElement TriggerOf(Row row)
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

    private void Render()
    {
        WithoutEvents(() =>
        {
            BuildRows();
            RenderPresetItems();
            RenderSettings();
            RenderRows();
            SyncInputsEnabled();
            RenderDetail(null);
            RenderWarnings();
        });
    }

    private void RenderPresetItems()
    {
        if (preset.Items.Count == 0)
        {
            foreach (SchemaPreset entry in schema.Presets)
            {
                preset.Items.Add(entry.Label);
            }

            preset.Items.Add(CustomPreset);
        }

        RenderPresetSelection();
    }

    private void RenderSettings()
    {
        foreach (KeyValuePair<SchemaSetting, Control> pair in settingInputs)
        {
            XmlElement element = SettingsXml.Find(cfg, pair.Key.Key);
            object value = SettingsXml.Value(element);
            if (pair.Value is CheckBox box)
            {
                box.Checked = value is bool flag ? flag : pair.Key.Default is true;
            }
            else if (pair.Value is ComboBox combo)
            {
                var items = pair.Key.Values.Select(choice => new Choice { Label = choice.Label, Value = choice.Value }).ToList();
                combo.Items.Clear();
                object current = element == null ? pair.Key.Default : value;
                Choice selected = items.FirstOrDefault(choice => Equals(choice.Value, current));
                if (selected == null)
                {
                    selected = new Choice { Label = $"{RawText(element)} (invalid)", Value = Keep };
                    items.Add(selected);
                }

                combo.Items.AddRange(items.Cast<object>().ToArray());
                combo.SelectedItem = selected;
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

    private void RenderRows()
    {
        triggers.BeginUpdate();
        int top = triggers.TopIndex;
        triggers.Items.Clear();
        foreach (Row row in rows)
        {
            triggers.Items.Add(RowText(row));
        }

        if (rowIndex < rows.Count)
        {
            triggers.SelectedIndex = rowIndex;
        }

        triggers.TopIndex = Math.Min(top, triggers.Items.Count - 1 < 0 ? 0 : triggers.Items.Count - 1);
        triggers.EndUpdate();
    }

    private string RowText(Row row)
    {
        XmlElement trigger = TriggerOf(row);
        string flag = RowProblem(trigger) != null ? Flag : "";
        return $"{flag}{row.Name}: {Summary(row, trigger)}";
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

    private string Summary(Row row, XmlElement trigger)
    {
        string kindName = trigger == null ? DefaultKind(row.Setting) : KindName(trigger);
        SchemaTrigger known = schema.Trigger(kindName);
        if (known == null)
        {
            return kindName ?? "(invalid)";
        }

        var parts = new List<string>();
        foreach (string name in known.Fields)
        {
            SchemaField field = schema.Field(name);
            if (field.Everest && !Everest)
            {
                continue;
            }

            XmlElement element = SettingsXml.Find(trigger, name);
            if (element == null)
            {
                continue;
            }

            parts.Add(FieldSummary(field, trigger, element));
        }

        return parts.Count > 0 ? $"{known.Label} ({string.Join(", ", parts)})" : known.Label;
    }

    private string FieldSummary(SchemaField field, XmlElement trigger, XmlElement element)
    {
        object value = SettingsXml.Value(element);
        if (field.Values != null)
        {
            SchemaChoice choice = field.Values.FirstOrDefault(entry => Equals(entry.Value, value));
            return choice != null ? choice.Label : RawText(element);
        }

        if (field.ValuesFrom == "chapters")
        {
            SchemaChapter chapter = value is long id ? schema.Chapters.FirstOrDefault(entry => entry.Id == id) : null;
            return chapter != null ? chapter.Name : RawText(element);
        }

        if (field.ValuesFrom == "checkpoints")
        {
            string name = CheckpointChoices(trigger).FirstOrDefault(choice => Equals(choice.Value, value))?.Label;
            return name ?? RawText(element);
        }

        return RawText(element);
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
        string legacy = legacyWarning();
        if (legacy != null)
        {
            lines.Add(legacy);
        }

        if (readOnly)
        {
            lines.Add("These settings were saved by a newer version of the autosplitter, so they are read-only here. Update the component to edit them.");
        }

        if (rows.Any(row => RowProblem(TriggerOf(row)) != null))
        {
            lines.Add("Triggers marked [!] are invalid or never fire.");
        }

        warnings.Text = string.Join("\n", lines);
        warnings.Visible = lines.Count > 0;
    }

    private void SyncInputsEnabled()
    {
        preset.Enabled = !readOnly;
        foreach (Control input in settingInputs.Values)
        {
            input.Enabled = !readOnly;
        }

        kind.Enabled = !readOnly;
        insertButton.Enabled = !readOnly && SelectedRow?.IsSplit == true;
        removeButton.Enabled = insertButton.Enabled;
        upButton.Enabled = insertButton.Enabled;
        downButton.Enabled = insertButton.Enabled;
    }

    private void RowSelected()
    {
        rowIndex = triggers.SelectedIndex;
        WithoutEvents(() =>
        {
            SyncInputsEnabled();
            RenderDetail(null);
        });
    }

    private void RenderDetail(Control except)
    {
        WithoutEvents(() =>
        {
            Row row = SelectedRow;
            if (row == null)
            {
                return;
            }

            XmlElement trigger = TriggerOf(row);
            string kindName = trigger == null ? DefaultKind(row.Setting) : KindName(trigger);
            if (except != kind)
            {
                RenderKind(row, trigger, kindName);
            }

            SchemaTrigger known = schema.Trigger(kindName);
            foreach (FieldInput input in fieldInputs)
            {
                bool taken = known != null && known.Fields.Contains(input.Field.Name);
                bool visible = taken && (!input.Field.Everest || Everest);
                input.Caption.Visible = visible;
                input.Input.Visible = visible;
                if (visible && input.Input != except)
                {
                    RenderField(input, known, trigger);
                }

                input.Input.Enabled = !readOnly && !IsReplaced(input.Field, known, trigger);
            }

            string message = RowProblem(trigger);
            problem.Text = message ?? "";
            problem.Visible = message != null;
        });
    }

    private bool IsReplaced(SchemaField field, SchemaTrigger known, XmlElement trigger)
    {
        return schema.Fields.Any(other => other.Replaces == field.Name
            && known != null
            && known.Fields.Contains(other.Name)
            && (!other.Everest || Everest)
            && SettingsXml.Find(trigger, other.Name) != null);
    }

    private void RenderKind(Row row, XmlElement trigger, string kindName)
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

        kind.Items.Clear();
        kind.Items.AddRange(items.Cast<object>().ToArray());
        kind.SelectedItem = items.FirstOrDefault(item => Equals(item.Value, kindName ?? Keep));
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
            items.Add(new Choice { Label = "(any)", Value = null });
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

    private void PresetChanged()
    {
        int index = preset.SelectedIndex;
        if (index < 0 || index >= schema.Presets.Count)
        {
            RenderPresetSelection();
            return;
        }

        SchemaPreset entry = schema.Presets[index];
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

        foreach (Row row in rows)
        {
            ApplyTriggerFields(entry, TriggerOf(row));
        }

        Commit();
        WithoutEvents(() =>
        {
            RenderSettings();
            RenderDetail(null);
        });
    }

    private void ApplyTrigger(Row row, Dictionary<string, object> expected)
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

    private bool Matches(SchemaPreset entry)
    {
        foreach (KeyValuePair<string, object> pair in entry.Settings)
        {
            SchemaSetting setting = schema.Setting(pair.Key);
            XmlElement element = SettingsXml.Find(cfg, pair.Key);
            if (setting.Type == "trigger")
            {
                if (!TriggerMatches(setting, element, (Dictionary<string, object>)pair.Value))
                {
                    return false;
                }
            }
            else if (!Equals(element == null ? setting.Default : SettingsXml.Value(element), pair.Value))
            {
                return false;
            }
        }

        foreach (KeyValuePair<string, Dictionary<string, object>> kindFields in entry.TriggerFields)
        {
            foreach (Row row in rows)
            {
                XmlElement trigger = TriggerOf(row);
                if (KindName(trigger) != kindFields.Key || schema.Problem(trigger, Everest) != null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> field in kindFields.Value)
                {
                    XmlElement element = SettingsXml.Find(trigger, field.Key);
                    object value = element == null ? schema.Field(field.Key).Default : SettingsXml.Value(element);
                    if (!Equals(value, field.Value))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private bool TriggerMatches(SchemaSetting setting, XmlElement element, Dictionary<string, object> expected)
    {
        string kindName = element == null ? DefaultKind(setting) : KindName(element);
        if (kindName != (string)expected["kind"])
        {
            return false;
        }

        SchemaTrigger known = schema.Trigger(kindName);
        if (known == null)
        {
            return false;
        }

        foreach (string fieldName in known.Fields)
        {
            SchemaField field = schema.Field(fieldName);
            XmlElement fieldElement = SettingsXml.Find(element, fieldName);
            object actual = fieldElement == null ? field.Default : SettingsXml.Value(fieldElement);
            object wanted = expected.TryGetValue(fieldName, out object value) ? value : field.Default;
            if (!Equals(actual, wanted))
            {
                return false;
            }
        }

        return true;
    }

    private void RenderPresetSelection()
    {
        WithoutEvents(() =>
        {
            int match = schema.Presets.FindIndex(Matches);
            preset.SelectedIndex = match >= 0 ? match : schema.Presets.Count;
        });
    }

    private void SettingChanged(SchemaSetting setting, object value)
    {
        EnsureCfg();
        SettingsXml.Set(cfg, setting.Key, value, cfgOrder);
        Commit();
        if (setting == everestSetting)
        {
            RenderDetail(null);
        }
    }

    private void FieldChanged(SchemaField field, Control source)
    {
        Row row = SelectedRow;
        if (row == null)
        {
            return;
        }

        object value;
        if (source is TextBox text)
        {
            value = text.Text.Length == 0 ? null : text.Text;
        }
        else if (source is ComboBox { DropDownStyle: ComboBoxStyle.DropDown } editable)
        {
            if (!TryParseChapter(editable, out value))
            {
                problem.Text = $"\"{editable.Text}\" isn't a chapter.";
                problem.Visible = true;
                return;
            }

            if (value == Keep)
            {
                RenderDetail(source);
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
            RenderDetail(source);
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
            DropStaleChoices(changed);
        }

        RenderDetail(source);
    }

    private void DropStaleChoices(ComboBox combo)
    {
        WithoutEvents(() =>
        {
            foreach (Choice stale in combo.Items.Cast<Choice>().Where(IsStale).ToList())
            {
                combo.Items.Remove(stale);
            }
        });

        bool IsStale(Choice choice) => choice.Value == Keep || (combo == kind && schema.Trigger((string)choice.Value) == null);
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

    private void KindChanged()
    {
        Row row = SelectedRow;
        if (row == null || kind.SelectedItem is not Choice choice || choice.Value == Keep)
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
        DropStaleChoices(kind);
        RenderDetail(kind);
    }

    private void EnsureCfg()
    {
        cfg ??= SettingsXml.SetContainer(map, "cfg", "map", TopLevelOrder);
    }

    private XmlElement EnsureTrigger(Row row)
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
            list.AppendChild(NewManual(list.OwnerDocument));
        }

        return list;
    }

    private XmlElement NewManual(XmlDocument document)
    {
        XmlElement item = SettingsXml.NewItem(document);
        SettingsXml.Set(item, "kind", "manual", ["kind"]);
        return item;
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
        RefreshAfterChange();
    }

    private void RefreshAfterChange()
    {
        WithoutEvents(() =>
        {
            BuildRows();
            RenderRows();
            RenderPresetSelection();
            RenderWarnings();
        });
    }

    private void InsertSplit()
    {
        Row row = SelectedRow;
        if (row == null || !row.IsSplit)
        {
            return;
        }

        XmlElement list = EnsureSplits(row.Index);
        XmlElement item = NewManual(list.OwnerDocument);
        list.InsertBefore(item, SettingsXml.Items(list).ElementAtOrDefault(row.Index));
        Commit();
        ReselectDetail();
    }

    private void RemoveSplit()
    {
        Row row = SelectedRow;
        if (row == null || !row.IsSplit)
        {
            return;
        }

        XmlElement list = SplitsList();
        XmlElement item = SettingsXml.Items(list).ElementAtOrDefault(row.Index);
        if (item != null)
        {
            list.RemoveChild(item);
            Commit();
            ReselectDetail();
        }
    }

    private void MoveSplit(int delta)
    {
        Row row = SelectedRow;
        if (row == null || !row.IsSplit)
        {
            return;
        }

        int other = row.Index + delta;
        if (other < 0 || other >= SplitRowCount())
        {
            return;
        }

        XmlElement list = EnsureSplits(Math.Max(row.Index, other) + 1);
        XmlElement current = SettingsXml.Items(list).ElementAt(row.Index);
        XmlElement neighbour = SettingsXml.Items(list).ElementAt(other);
        if (delta < 0)
        {
            list.RemoveChild(current);
            list.InsertBefore(current, neighbour);
        }
        else
        {
            list.RemoveChild(current);
            list.InsertAfter(current, neighbour);
        }

        rowIndex += delta;
        Commit();
        ReselectDetail();
    }

    private void ReselectDetail()
    {
        WithoutEvents(() =>
        {
            rowIndex = Math.Max(0, Math.Min(rowIndex, rows.Count - 1));
            triggers.SelectedIndex = rowIndex;
            SyncInputsEnabled();
            RenderDetail(null);
        });
    }
}
