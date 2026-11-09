using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Xml;
using CelesteAutosplit;

[assembly: SupportedOSPlatform("browser")]

namespace LegacyImport;

public static partial class Program
{
    public static void Main()
    {
    }

    [JSExport]
    public static string Migrate(string settings)
    {
        var document = new XmlDocument();
        document.LoadXml(settings);
        return LegacyMigration.Migrate(document.DocumentElement).OuterXml;
    }
}
