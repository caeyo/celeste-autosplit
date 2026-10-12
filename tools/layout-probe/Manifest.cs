using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LayoutProbe
{
    /// <summary>layouts/manifest.json: what the auto splitter reads, by .NET names.</summary>
    [DataContract]
    internal sealed class Manifest
    {
        [DataMember(Name = "roots", IsRequired = true)]
        public ManifestRoot[] Roots { get; set; }

        public static Manifest Load(string path)
        {
            using (FileStream file = File.OpenRead(path))
            {
                try
                {
                    return (Manifest)new DataContractJsonSerializer(typeof(Manifest)).ReadObject(file);
                }
                catch (SerializationException e)
                {
                    throw new ProbeException("can't read " + path + ": " + e.Message);
                }
            }
        }
    }

    /// <summary>A static field the auto splitter finds through a signature, and the paths it reads from there.</summary>
    [DataContract]
    internal sealed class ManifestRoot
    {
        /// <summary>The static field, as Namespace.Type::Field.</summary>
        [DataMember(Name = "field", IsRequired = true)]
        public string Field { get; set; }

        /// <summary>
        /// The method whose compiled code holds the field's address, as
        /// Namespace.Type::Method(ParameterType, ...).
        /// </summary>
        [DataMember(Name = "anchor", IsRequired = true)]
        public string Anchor { get; set; }

        /// <summary>Instance field names from the root's object, joined with dots.</summary>
        [DataMember(Name = "paths", IsRequired = true)]
        public string[] Paths { get; set; }
    }
}
