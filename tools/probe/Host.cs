using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

namespace Probe
{
    internal static class Host
    {
        public static void Run(Assembly game)
        {
            for (int i = 0; i < Game.Anchors.GetLength(0); i++)
            {
                Game.Prepare(Game.StaticField(game, Game.Anchors[i, 1]), Game.Method(game, Game.Anchors[i, 0]));
            }

            FieldInfo settings = Game.StaticField(game, "Celeste.Settings::Instance");
            if (settings.GetValue(null) == null)
            {
                settings.SetValue(null, Activator.CreateInstance(settings.FieldType, true));
            }
            GC.SuppressFinalize(Activator.CreateInstance(Game.Type(game, "Celeste.Celeste"), true));

            Console.WriteLine("probe: ready");
            Console.Out.Flush();
            var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            for (string line = input.ReadLine(); line != null; line = input.ReadLine())
            {
                if (line.Length > 0)
                {
                    try
                    {
                        Write(game, line);
                    }
                    catch (ProbeException e)
                    {
                        throw new ProbeException("can't write " + line + ": " + e.Message);
                    }
                    continue;
                }
                Console.WriteLine("probe: written");
                Console.Out.Flush();
            }
        }

        private static void Write(Assembly game, string line)
        {
            int space = line.IndexOf(' ');
            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                throw new ProbeException("expected Namespace.Type::Field path=value or Namespace.Type::Field=null");
            }
            if (space < 0 || equals < space)
            {
                if (line.Substring(equals + 1) != "null")
                {
                    throw new ProbeException("a root can only be set to null");
                }
                Game.StaticField(game, line.Substring(0, equals)).SetValue(null, null);
                return;
            }
            FieldInfo field = Game.StaticField(game, line.Substring(0, space));
            if (field.GetValue(null) == null)
            {
                object uninitialized = FormatterServices.GetUninitializedObject(field.FieldType);
                GC.SuppressFinalize(uninitialized);
                field.SetValue(null, uninitialized);
            }
            object owner = null;
            foreach (string hop in line.Substring(space + 1, equals - space - 1).Split('.'))
            {
                owner = field.GetValue(owner) ?? throw new ProbeException(field.Name + " is null");
                field = Game.InstanceField(owner.GetType(), hop);
            }
            object value;
            try
            {
                value = Parse(field.FieldType, line.Substring(equals + 1));
            }
            catch (Exception e) when (e is FormatException || e is OverflowException)
            {
                value = null;
            }
            if (value == null)
            {
                throw new ProbeException(field.Name + " is a " + field.FieldType);
            }
            field.SetValue(owner, value);
        }

        private static object Parse(Type type, string text)
        {
            if (type == typeof(string)) return text;
            if (type == typeof(bool)) return bool.Parse(text);
            if (type == typeof(int)) return int.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (type == typeof(long)) return long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return null;
        }
    }
}
