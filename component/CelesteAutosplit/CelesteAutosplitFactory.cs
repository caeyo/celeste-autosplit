using CelesteAutosplit;
using LiveSplit.Model;
using LiveSplit.UI.Components;
using System;

[assembly: ComponentFactory(typeof(CelesteAutosplitFactory))]

namespace CelesteAutosplit;

public class CelesteAutosplitFactory : IComponentFactory
{
    public const string Name = "Celeste Autosplitter";

    public string ComponentName => Name;
    public string Description => "Celeste autosplitter, running celeste_autosplit.wasm through the Auto Splitting Runtime.";
    public ComponentCategory Category => ComponentCategory.Control;

    public IComponent Create(LiveSplitState state) => new CelesteAutosplitComponent(state);

    public string UpdateName => ComponentName;
    public string XMLURL => "https://raw.githubusercontent.com/caeyo/celeste-autosplit/master/update.xml";
    public string UpdateURL => "https://github.com/caeyo/celeste-autosplit/releases/";

    public Version Version
    {
        get
        {
            Version version = typeof(CelesteAutosplitFactory).Assembly.GetName().Version;
            return new Version(version.Major, version.Minor, version.Build);
        }
    }
}
