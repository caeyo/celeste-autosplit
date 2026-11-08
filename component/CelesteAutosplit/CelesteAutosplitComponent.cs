using LiveSplit.AutoSplittingRuntime;
using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Xml;

namespace CelesteAutosplit;

public class CelesteAutosplitComponent : LogicComponent
{
    public const string WasmFileName = "celeste_autosplit.wasm";
    private const string LegacyFileName = "LiveSplit.Celeste.dll";

    private readonly LogicComponent inner;
    private readonly string status;
    private readonly LiveSplitState state;
    private readonly SettingsSchema schema;
    private readonly GamePriority priority = new();
    private Control settingsControl;
    private SettingsEditor editor;

    private XmlNode heldSettings;
    private bool gameTimeSettled;
    private bool legacyWarned;
    private int lastUpdateCheck = Environment.TickCount;

    public override string ComponentName => CelesteAutosplitFactory.Name;

    public CelesteAutosplitComponent(LiveSplitState state)
    {
        this.state = state;

        if (!Environment.Is64BitProcess)
        {
            status = "This autosplitter requires 64-bit Windows. If you need a 32-bit version, download the legacy LiveSplit.Celeste component and add it to your layout.";
            Log.Error($"[{ComponentName}] LiveSplit is running as a 32-bit process, where ASR can't run wasm modules");
            return;
        }

        string wasmPath = Path.GetFullPath(Path.Combine(ComponentManager.BasePath ?? "", ComponentManager.PATH_COMPONENTS, WasmFileName));

        if (!File.Exists(wasmPath))
        {
            status = $"The wasm module is missing. Expected it at:\n{wasmPath}\n\nTo repair it, close LiveSplit, delete celeste-autosplit.dll from its Components folder and start LiveSplit again, which downloads both files. Or download {WasmFileName} from https://github.com/caeyo/celeste-autosplit/releases/latest into that folder.";
            Log.Error($"[{ComponentName}] Wasm module not found: {wasmPath}");
            return;
        }

        try
        {
            inner = CreateInner(state, wasmPath);
        }
        catch (Exception ex)
        {
            status = $"Could not create the Auto Splitting Runtime component. This version of LiveSplit may be incompatible.\n\n{ex.GetType().Name}: {ex.Message}";
            Log.Error($"[{ComponentName}] Could not create the ASR component");
            Log.Error(ex);
            return;
        }

        bool? hasRuntime;
        try
        {
            hasRuntime = HasRuntime(inner);
        }
        catch (Exception ex)
        {
            hasRuntime = null;
            Log.Warning($"[{ComponentName}] Could not check whether ASR loaded the wasm module: {ex.GetType().Name}: {ex.Message}");
        }

        switch (hasRuntime)
        {
            case true:
                status = $"Running {wasmPath}";
                Log.Info($"[{ComponentName}] Running {wasmPath}");
                break;
            case false:
                inner.Dispose();
                inner = null;
                status = $"The Auto Splitting Runtime failed to load the wasm module:\n{wasmPath}\n\nThe cause is in LiveSplit's debug output (Sysinternals DebugView).";
                Log.Error($"[{ComponentName}] ASR failed to load {wasmPath}");
                break;
            default:
                status = $"Loaded {wasmPath}, but could not confirm that the Auto Splitting Runtime is running it.";
                break;
        }

        if (inner == null)
        {
            return;
        }

        try
        {
            schema = SettingsSchema.Load();
            ApplyHostSettings(false);
        }
        catch (Exception ex)
        {
            status += $"\n\nThe settings editor is unavailable: {ex.GetType().Name}: {ex.Message}";
            Log.Error($"[{ComponentName}] Could not load the settings schema");
            Log.Error(ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LogicComponent CreateInner(LiveSplitState state, string wasmPath)
    {
        return new ASRComponent(state, wasmPath);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasRuntime(LogicComponent inner)
    {
        return ((ComponentSettings)inner.GetSettingsControl(LayoutMode.Vertical)).runtime != null;
    }

    public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        inner?.Update(invalidator, state, width, height, mode);
        if (inner == null)
        {
            return;
        }

        priority.Update();

        int now = Environment.TickCount;
        if (unchecked(now - lastUpdateCheck) < 2000)
        {
            return;
        }

        lastUpdateCheck = now;
        CheckForLegacyComponent();
    }

    private void CheckForLegacyComponent()
    {
        if (legacyWarned || state.CurrentPhase != TimerPhase.NotRunning || FindLegacyComponent() == null)
        {
            return;
        }

        legacyWarned = true;
        state.Form.BeginInvoke(new Action(() =>
        {
            ILayoutComponent legacy = FindLegacyComponent();
            if (legacy != null)
            {
                WarnAboutLegacyComponent(legacy);
            }
        }));
    }

    private ILayoutComponent FindLegacyComponent()
    {
        return state.Layout?.LayoutComponents.FirstOrDefault(component =>
            string.Equals(Path.GetFileName(component.Path ?? ""), LegacyFileName, StringComparison.OrdinalIgnoreCase));
    }

    private string LegacyWarning()
    {
        return FindLegacyComponent() != null
            ? $"The legacy {LegacyFileName} component is in your layout. Remove it in the layout editor, or both will split."
            : null;
    }

    private void WarnAboutLegacyComponent(ILayoutComponent legacy)
    {
        try
        {
            bool offerImport = HasNoSettings();
            string message = LegacyWarning();
            if (offerImport)
            {
                message += "\n\nThese splits have no settings for this autosplitter yet. Import the legacy component's settings from the layout?";
            }

            DialogResult result = MessageBox.Show(
                state.Form,
                message,
                ComponentName,
                offerImport ? MessageBoxButtons.YesNo : MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            if (offerImport && result == DialogResult.Yes)
            {
                ImportLegacySettings(legacy.Component);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }

    private bool HasNoSettings()
    {
        return ReadSettings()["CustomSettings"]?.HasChildNodes != true;
    }

    internal void ImportLegacySettings(IComponent legacy)
    {
        inner.SetSettings(LegacyMigration.Migrate(legacy.GetSettings(new XmlDocument())));
        ApplyHostSettings(false);

        if (state.Run != null)
        {
            var document = new XmlDocument();
            XmlElement autoSplitterSettings = document.CreateElement("AutoSplitterSettings");
            autoSplitterSettings.InnerXml = inner.GetSettings(document).InnerXml;
            XmlAttribute gameName = document.CreateAttribute("gameName");
            gameName.Value = state.Run.GameName;
            autoSplitterSettings.Attributes.Append(gameName);
            state.Run.AutoSplitterSettings = autoSplitterSettings;
            state.Run.HasChanged = true;
        }

        editor?.Reload();
    }

    private XmlElement ReadSettings()
    {
        return (XmlElement)inner.GetSettings(new XmlDocument());
    }

    private void WriteSettings(XmlElement settings)
    {
        inner.SetSettings(settings);
        ApplyHostSettings(false);
    }

    private void ApplyHostSettings(bool loading)
    {
        if (schema == null)
        {
            return;
        }

        try
        {
            XmlElement map = ReadSettings()["CustomSettings"];
            priority.Enabled = schema.Flag(map, "high_priority");
            if (loading && !gameTimeSettled)
            {
                gameTimeSettled = true;
                if (schema.Flag(map, "set_game_time"))
                {
                    state.CurrentTimingMethod = TimingMethod.GameTime;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{ComponentName}] Could not read the settings");
            Log.Error(ex);
        }
    }

    public override XmlNode GetSettings(XmlDocument document)
    {
        if (inner != null)
        {
            return inner.GetSettings(document);
        }

        return heldSettings != null
            ? document.ImportNode(heldSettings, true)
            : document.CreateElement("Settings");
    }

    public override void SetSettings(XmlNode settings)
    {
        if (inner != null)
        {
            inner.SetSettings(LegacyMigration.Migrate(settings));
            ApplyHostSettings(true);
        }
        else
        {
            heldSettings = settings?.CloneNode(true);
        }
    }

    public override Control GetSettingsControl(LayoutMode mode)
    {
        gameTimeSettled = true;
        if (inner != null && schema != null)
        {
            try
            {
                if (editor == null || editor.IsDisposed)
                {
                    editor = new SettingsEditor(state, schema, status, ReadSettings, WriteSettings, LegacyWarning);
                }

                editor.Reload();
                return editor;
            }
            catch (Exception ex)
            {
                Log.Error($"[{ComponentName}] Could not open the settings editor");
                Log.Error(ex);
                editor = null;
                return StatusLabel($"{status}\n\nThe settings editor is unavailable: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return StatusLabel(status);
    }

    private Control StatusLabel(string text)
    {
        if (settingsControl == null || settingsControl.IsDisposed)
        {
            settingsControl = new Label
            {
                AutoSize = true,
                MaximumSize = new System.Drawing.Size(460, 0),
                Padding = new Padding(7),
            };
        }

        settingsControl.Text = text;
        return settingsControl;
    }

    public override void Dispose()
    {
        priority.Dispose();
        inner?.Dispose();
    }
}
