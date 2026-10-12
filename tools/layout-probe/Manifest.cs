using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LayoutProbe
{
    /// <summary>layouts/manifest.json: what the auto splitter reads, by .NET names.</summary>
    internal sealed class Manifest
    {
        public ManifestRoot[] Roots;

        public static Manifest Load(string path)
        {
            object json;
            try
            {
                json = Json.Parse(File.ReadAllText(path));
            }
            catch (FormatException e)
            {
                throw new ProbeException("can't read " + path + ": " + e.Message);
            }
            List<object> roots = Member<List<object>>(As<JsonObject>(json, "the manifest"), "roots", null);
            return new Manifest { Roots = roots.Select((r, i) => ManifestRoot.Read(r, "roots[" + i + "]")).ToArray() };
        }

        /// <summary>An object's member, where null means the manifest itself.</summary>
        public static T Member<T>(JsonObject obj, string key, string where)
        {
            if (!obj.TryGetValue(key, out object value))
            {
                throw new ProbeException((where ?? "the manifest") + " has no \"" + key + "\"");
            }
            return As<T>(value, where == null ? key : where + "." + key);
        }

        public static T As<T>(object value, string where)
        {
            if (!(value is T typed))
            {
                throw new ProbeException(where + " should be " + (typeof(T) == typeof(JsonObject) ? "an object" : typeof(T) == typeof(string) ? "a string" : "an array"));
            }
            return typed;
        }
    }

    /// <summary>A static field the auto splitter finds through a signature, and the paths it reads from there.</summary>
    internal sealed class ManifestRoot
    {
        /// <summary>The static field, as Namespace.Type::Field.</summary>
        public string Field;

        /// <summary>
        /// The method whose compiled code holds the field's address, as
        /// Namespace.Type::Method(ParameterType, ...).
        /// </summary>
        public string Anchor;

        /// <summary>Instance field names from the root's object, joined with dots.</summary>
        public string[] Paths;

        public static ManifestRoot Read(object json, string where)
        {
            JsonObject root = Manifest.As<JsonObject>(json, where);
            return new ManifestRoot
            {
                Field = Manifest.Member<string>(root, "field", where),
                Anchor = Manifest.Member<string>(root, "anchor", where),
                Paths = Manifest.Member<List<object>>(root, "paths", where).Select((p, i) => Manifest.As<string>(p, where + ".paths[" + i + "]")).ToArray(),
            };
        }
    }
}
