using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Xunit;

namespace CelesteAutosplit.Tests;

// Runs the real wasm module inside LiveSplit's ASR component, to catch changes in
// the ASR internals the component relies on when moving to a newer LiveSplit.
[Collection(LiveSplitCollection.Name)]
public sealed class ComponentTests : IDisposable
{
    private const string Settings =
        "<AutoSplitterSettings><Version>1.0</Version><CustomSettings>" +
        "<Setting id=\"schema_version\" type=\"i64\">1</Setting>" +
        "<Setting id=\"cfg\" type=\"map\">" +
        "<Setting id=\"auto_reset\" type=\"bool\">True</Setting>" +
        "<Setting id=\"mode\" type=\"string\" value=\"full_game\" />" +
        "<Setting id=\"note\" type=\"string\" value=\"a &amp; &quot;b&quot; &lt;c&gt; é\" />" +
        "<Setting id=\"offset\" type=\"f64\">-0.25</Setting>" +
        "<Setting id=\"empty_list\" type=\"list\" />" +
        "<Setting id=\"empty_map\" type=\"map\" />" +
        "<Setting id=\"splits\" type=\"list\">" +
        "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"chapter_complete\" /><Setting id=\"chapter\" type=\"i64\">1</Setting></Setting>" +
        "<Setting type=\"map\"><Setting id=\"kind\" type=\"string\" value=\"level_enter\" /><Setting id=\"room\" type=\"string\" value=\"a-02\" /></Setting>" +
        "</Setting>" +
        "</Setting>" +
        "</CustomSettings></AutoSplitterSettings>";

    private const string OtherSettings =
        "<AutoSplitterSettings><Version>1.0</Version><CustomSettings>" +
        "<Setting id=\"schema_version\" type=\"i64\">1</Setting>" +
        "<Setting id=\"cfg\" type=\"map\"><Setting id=\"auto_reset\" type=\"bool\">False</Setting></Setting>" +
        "</CustomSettings></AutoSplitterSettings>";

    private readonly LiveSplitHost host;
    private readonly List<IComponent> created = [];

    public ComponentTests(LiveSplitHost host)
    {
        this.host = host;
    }

    [Fact]
    public void ASR_loads_the_wasm_module()
    {
        // Compiling and instantiating the module is where unsupported wasm features
        // and missing asr functions fail.
        IComponent component = Create();

        Assert.StartsWith("Running ", Status(component));
    }

    [Fact]
    public void Settings_round_trip_exactly_through_the_runtime()
    {
        IComponent component = Create();

        SetSettings(component, Settings);
        Assert.Equal(Parse(Settings).InnerXml, GetSettings(component));
        XmlNode snapshot = host.OnUiThread(() => component.GetSettings(new XmlDocument()));

        // Replaces rather than merges, which the editor relies on to remove keys.
        SetSettings(component, OtherSettings);
        Assert.Equal(Parse(OtherSettings).InnerXml, GetSettings(component));

        // What LiveSplit's settings dialog does on Cancel.
        host.OnUiThread(() => component.SetSettings(snapshot));
        Assert.Equal(Parse(Settings).InnerXml, GetSettings(component));
    }

    [Fact]
    public void Dispose_stops_the_runtime()
    {
        // Reaches into ASR's private fields, so it may need updating when ASR changes.
        IComponent component = Create();
        created.Remove(component);
        object inner = GetField(component, "inner");
        object asrSettings = GetField(inner, "settings");
        object runtime = asrSettings.GetType().GetField("runtime").GetValue(asrSettings);
        Assert.NotNull(runtime);

        host.OnUiThread(component.Dispose);

        Assert.Null(GetField(inner, "updateTimer"));
        Assert.Equal(IntPtr.Zero, (IntPtr)GetField(runtime, "ptr"));
    }

    [Fact]
    public void A_missing_wasm_module_is_reported_and_settings_are_kept()
    {
        IComponent component = Create(host.MissingWasmBasePath);

        Assert.Contains("missing", Status(component));
        SetSettings(component, Settings);
        Assert.Equal(Parse(Settings).InnerXml, GetSettings(component));
    }

    [Fact]
    public void A_wasm_module_ASR_cannot_load_is_reported_and_settings_are_kept()
    {
        IComponent component = Create(host.CorruptWasmBasePath);

        Assert.Contains("failed to load", Status(component));
        SetSettings(component, Settings);
        Assert.Equal(Parse(Settings).InnerXml, GetSettings(component));
    }

    [Fact]
    public void A_missing_ASR_assembly_is_reported_and_settings_are_kept()
    {
        // Runs in its own AppDomain whose folder lacks LiveSplit.AutoSplittingRuntime.dll,
        // as in a LiveSplit without ASR. Covers ASR types being kept out of the factory
        // and out of everything the wrapper's constructor compiles.
        string folder = Path.Combine(host.TempPath, "without-asr");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(host.BasePath))
        {
            if (Path.GetFileName(file) != "LiveSplit.AutoSplittingRuntime.dll")
            {
                File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
            }
        }

        AppDomain domain = AppDomain.CreateDomain("without-asr", null, new AppDomainSetup
        {
            ApplicationBase = folder,
            ConfigurationFile = Path.Combine(folder, Path.GetFileName(AppDomain.CurrentDomain.SetupInformation.ConfigurationFile)),
        });
        try
        {
            var probe = (WithoutAsrProbe)domain.CreateInstanceAndUnwrap(typeof(WithoutAsrProbe).Assembly.FullName, typeof(WithoutAsrProbe).FullName);
            (string status, string settings) = probe.Run(host.BasePath, Settings);

            Assert.Contains("FileNotFoundException", status);
            Assert.Contains("LiveSplit.AutoSplittingRuntime", status);
            Assert.Equal(Parse(Settings).InnerXml, settings);
        }
        finally
        {
            AppDomain.Unload(domain);
        }
    }

    public sealed class WithoutAsrProbe : MarshalByRefObject
    {
        public (string status, string settings) Run(string basePath, string settingsXml)
        {
            ComponentManager.BasePath = basePath;
            IComponent component = new CelesteAutosplitFactory().Create(new LiveSplitState(null, null, null, null, null));
            string status = component.GetSettingsControl(LayoutMode.Vertical).Text;
            component.SetSettings(Parse(settingsXml));
            string settings = component.GetSettings(new XmlDocument()).InnerXml;
            component.Dispose();
            return (status, settings);
        }
    }

    public void Dispose()
    {
        host.OnUiThread(() =>
        {
            foreach (IComponent component in created)
            {
                component.Dispose();
            }
        });
    }

    private IComponent Create(string basePath = null)
    {
        IComponent component = host.OnUiThread(() =>
        {
            ComponentManager.BasePath = basePath ?? host.BasePath;
            try
            {
                return new CelesteAutosplitFactory().Create(host.NewState());
            }
            finally
            {
                ComponentManager.BasePath = host.BasePath;
            }
        });
        created.Add(component);
        return component;
    }

    private string Status(IComponent component)
    {
        return host.OnUiThread(() => component.GetSettingsControl(LayoutMode.Vertical).Text);
    }

    private void SetSettings(IComponent component, string xml)
    {
        host.OnUiThread(() => component.SetSettings(Parse(xml)));
    }

    private string GetSettings(IComponent component)
    {
        return host.OnUiThread(() => component.GetSettings(new XmlDocument()).InnerXml);
    }

    private static XmlElement Parse(string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        return document.DocumentElement;
    }

    private static object GetField(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
