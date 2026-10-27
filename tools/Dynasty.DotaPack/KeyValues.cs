/// <summary>
/// Valve KeyValues text (the game's script files such as npc_units.txt): quoted keys with a quoted value or a
/// braced block, // comments. Reads the blocks of one level under the root as flat key → value tables.
/// </summary>
static class KeyValues
{
    /// <summary>The root block's children (units, abilities, …), each with its direct string values; nested blocks are skipped.</summary>
    public static Dictionary<string, Dictionary<string, string>> Entries(string text)
    {
        var tokens = Tokens(text).GetEnumerator();
        Expect(tokens, out string _);                         // root name ("DOTAUnits")
        if (!tokens.MoveNext() || tokens.Current != "{")
            throw new InvalidDataException("KeyValues: the root block does not open");
        var entries = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        while (tokens.MoveNext() && tokens.Current != "}")
        {
            string name = tokens.Current;
            if (!tokens.MoveNext())
                throw new InvalidDataException("KeyValues: " + name + " has no value");
            if (tokens.Current != "{")
                continue;                                     // a plain value at the root ("Version" "1")
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            while (tokens.MoveNext() && tokens.Current != "}")
            {
                string key = tokens.Current;
                Expect(tokens, out string value);
                if (value == "{")
                    SkipBlock(tokens);
                else
                    values[key] = value;
            }

            entries[name] = values;
        }

        return entries;
    }

    /// <summary>The whole file as nested objects: a key's value is a string or an object (later duplicates win).</summary>
    public static System.Text.Json.Nodes.JsonObject Tree(string text)
    {
        var tokens = Tokens(text).GetEnumerator();
        Expect(tokens, out string _);                         // root name
        if (!tokens.MoveNext() || tokens.Current != "{")
            throw new InvalidDataException("KeyValues: the root block does not open");
        return Block(tokens);
    }

    private static System.Text.Json.Nodes.JsonObject Block(IEnumerator<string> tokens)
    {
        var block = new System.Text.Json.Nodes.JsonObject();
        while (tokens.MoveNext() && tokens.Current != "}")
        {
            string key = tokens.Current;
            Expect(tokens, out string value);
            block[key] = value == "{" ? Block(tokens) : System.Text.Json.Nodes.JsonValue.Create(value);
        }

        return block;
    }

    private static void Expect(IEnumerator<string> tokens, out string value)
    {
        if (!tokens.MoveNext())
            throw new InvalidDataException("KeyValues: unexpected end");
        value = tokens.Current;
    }

    private static void SkipBlock(IEnumerator<string> tokens)
    {
        for (int depth = 1; depth > 0;)
        {
            if (!tokens.MoveNext())
                throw new InvalidDataException("KeyValues: a block does not close");
            depth += tokens.Current == "{" ? 1 : tokens.Current == "}" ? -1 : 0;
        }
    }

    /// <summary>Quoted strings (the quotes removed; a quoted "{" stays a string, see below), braces, and bare words.</summary>
    private static IEnumerable<string> Tokens(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
                i++;
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                i = text.IndexOf('\n', i) is int end && end >= 0 ? end : text.Length;
            else if (c == '{' || c == '}')
            {
                i++;
                yield return c.ToString();
            }
            else if (c == '"')
            {
                int end = text.IndexOf('"', i + 1);
                if (end < 0)
                    throw new InvalidDataException("KeyValues: unterminated string");
                string value = text.Substring(i + 1, end - i - 1);
                i = end + 1;
                yield return value == "{" || value == "}" ? " " + value : value;   // never mistaken for a brace
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"' && text[i] != '{' && text[i] != '}')
                    i++;
                yield return text.Substring(start, i - start);
            }
        }
    }
}
