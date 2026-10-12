using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LayoutProbe
{
    /// <summary>A JSON object that keeps its members in the order they were added.</summary>
    internal sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> members = new List<KeyValuePair<string, object>>();

        public void Add(string key, object value) => members.Add(new KeyValuePair<string, object>(key, value));

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => members.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Writes JSON in one stable format, so that regenerated layouts only differ where values do:
    /// two-space indents, members in the order they were added, and objects and arrays holding
    /// only scalars and arrays of scalars on one line.
    /// </summary>
    internal static class Json
    {
        public static string Write(object value)
        {
            var text = new StringBuilder();
            Write(text, value, "");
            return text.Append('\n').ToString();
        }

        private static void Write(StringBuilder text, object value, string indent)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case bool b:
                    text.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(text, s);
                    break;
                case JsonObject obj:
                    WriteContainer(text, obj.ToList(), obj.All(m => FitsOnLine(m.Value)), '{', '}', indent);
                    break;
                case IEnumerable items:
                    List<object> list = items.Cast<object>().ToList();
                    // Items are written like members without keys.
                    WriteContainer(text, list.Select(i => new KeyValuePair<string, object>(null, i)).ToList(), list.All(IsScalar), '[', ']', indent);
                    break;
                default:
                    if (!IsInteger(value))
                    {
                        throw new NotSupportedException("can't write a " + value.GetType() + " as JSON");
                    }
                    text.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                    break;
            }
        }

        private static void WriteContainer(StringBuilder text, List<KeyValuePair<string, object>> members, bool oneLine, char open, char close, string indent)
        {
            text.Append(open);
            if (members.Count > 0)
            {
                string inner = indent + "  ";
                // One-line objects get spaces inside their braces, one-line arrays don't.
                string padding = oneLine ? (open == '{' ? " " : "") : "\n" + inner;
                text.Append(padding);
                for (int i = 0; i < members.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(oneLine ? ", " : ",\n" + inner);
                    }
                    if (members[i].Key != null)
                    {
                        WriteString(text, members[i].Key);
                        text.Append(": ");
                    }
                    Write(text, members[i].Value, inner);
                }
                text.Append(oneLine ? padding : "\n" + indent);
            }
            text.Append(close);
        }

        private static void WriteString(StringBuilder text, string s)
        {
            text.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\')
                {
                    text.Append('\\').Append(c);
                }
                else if (c < ' ')
                {
                    text.AppendFormat("\\u{0:x4}", (int)c);
                }
                else
                {
                    text.Append(c);
                }
            }
            text.Append('"');
        }

        private static bool FitsOnLine(object value) =>
            IsScalar(value) || (value is IEnumerable items && !(value is JsonObject) && items.Cast<object>().All(IsScalar));

        private static bool IsScalar(object value) =>
            value == null || value is string || value is bool || IsInteger(value);

        private static bool IsInteger(object value) =>
            value is int || value is long || value is uint || value is ulong;
    }
}
