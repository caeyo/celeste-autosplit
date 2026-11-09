using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Xml;

namespace CelesteAutosplit;

internal sealed class SchemaChoice
{
    public object Value;
    public string Label;
}

internal sealed class SchemaSetting
{
    public string Key;
    public string Type;
    public object Default;
    public string Label;
    public string Description;
    public List<SchemaChoice> Values;
}

internal sealed class SchemaField
{
    public string Name;
    public string Type;
    public string Label;
    public string Description;
    public object Default;
    public List<SchemaChoice> Values;
    public string ValuesFrom;
    public bool Everest;
    public string Replaces;
    public bool ReadOnly;
}

internal sealed class SchemaTrigger
{
    public string Kind;
    public string Label;
    public List<string> Required;
    public List<string> Optional;
    public List<string> Offer;

    public IEnumerable<string> Fields => Required.Concat(Optional);
}

internal sealed class SchemaPreset
{
    public string Label;
    public Dictionary<string, object> Settings;
    public Dictionary<string, Dictionary<string, object>> TriggerFields;
}

internal sealed class SchemaCheckpoint
{
    public List<string> Names;
}

internal sealed class SchemaChapter
{
    public long Id;
    public string Name;
    public List<SchemaCheckpoint> Checkpoints;
}

internal sealed class SettingsSchema
{
    public long Version;
    public List<SchemaSetting> Settings;
    public List<SchemaField> Fields;
    public List<SchemaTrigger> Triggers;
    public List<SchemaPreset> Presets;
    public List<SchemaChapter> Chapters;

    public SchemaField Field(string name) => Fields.FirstOrDefault(field => field.Name == name);

    public SchemaTrigger Trigger(string kind) => Triggers.FirstOrDefault(trigger => trigger.Kind == kind);

    public SchemaSetting Setting(string key) => Settings.FirstOrDefault(setting => setting.Key == key);

    public static SettingsSchema Load()
    {
        using Stream stream = typeof(SettingsSchema).Assembly.GetManifestResourceStream("CelesteAutosplit.settings-schema.json");
        using var reader = new StreamReader(stream);
        var serializer = new JavaScriptSerializer();
        var root = (Dictionary<string, object>)Normalize(serializer.DeserializeObject(reader.ReadToEnd()));

        return new SettingsSchema
        {
            Version = (long)root["schema_version"],
            Settings = List(root["settings"]).Select(ParseSetting).ToList(),
            Fields = ((Dictionary<string, object>)root["fields"]).Select(pair => ParseField(pair.Key, (Dictionary<string, object>)pair.Value)).ToList(),
            Triggers = List(root["triggers"]).Select(ParseTrigger).ToList(),
            Presets = List(root["presets"]).Select(ParsePreset).ToList(),
            Chapters = List(root["chapters"]).Select(ParseChapter).ToList(),
        };
    }

