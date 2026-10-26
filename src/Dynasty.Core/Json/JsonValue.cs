using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dynasty.Core.Mathematics;

namespace Dynasty.Core.Json
{
    public enum JsonKind
    {
        Null = 0,
        Bool = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5,
    }

    public sealed class JsonException : Exception
    {
        public JsonException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// A parsed JSON value for ruleset packs (spec §27). No external dependencies; object members keep
    /// their order; numbers keep their decimal text and are read exactly as integers or <see cref="Fixed"/>
    /// (no floating point reaches the simulation); duplicate keys, trailing commas and nesting deeper than
    /// <see cref="MaxDepth"/> are rejected.
    /// </summary>
    public sealed class JsonValue
    {
        public const int MaxDepth = 64;

        private static readonly JsonValue[] NoItems = new JsonValue[0];
        private static readonly KeyValuePair<string, JsonValue>[] NoMembers = new KeyValuePair<string, JsonValue>[0];

        private readonly string? _text;
        private readonly bool _bool;
        private readonly IReadOnlyList<JsonValue> _items;
        private readonly IReadOnlyList<KeyValuePair<string, JsonValue>> _members;
        private readonly Dictionary<string, JsonValue>? _byKey;

        private JsonValue(JsonKind kind, string? text = null, bool flag = false, IReadOnlyList<JsonValue>? items = null,
            List<KeyValuePair<string, JsonValue>>? members = null)
        {
            Kind = kind;
            _text = text;
            _bool = flag;
            _items = items ?? NoItems;
            _members = (IReadOnlyList<KeyValuePair<string, JsonValue>>?)members ?? NoMembers;
            if (members != null)
            {
                _byKey = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonValue> member in members)
                    _byKey.Add(member.Key, member.Value);
            }
        }

        public JsonKind Kind { get; }
        public bool IsNull => Kind == JsonKind.Null;

        /// <summary>Array items (empty for other kinds).</summary>
        public IReadOnlyList<JsonValue> Items => _items;

        /// <summary>Object members in document order (empty for other kinds).</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => _members;

        /// <summary>A required object member.</summary>
        public JsonValue this[string key] => Get(key) ?? throw new JsonException("Missing member \"" + key + "\".");

        public bool Has(string key) => _byKey != null && _byKey.ContainsKey(key);

        /// <summary>An optional object member, or null when absent.</summary>
        public JsonValue? Get(string key) =>
            Kind != JsonKind.Object ? throw new JsonException("Expected an object to read \"" + key + "\", found " + Kind + ".")
            : _byKey!.TryGetValue(key, out JsonValue? value) ? value : null;

        public string AsString() => Kind == JsonKind.String ? _text! : throw Wrong(JsonKind.String);

        public bool AsBool() => Kind == JsonKind.Bool ? _bool : throw Wrong(JsonKind.Bool);

        public int AsInt()
        {
            if (Kind != JsonKind.Number)
                throw Wrong(JsonKind.Number);
            (long numerator, long denominator) = Decimal(_text!);
            if (numerator % denominator != 0 || numerator / denominator > int.MaxValue || numerator / denominator < int.MinValue)
                throw new JsonException("Expected a whole number, found " + _text + ".");
            return (int)(numerator / denominator);
        }

        /// <summary>The number exactly as its decimal text says (e.g. "1.6" is 16/10).</summary>
        public Fixed AsFixed()
        {
            if (Kind != JsonKind.Number)
                throw Wrong(JsonKind.Number);
            (long numerator, long denominator) = Decimal(_text!);
            return Fixed.FromRatio(numerator, denominator);
        }

        /// <summary>The number's text as written in the document.</summary>
        public string NumberText => Kind == JsonKind.Number ? _text! : throw Wrong(JsonKind.Number);

        private JsonException Wrong(JsonKind expected) => new JsonException("Expected " + expected + ", found " + Kind + ".");

