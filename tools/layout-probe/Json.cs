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

        public bool TryGetValue(string key, out object value)
        {
            foreach (KeyValuePair<string, object> member in members)
            {
                if (member.Key == key)
                {
                    value = member.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => members.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Reads and writes JSON. The game's bundled Mono lacks what DataContractJsonSerializer
    /// needs, so this does it by hand. Objects are read as JsonObjects, arrays as lists, and
    /// numbers as longs or doubles.
    ///
    /// Writing uses one stable format, so that regenerated layouts only differ where values do:
    /// two-space indents, members in the order they were added, and objects and arrays holding
    /// only scalars and arrays of scalars on one line.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text) => new Reader(text).Document();

        private sealed class Reader
        {
            private readonly string text;
            private int position;

            public Reader(string text)
            {
                this.text = text;
            }

            public object Document()
            {
                object value = Value();
                SkipSpace();
                if (position < text.Length)
                {
                    throw Error("unexpected text after the end");
                }
                return value;
            }

            private object Value()
            {
                SkipSpace();
                switch (Peek())
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': return Word("true", true);
                    case 'f': return Word("false", false);
                    case 'n': return Word("null", null);
                    default: return Number();
                }
            }

            private JsonObject Object()
            {
                var obj = new JsonObject();
                position++;
                SkipSpace();
                if (Peek() == '}')
                {
                    position++;
                    return obj;
                }
                while (true)
                {
                    SkipSpace();
                    if (Peek() != '"')
                    {
                        throw Error("expected a member name");
                    }
                    string key = String();
                    SkipSpace();
                    Expect(':');
                    obj.Add(key, Value());
                    SkipSpace();
                    if (Peek() == '}')
                    {
                        position++;
                        return obj;
                    }
                    Expect(',');
                }
            }

            private List<object> Array()
            {
                var list = new List<object>();
                position++;
                SkipSpace();
                if (Peek() == ']')
                {
                    position++;
                    return list;
                }
                while (true)
                {
                    list.Add(Value());
                    SkipSpace();
                    if (Peek() == ']')
                    {
                        position++;
                        return list;
                    }
                    Expect(',');
                }
            }

            private string String()
            {
                var s = new StringBuilder();
                position++;
                while (true)
                {
                    char c = Peek();
                    position++;
                    if (c == '"')
                    {
                        return s.ToString();
                    }
                    if (c < ' ')
                    {
                        throw Error("unescaped control character in a string");
                    }
                    if (c != '\\')
                    {
                        s.Append(c);
                        continue;
                    }
                    char escape = Peek();
                    position++;
                    switch (escape)
                    {
                        case '"': case '\\': case '/': s.Append(escape); break;
                        case 'b': s.Append('\b'); break;
                        case 'f': s.Append('\f'); break;
                        case 'n': s.Append('\n'); break;
                        case 'r': s.Append('\r'); break;
                        case 't': s.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length || !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Error("bad \\u escape");
                            }
                            s.Append((char)code);
                            position += 4;
                            break;
                        default: throw Error("bad escape \\" + escape);
                    }
                }
            }

            private object Number()
            {
                int start = position;
                while (position < text.Length && "+-0123456789.eE".IndexOf(text[position]) >= 0)
                {
                    position++;
                }
                string number = text.Substring(start, position - start);
                if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
                {
                    return integer;
                }
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                {
                    return real;
                }
                position = start;
                throw Error(number.Length == 0 ? "unexpected '" + Peek() + "'" : "bad number " + number);
            }

            private object Word(string word, object value)
            {
                if (string.CompareOrdinal(text, position, word, 0, word.Length) != 0)
                {
                    throw Error("unexpected '" + Peek() + "'");
                }
                position += word.Length;
                return value;
            }

            private void Expect(char c)
            {
                if (Peek() != c)
                {
                    throw Error("expected '" + c + "'");
                }
                position++;
            }

            private char Peek()
            {
                if (position >= text.Length)
                {
                    throw Error("unexpected end");
                }
                return text[position];
            }

            private void SkipSpace()
            {
                while (position < text.Length && " \t\r\n".IndexOf(text[position]) >= 0)
                {
                    position++;
                }
            }

            private FormatException Error(string message)
            {
                int line = 1 + text.Take(position).Count(c => c == '\n');
                int column = position - text.LastIndexOf('\n', Math.Max(0, position - 1));
                return new FormatException(message + " at line " + line + ", column " + column);
            }
        }

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