    private static object Normalize(object value)
    {
        switch (value)
        {
            case Dictionary<string, object> map:
                return map.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value));
            case object[] list:
                return list.Select(Normalize).ToList();
            case int number:
                return (long)number;
            case decimal number:
                return (long)number;
            default:
                return value;
        }
    }

    private static List<object> List(object value) => (List<object>)value;

    private static List<string> Strings(object value) => List(value).Cast<string>().ToList();

    private static string String(Dictionary<string, object> map, string key) =>
        map.TryGetValue(key, out object value) ? value as string : null;

    private static List<SchemaChoice> Choices(Dictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out object value))
        {
            return null;
        }

        return List(value).Cast<Dictionary<string, object>>().Select(choice => new SchemaChoice
        {
            Value = choice["value"],
            Label = String(choice, "label"),
        }).ToList();
    }

    private static SchemaSetting ParseSetting(object value)
    {
        var map = (Dictionary<string, object>)value;
        return new SchemaSetting
        {
            Key = String(map, "key"),
            Type = String(map, "type"),
            Default = map["default"],
            Label = String(map, "label"),
            Description = String(map, "description"),
            Values = Choices(map, "values"),
        };
    }

    private static SchemaField ParseField(string name, Dictionary<string, object> map)
    {
        return new SchemaField
        {
            Name = name,
            Type = String(map, "type"),
            Label = String(map, "label"),
            Description = String(map, "description"),
            Default = map.TryGetValue("default", out object fallback) ? fallback : null,
            Values = Choices(map, "values"),
            ValuesFrom = String(map, "values_from"),
            Everest = map.TryGetValue("everest", out object everest) && (bool)everest,
            Replaces = String(map, "replaces"),
            ReadOnly = map.TryGetValue("read_only", out object readOnly) && (bool)readOnly,
        };
    }

    private static SchemaTrigger ParseTrigger(object value)
    {
        var map = (Dictionary<string, object>)value;
        return new SchemaTrigger
        {
            Kind = String(map, "kind"),
            Label = String(map, "label"),
            Required = Strings(map["required"]),
            Optional = Strings(map["optional"]),
            Offer = Strings(map["offer"]),
        };
    }

    private static SchemaPreset ParsePreset(object value)
    {
        var map = (Dictionary<string, object>)value;
        return new SchemaPreset
        {
            Label = String(map, "label"),
            Settings = (Dictionary<string, object>)map["settings"],
            TriggerFields = ((Dictionary<string, object>)map["trigger_fields"])
                .ToDictionary(pair => pair.Key, pair => (Dictionary<string, object>)pair.Value),
        };
    }

    private static SchemaChapter ParseChapter(object value)
    {
        var map = (Dictionary<string, object>)value;
        return new SchemaChapter
        {
            Id = (long)map["id"],
            Name = String(map, "name"),
            Checkpoints = List(map["checkpoints"]).Cast<Dictionary<string, object>>()
                .Select(checkpoint => new SchemaCheckpoint { Names = Strings(checkpoint["names"]) })
                .ToList(),
        };
    }

    public bool IsNewer(XmlElement map)
    {
        return SettingsXml.Value(SettingsXml.Find(map, "schema_version")) is long version && version > Version;
    }

    public bool Flag(XmlElement map, string key)
    {
        if (IsNewer(map))
        {
            return false;
        }

        XmlElement cfg = SettingsXml.FindMap(map, "cfg");
        return SettingsXml.Value(SettingsXml.Find(cfg, key)) is bool value
            ? value
            : Setting(key).Default is true;
    }

    public string Problem(XmlElement trigger, bool everest)
    {
        if (SettingsXml.Type(trigger) != "map")
        {
            return "isn't a map";
        }

        if (SettingsXml.Value(SettingsXml.Find(trigger, "kind")) is not string kindName)
        {
            return "has no kind";
        }

        SchemaTrigger kind = Trigger(kindName);
        if (kind == null)
        {
            return $"has the unknown kind \"{kindName}\"";
        }

        var present = new HashSet<string>();
        long? chapter = null;
        long? checkpoint = null;
        foreach (string name in kind.Fields)
        {
            SchemaField field = Field(name);
            if (field.Everest && !everest)
            {
                continue;
            }

            XmlElement element = SettingsXml.Find(trigger, name);
            if (element == null)
            {
                continue;
            }

            object value = SettingsXml.Value(element);
            string invalid = $"has an invalid {field.Label.ToLowerInvariant()}";
            if (SettingsXml.Type(element) != field.Type || value == null)
            {
                return invalid;
            }

            if (field.Values != null && !field.Values.Any(choice => Equals(choice.Value, value)))
            {
                return invalid;
            }

            if (field.ValuesFrom == "chapters")
            {
                if ((long)value < 0)
                {
                    return invalid;
                }

                chapter = (long)value;
            }
            else if (field.ValuesFrom == "checkpoints")
            {
                checkpoint = (long)value;
            }

            present.Add(name);
            if (field.Replaces != null)
            {
                present.Add(field.Replaces);
            }
        }

        string missing = kind.Required.FirstOrDefault(name => !present.Contains(name));
        if (missing != null)
        {
            return $"has no {Field(missing).Label.ToLowerInvariant()}";
        }

        if (checkpoint != null)
        {
            SchemaChapter known = Chapters.FirstOrDefault(entry => entry.Id == chapter);
            if (known == null || checkpoint < 1 || checkpoint > known.Checkpoints.Count)
            {
                return "has an invalid checkpoint";
            }
        }

        return null;
    }
}