        /// <summary>Decimal text (optional sign, digits, fraction, integer exponent) as numerator / power of ten.</summary>
        private static (long Numerator, long Denominator) Decimal(string text)
        {
            int e = text.IndexOfAny(new[] { 'e', 'E' });
            string mantissa = e < 0 ? text : text.Substring(0, e);
            int exponent = e < 0 ? 0 : int.Parse(text.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            int dot = mantissa.IndexOf('.');
            string digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
            int scale = (dot < 0 ? 0 : mantissa.Length - dot - 1) - exponent;
            long numerator = long.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            long denominator = 1;
            for (; scale > 0; scale--)
                denominator = checked(denominator * 10);
            for (; scale < 0; scale++)
                numerator = checked(numerator * 10);
            return (numerator, denominator);
        }

        public static JsonValue Parse(string text)
        {
            var parser = new Parser(text);
            JsonValue value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("Unexpected text after the value");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string text)
            {
                _s = text;
            }

            public bool AtEnd => _i >= _s.Length;

            public JsonException Error(string what) => new JsonException(what + " at position " + _i + ".");

            public void SkipWhitespace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r'))
                    _i++;
            }

            public JsonValue ReadValue(int depth)
            {
                if (depth > MaxDepth)
                    throw Error("Nesting deeper than " + MaxDepth);
                SkipWhitespace();
                if (AtEnd)
                    throw Error("Unexpected end of input");
                char c = _s[_i];
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return new JsonValue(JsonKind.String, ReadString());
                    case 't':
                        Expect("true");
                        return new JsonValue(JsonKind.Bool, flag: true);
                    case 'f':
                        Expect("false");
                        return new JsonValue(JsonKind.Bool, flag: false);
                    case 'n':
                        Expect("null");
                        return new JsonValue(JsonKind.Null);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return new JsonValue(JsonKind.Number, ReadNumber());
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0)
                    throw Error("Expected " + word);
                _i += word.Length;
            }

            private JsonValue ReadObject(int depth)
            {
                _i++;
                var members = new List<KeyValuePair<string, JsonValue>>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && _s[_i] == '}')
                {
                    _i++;
                    return new JsonValue(JsonKind.Object, members: members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != '"')
                        throw Error("Expected a member name");
                    string key = ReadString();
                    if (!seen.Add(key))
                        throw Error("Duplicate member \"" + key + "\"");
                    SkipWhitespace();
                    if (AtEnd || _s[_i] != ':')
                        throw Error("Expected ':'");
                    _i++;
                    members.Add(new KeyValuePair<string, JsonValue>(key, ReadValue(depth + 1)));
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unexpected end of input in an object");
                    if (_s[_i] == '}')
                    {
                        _i++;
                        return new JsonValue(JsonKind.Object, members: members);
                    }

                    if (_s[_i] != ',')
                        throw Error("Expected ',' or '}'");
                    _i++;
                }
            }

            private JsonValue ReadArray(int depth)
            {
                _i++;
                var items = new List<JsonValue>();
                SkipWhitespace();
                if (!AtEnd && _s[_i] == ']')
                {
                    _i++;
                    return new JsonValue(JsonKind.Array, items: items);
                }

                while (true)
                {
                    items.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unexpected end of input in an array");
                    if (_s[_i] == ']')
                    {
                        _i++;
                        return new JsonValue(JsonKind.Array, items: items);
                    }

                    if (_s[_i] != ',')
                        throw Error("Expected ',' or ']'");
                    _i++;
                }
            }

            private string ReadString()
            {
                _i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                        throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"')
                        return sb.ToString();
                    if (c != '\\')
                    {
                        if (c < ' ')
                            throw Error("Control character in a string");
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd)
                        throw Error("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length || !int.TryParse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("Bad \\u escape");
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            throw Error("Unknown escape \\" + e);
                    }
                }
            }

            private string ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-')
                    _i++;
                int digits = SkipDigits();
                if (digits == 0)
                    throw Error("Expected digits");
                if (!AtEnd && _s[_i] == '.')
                {
                    _i++;
                    if (SkipDigits() == 0)
                        throw Error("Expected digits after '.'");
                }

                if (!AtEnd && (_s[_i] == 'e' || _s[_i] == 'E'))
                {
                    _i++;
                    if (!AtEnd && (_s[_i] == '+' || _s[_i] == '-'))
                        _i++;
                    if (SkipDigits() == 0)
                        throw Error("Expected exponent digits");
                }

                return _s.Substring(start, _i - start);
            }

            private int SkipDigits()
            {
                int start = _i;
                while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9')
                    _i++;
                return _i - start;
            }
        }
    }
}
