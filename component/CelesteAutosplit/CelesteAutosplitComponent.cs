using LiveSplit.AutoSplittingRuntime;
using LiveSplit.Model;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Xml;

namespace CelesteAutosplit;

// Runs celeste_autosplit.wasm through LiveSplit's ASR component, which does all
// the auto splitting.
public class CelesteAutosplitComponent : LogicComponent
{
    public const string WasmFileName = "celeste_autosplit.wasm";

    // Typed as LogicComponent so that only CreateInner and HasRuntime touch ASR
    // types. Null when ASR can't run in this process, the wasm is missing, or ASR
    // failed to load it.
    private readonly LogicComponent inner;
    private readonly string status;
    private Control settingsControl;

    // Without an inner component, the last settings LiveSplit gave us, handed
    // back unchanged so that saving the splits doesn't wipe them.
    private XmlNode heldSettings;

    public override string ComponentName => CelesteAutosplitFactory.Name;

    public CelesteAutosplitComponent(LiveSplitState state)
    {
        // ASR's 32-bit native library is a stub: it hands out a runtime that never
        // runs the module and drops all settings, so ASR can't be used at all.
        if (!Environment.Is64BitProcess)
        {
            status = "This autosplitter requires 64-bit Windows. If you need a 32-bit version, download the legacy LiveSplit.Celeste component and add it to your layout.";
            Log.Error($"[{ComponentName}] LiveSplit is running as a 32-bit process, where ASR can't run wasm modules");
            return;
        }

        string wasmPath = Path.GetFullPath(Path.Combine(ComponentManager.BasePath ?? "", ComponentManager.PATH_COMPONENTS, WasmFileName));

        if (!File.Exists(wasmPath))
        {
            status = $"The wasm module is missing. Expected it at:\n{wasmPath}";
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
                // Without a runtime ASR keeps no settings, so hold them ourselves.
                inner.Dispose();
                inner = null;
                status = $"The Auto Splitting Runtime failed to load the wasm module:\n{wasmPath}\n\nThe cause is in LiveSplit's debug output (Sysinternals DebugView).";
                Log.Error($"[{ComponentName}] ASR failed to load {wasmPath}");
                break;
            default:
                status = $"Loaded {wasmPath}, but could not confirm that the Auto Splitting Runtime is running it.";
                break;
        }
    }

    // The same constructor ASRFactory.Create(state, script) uses. Kept out of line
    // so that a missing or changed ASR assembly throws inside the constructor's
    // try block rather than when the constructor itself is compiled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LogicComponent CreateInner(LiveSplitState state, string wasmPath)
    {
        return new ASRComponent(state, wasmPath);
    }

    // ASRComponent's constructor logs and swallows runtime load failures, leaving
    // ComponentSettings.runtime null. This is the only way to detect them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasRuntime(LogicComponent inner)
    {
        return ((ComponentSettings)inner.GetSettingsControl(LayoutMode.Vertical)).runtime != null;
    }

    public override void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        inner?.Update(invalidator, state, width, height, mode);
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
            inner.SetSettings(settings);
        }
        else
        {
            heldSettings = settings?.CloneNode(true);
        }
    }

    // Placeholder until the settings editor exists. Must not be null, or
    // LiveSplit disables the Settings button.
    public override Control GetSettingsControl(LayoutMode mode)
    {
        return settingsControl ??= new Label
        {
            Text = status,
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            Padding = new Padding(7),
        };
    }

    public override void Dispose()
    {
        inner?.Dispose();
    }
}
