using LiveSplit.Model;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Xunit;

namespace CelesteAutosplit.Tests;

[Collection(LiveSplitCollection.Name)]
public sealed class ComponentTests : IDisposable
{
    private const string Settings =
        "<AutoSplitterSettings><Version>1.0</Version><CustomSettings>" +
        "<Setting id=\"schema_version\" type=\"i64\">1</Setting>" +
        "<Setting id=\"cfg\" type=\"map\">" +
        "<Setting id=\"everest\" type=\"bool\">True</Setting>" +
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
        "<Setting id=\"cfg\" type=\"map\"><Setting id=\"everest\" type=\"bool\">False</Setting></Setting>" +
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

        SetSettings(component, OtherSettings);
        Assert.Equal(Parse(OtherSettings).InnerXml, GetSettings(component));

        host.OnUiThread(() => component.SetSettings(snapshot));
        Assert.Equal(Parse(Settings).InnerXml, GetSettings(component));
    }

    [Theory]
    [MemberData(nameof(LegacyFiles.Names), MemberType = typeof(LegacyFiles))]
    public void A_migrated_legacy_file_round_trips_exactly_through_the_runtime(string name)
    {
        IComponent component = Create();

        host.OnUiThread(() => component.SetSettings(LegacyFiles.Settings(name)));

        Assert.Equal(LegacyFiles.Expected(name).InnerXml, GetSettings(component));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public void Legacy_settings_are_kept_as_they_are_without_an_ASR_to_run_the_module(string kind)
    {
        IComponent component = Create(kind == "missing" ? host.MissingWasmBasePath : host.CorruptWasmBasePath);
        XmlElement legacy = LegacyFiles.Settings("1a-rooms");

        host.OnUiThread(() => component.SetSettings(legacy));

        Assert.Equal(legacy.InnerXml, GetSettings(component));
    }

    [Fact]
    public void A_missing_wasm_module_is_reported_and_settings_are_kept()
    {
        IComponent component = Create(host.MissingWasmBasePath);

        Assert.Contains("missing", Status(component));
        Assert.Contains("delete celeste-autosplit.dll", Status(component));
        Assert.Contains("https://github.com/caeyo/celeste-autosplit/releases/latest", Status(component));
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

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_first_settings_switch_to_game_time_only_if_set_game_time_is_on(bool? value, bool switches)
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);
        string setting = value == null ? "" : $"<Setting id=\"set_game_time\" type=\"bool\">{value}</Setting>";

        SetSettings(component, Cfg(setting));

        Assert.Equal(switches ? TimingMethod.GameTime : TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Later_settings_do_not_switch_to_game_time_again()
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);
        SetSettings(component, Cfg(""));
        host.OnUiThread(() => state.CurrentTimingMethod = TimingMethod.RealTime);

        SetSettings(component, Cfg(""));

        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void A_run_with_no_settings_switches_to_game_time_on_the_first_update_only()
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);
        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));

        host.OnUiThread(() => component.Update(null, state, 0, 0, LayoutMode.Vertical));
        Assert.Equal(TimingMethod.GameTime, host.OnUiThread(() => state.CurrentTimingMethod));

        host.OnUiThread(() => state.CurrentTimingMethod = TimingMethod.RealTime);
        host.OnUiThread(() => component.Update(null, state, 0, 0, LayoutMode.Vertical));
        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Settings_given_before_the_first_update_decide_game_time_and_the_update_changes_nothing()
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);

        SetSettings(component, Cfg("<Setting id=\"set_game_time\" type=\"bool\">False</Setting>"));
        host.OnUiThread(() => component.Update(null, state, 0, 0, LayoutMode.Vertical));

        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Cancelling_the_dialog_after_the_first_update_does_not_switch_to_game_time()
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);
        host.OnUiThread(() => component.Update(null, state, 0, 0, LayoutMode.Vertical));
        host.OnUiThread(() => state.CurrentTimingMethod = TimingMethod.RealTime);
        host.OnUiThread(() => component.GetSettingsControl(LayoutMode.Vertical));
        XmlNode snapshot = host.OnUiThread(() => component.GetSettings(new XmlDocument()));

        host.OnUiThread(() => component.SetSettings(snapshot));

        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    [Fact]
    public void Migrated_settings_switch_to_game_time_as_their_set_game_time_says()
    {
        LiveSplitState state = host.OnUiThread(() => host.NewState());
        IComponent component = Create(state: state);
        var document = new XmlDocument();
        document.LoadXml("<AutoSplitterSettings><AutoReset>True</AutoReset><SetGameTime>False</SetGameTime><Splits /></AutoSplitterSettings>");

        host.OnUiThread(() => component.SetSettings(document.DocumentElement));

        Assert.Equal(TimingMethod.RealTime, host.OnUiThread(() => state.CurrentTimingMethod));
    }

    private IComponent Create(string basePath = null, LiveSplitState state = null)
    {
        IComponent component = host.OnUiThread(() =>
        {
            ComponentManager.BasePath = basePath ?? host.BasePath;
            try
            {
                return new CelesteAutosplitFactory().Create(state ?? host.NewState());
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
        return host.OnUiThread(() =>
        {
            Control control = component.GetSettingsControl(LayoutMode.Vertical);
            return control is Label ? control.Text : control.Controls.Find("status", true)[0].Text;
        });
    }

    private static string Cfg(string cfg)
    {
        return "<AutoSplitterSettings><Version>1.0</Version><CustomSettings><Setting id=\"schema_version\" type=\"i64\">1</Setting><Setting id=\"cfg\" type=\"map\">" + cfg + "</Setting></CustomSettings></AutoSplitterSettings>";
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
}