internal static class SettingsXml
{
    public static XmlElement Find(XmlElement map, string key)
    {
        return map?.ChildNodes.OfType<XmlElement>().FirstOrDefault(element => element.GetAttribute("id") == key);
    }

    public static XmlElement FindMap(XmlElement map, string key)
    {
        XmlElement element = Find(map, key);
        return Type(element) == "map" ? element : null;
    }

    public static IEnumerable<XmlElement> Items(XmlElement list)
    {
        return list?.ChildNodes.OfType<XmlElement>() ?? [];
    }

    public static string Type(XmlElement element) => element?.GetAttribute("type");

    public static object Value(XmlElement element)
    {
        switch (Type(element))
        {
            case "bool":
                return bool.TryParse(element.InnerText, out bool flag) ? flag : null;
            case "i64":
                return long.TryParse(element.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : null;
            case "string":
                return element.GetAttribute("value");
            default:
                return null;
        }
    }

    public static void Remove(XmlElement map, string key)
    {
        XmlElement element = Find(map, key);
        if (element != null)
        {
            map.RemoveChild(element);
        }
    }

    public static void Set(XmlElement map, string key, object value, IList<string> order)
    {
        if (value == null)
        {
            Remove(map, key);
            return;
        }

        XmlElement element = Find(map, key);
        if (element == null)
        {
            element = map.OwnerDocument.CreateElement("Setting");
            element.SetAttribute("id", key);
            InsertOrdered(map, element, key, order);
        }

        Assign(element, value);
    }

    public static XmlElement SetContainer(XmlElement map, string key, string type, IList<string> order)
    {
        XmlElement element = Find(map, key);
        if (element == null)
        {
            element = map.OwnerDocument.CreateElement("Setting");
            element.SetAttribute("id", key);
            InsertOrdered(map, element, key, order);
        }
        else if (Type(element) == type)
        {
            return element;
        }

        MakeContainer(element, type);
        return element;
    }

    public static void MakeContainer(XmlElement element, string type)
    {
        string id = element.GetAttribute("id");
        element.RemoveAll();
        if (id.Length > 0)
        {
            element.SetAttribute("id", id);
        }

        element.SetAttribute("type", type);
    }

    public static XmlElement NewItem(XmlDocument document)
    {
        XmlElement element = document.CreateElement("Setting");
        element.SetAttribute("type", "map");
        return element;
    }

    private static void InsertOrdered(XmlElement map, XmlElement element, string key, IList<string> order)
    {
        int rank = order.IndexOf(key);
        XmlElement after = null;
        foreach (XmlElement sibling in map.ChildNodes.OfType<XmlElement>())
        {
            int siblingRank = order.IndexOf(sibling.GetAttribute("id"));
            if (siblingRank >= 0 && siblingRank < rank)
            {
                after = sibling;
            }
        }

        if (after != null)
        {
            map.InsertAfter(element, after);
        }
        else
        {
            map.PrependChild(element);
        }
    }

    private static void Assign(XmlElement element, object value)
    {
        string id = element.GetAttribute("id");
        element.RemoveAll();
        if (id.Length > 0)
        {
            element.SetAttribute("id", id);
        }

        switch (value)
        {
            case bool flag:
                element.SetAttribute("type", "bool");
                element.InnerText = flag.ToString(CultureInfo.InvariantCulture);
                break;
            case long number:
                element.SetAttribute("type", "i64");
                element.InnerText = number.ToString(CultureInfo.InvariantCulture);
                break;
            case string text:
                element.SetAttribute("type", "string");
                element.SetAttribute("value", text);
                break;
            default:
                throw new ArgumentException($"Can't write a setting of type {value.GetType()}");
        }
    }
}
