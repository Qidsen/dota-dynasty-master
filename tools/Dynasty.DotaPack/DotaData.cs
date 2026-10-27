using System.Globalization;
using System.Text.Json.Nodes;

/// <summary>Source data with a local cache: dotaconstants at a pinned commit, OpenDota API responses.</summary>
sealed class DotaData
{
    public const string Commit = "5c0242df9057848fa2036ca3fafe9f2bd4508163";   // dotaconstants, 2026-10-12, verified against patch 7.41f

    private readonly string _cache;
    private readonly HttpClient _http = new HttpClient();
    private DateTime _lastOpenDotaCall = DateTime.MinValue;

    public DotaData(string cache)
    {
        _cache = cache;
        Directory.CreateDirectory(Path.Combine(cache, "opendota"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("dota-dynasty-pack/1.0");
    }

    /// <summary>A dotaconstants build file (heroes, items, abilities, …).</summary>
    public JsonNode Constants(string name) =>
        Cached(Path.Combine(_cache, name + ".json"), $"https://raw.githubusercontent.com/odota/dotaconstants/{Commit}/build/{name}.json", throttle: false);

    /// <summary>The game's own script files (SteamDatabase/GameTracking-Dota2) at the latest build after patch 7.41f (2026-09-15).</summary>
    public const string GameFilesCommit = "282fad29f0f74504fad465c52d197fb61f4d612b";   // 2026-10-15, build 6962: the latest; builds after 7.41f (6951, 6960, 6962) changed only cosmetic slots in hero files

    /// <summary>Unit definitions from the game's npc_units.txt (summons: base stats an ability's values do not set).</summary>
    public Dictionary<string, Dictionary<string, string>> GameUnits()
    {
        string file = Path.Combine(_cache, "npc_units.txt");
        if (!File.Exists(file))
        {
            string url = $"https://raw.githubusercontent.com/SteamDatabase/GameTracking-Dota2/{GameFilesCommit}/game/dota/pak01_dir/scripts/npc/npc_units.txt";
            Console.WriteLine("download " + url);
            File.WriteAllText(file, _http.GetStringAsync(url).GetAwaiter().GetResult());
        }

        return KeyValues.Entries(File.ReadAllText(file));
    }

    /// <summary>
    /// Item definitions from the game's items.txt (same pinned commit): what dotaconstants lacks, such as an item's
    /// cast range (AbilityCastRange) and the level of a leveled item (ItemBaseLevel: Dagon 2–5).
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> GameItems()
    {
        string file = Path.Combine(_cache, "npc_items.txt");
        if (!File.Exists(file))
        {
            string url = $"https://raw.githubusercontent.com/SteamDatabase/GameTracking-Dota2/{GameFilesCommit}/game/dota/pak01_dir/scripts/npc/items.txt";
            Console.WriteLine("download " + url);
            File.WriteAllText(file, _http.GetStringAsync(url).GetAwaiter().GetResult());
        }

        return KeyValues.Entries(File.ReadAllText(file));
    }

    /// <summary>A hero's file of the game (its abilities' values with talent, scepter and shard links, Valve's bot build); null if the game has none.</summary>
    public System.Text.Json.Nodes.JsonObject? GameHero(string heroName) => GameFile("heroes/" + heroName + ".txt", "npc_heroes/" + heroName + ".txt");

    /// <summary>The game's npc_abilities.txt (the generic talents' values).</summary>
    public System.Text.Json.Nodes.JsonObject GameAbilities() => GameFile("npc_abilities.txt", "npc_abilities.txt")!;

    private System.Text.Json.Nodes.JsonObject? GameFile(string path, string cacheName)
    {
        string file = Path.Combine(_cache, cacheName);
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string url = $"https://raw.githubusercontent.com/SteamDatabase/GameTracking-Dota2/{GameFilesCommit}/game/dota/pak01_dir/scripts/npc/{path}";
            Console.WriteLine("download " + url);
            using HttpResponseMessage response = _http.GetAsync(url).GetAwaiter().GetResult();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            File.WriteAllText(file, response.EnsureSuccessStatusCode().Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return KeyValues.Tree(File.ReadAllText(file));
    }

    /// <summary>Rows of an SQL query on OpenDota's public explorer (parsed pro matches), cached under <paramref name="name"/>.</summary>
    public JsonArray Explorer(string name, string sql)
    {
        string file = Path.Combine(_cache, "opendota", "explorer_" + name + ".json");
        if (!File.Exists(file))
        {
            Console.WriteLine("query OpenDota explorer: " + name);
            string url = "https://api.opendota.com/api/explorer?sql=" + Uri.EscapeDataString(sql);
            File.WriteAllText(file, _http.GetStringAsync(url).GetAwaiter().GetResult());
        }

        JsonNode result = JsonNode.Parse(File.ReadAllText(file))!;
        if (result["err"] is JsonNode error && error.GetValueKind() != System.Text.Json.JsonValueKind.Null)
            throw new InvalidDataException("OpenDota explorer " + name + ": " + error.ToJsonString());
        return result["rows"]!.AsArray();
    }

    /// <summary>
    /// A STRATZ GraphQL response, cached under <paramref name="name"/>; null without a token. The token is read from
    /// packs/stratz.token (git-ignored, never in the pack or the repository).
    /// </summary>
    public JsonNode? Stratz(string name, string query, string tokenFile)
    {
        string file = Path.Combine(_cache, "stratz", name + ".json");
        if (!File.Exists(file))
        {
            if (!File.Exists(tokenFile))
                return null;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            Console.WriteLine("query STRATZ: " + name);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.stratz.com/graphql");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", File.ReadAllText(tokenFile).Trim());
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd("STRATZ_API");
            request.Content = new StringContent(new JsonObject { ["query"] = query }.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            using HttpResponseMessage response = _http.Send(request);
            string body = response.EnsureSuccessStatusCode().Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (JsonNode.Parse(body)?["errors"] is JsonNode errors)
                throw new InvalidDataException("STRATZ " + name + ": " + errors.ToJsonString());
            File.WriteAllText(file, body);
        }

        return JsonNode.Parse(File.ReadAllText(file))!["data"];
    }

    /// <summary>An OpenDota API response (free tier: at most one call per second).</summary>
    public JsonNode OpenDota(string path) =>
        Cached(Path.Combine(_cache, "opendota", path.Replace('/', '_') + ".json"), "https://api.opendota.com/api/" + path, throttle: true);

    private JsonNode Cached(string file, string url, bool throttle)
    {
        if (!File.Exists(file))
        {
            if (throttle)
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - _lastOpenDotaCall);
                if (wait > TimeSpan.Zero)
                    Thread.Sleep(wait);
                _lastOpenDotaCall = DateTime.UtcNow;
            }

            Console.WriteLine("download " + url);
            File.WriteAllText(file, _http.GetStringAsync(url).GetAwaiter().GetResult());
        }

        return JsonNode.Parse(File.ReadAllText(file))!;
    }

    /// <summary>
    /// Names of the items sold in the shop now (Liquipedia «Items», shop sections up to «Non-purchasable
    /// Items»): dotaconstants keeps removed items (Iron Talon, Necronomicon) without marking them.
    /// </summary>
    public HashSet<string> ShopItemNames()
    {
        string file = Path.Combine(_cache, "liquipedia_items.json");
        if (!File.Exists(file))
        {
            Console.WriteLine("download Liquipedia Items");
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://liquipedia.net/dota2/api.php?action=parse&page=Items&prop=text&format=json&redirects=1");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 dota-dynasty-pack/1.0");
            request.Headers.AcceptEncoding.ParseAdd("gzip");
            using HttpResponseMessage response = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip }).Send(request);
            File.WriteAllText(file, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        string page = JsonNode.Parse(File.ReadAllText(file))!["parse"]!["text"]!["*"]!.GetValue<string>();
        int start = page.IndexOf("id=\"Basics_Items\"", StringComparison.Ordinal);
        int end = page.IndexOf("id=\"Non-purchasable_Items\"", StringComparison.Ordinal);
        if (start < 0 || end < start)
            throw new InvalidDataException("Liquipedia «Items» page changed: shop sections not found.");
        var names = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(page[start..end], @"title=""([^""(]+?)"">[^<]+</a>&#160;\(")
            .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)));
        if (names.Count < 150)
            throw new InvalidDataException("Liquipedia «Items» page changed: only " + names.Count + " shop items found.");
        return names;
    }

    /// <summary>A number from the data (sometimes written as a string) as an exact JSON number.</summary>
    public static JsonNode Number(JsonNode? value) => value is JsonValue v && v.TryGetValue(out string? s)
        ? JsonValue.Create(decimal.Parse(s.Trim().TrimEnd('%'), CultureInfo.InvariantCulture))!
        : JsonValue.Create(value!.GetValue<decimal>())!;

    public static decimal Decimal(JsonNode? value) => Number(value).GetValue<decimal>();
}
