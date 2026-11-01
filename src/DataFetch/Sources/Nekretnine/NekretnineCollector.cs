using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DataFetch.Parsing;

namespace DataFetch.Sources.Nekretnine;

/// <summary>
/// Reads the nekretnine.hr listing API page by page (25 ads per page) until the end of the search,
/// keeps building land in Grad Zagreb, and stops at the first error or bot-check answer – no retries.
/// </summary>
public sealed class NekretnineCollector
{
    static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    // The site's own utility labels (Italian) → Croatian
    static readonly Dictionary<string, string> UtilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["allaccio elettricità"] = "struja",
        ["allaccio acqua"] = "voda",
        ["allaccio fogne"] = "kanalizacija",
        ["allaccio gas"] = "plin",
        ["allaccio telefonico"] = "telefon",
    };

    readonly HttpClient _http;
    readonly NekretnineSettings _s;

    public NekretnineCollector(HttpClient http, NekretnineSettings settings) { _http = http; _s = settings; }

    /// <param name="handler">Only for tests (an in-memory fake API); normally null.</param>
    public static HttpClient CreateHttpClient(HttpMessageHandler? handler = null)
    {
        var http = new HttpClient(handler ?? new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DataFetch/1.0 (personal research)");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("hr");
        return http;
    }

    /// <summary>The configured API address with pag=N (the rest of the address is kept exactly as written).</summary>
    public string PageUrl(int page)
    {
        var url = Regex.Replace(_s.ApiUrl, @"([?&])pag=[^&#]*&?", "$1").TrimEnd('&', '?');
        return url + (url.Contains('?') ? "&" : "?") + "pag=" + page.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Fetches one result page. Anything other than a JSON answer stops the run.</summary>
    public async Task<JsonNode> FetchPageAsync(int page, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(PageUrl(page), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var botCheck = body.Contains("captcha-delivery", StringComparison.OrdinalIgnoreCase) || body.Contains("datadome", StringComparison.OrdinalIgnoreCase);
        if (!resp.IsSuccessStatusCode)
            throw new BlockedException($"nekretnine.hr answered HTTP {(int)resp.StatusCode}{(botCheck ? " with a bot check (DataDome)" : "")} on page {page}");
        var media = resp.Content.Headers.ContentType?.MediaType;
        if (media is null || !media.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var title = Regex.Match(body, "<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            throw new BlockedException($"page {page}: expected JSON but got {media ?? "no content type"}{(title.Length > 0 ? $" (\"{title}\")" : "")}{(botCheck ? " – a bot check (DataDome)" : "")}");
        }
        try { return JsonNode.Parse(body) ?? throw new BlockedException($"page {page}: empty answer"); }
        catch (JsonException ex) { throw new BlockedException($"page {page}: answer was not valid JSON ({ex.Message})"); }
    }

    // ---------------------------------------------------------------- collect / dry run
    public async Task<(NekretnineRun Run, List<NekretnineListing> Matching)> CollectAsync(NekretnineStore store, bool dryRun, CancellationToken ct)
    {
        var run = new NekretnineRun { StartedAt = DateTimeOffset.Now };
        var matching = new List<NekretnineListing>();
        var seen = new HashSet<long>();
        var reachedEnd = false;

        try
        {
            for (var page = 1; page <= _s.MaxPages; page++)
            {
                if (page > 1 && _s.DelayMs > 0) await Task.Delay(_s.DelayMs, ct);
                var root = await FetchPageAsync(page, ct);
                var results = root["results"] as JsonArray;
                if (results is null || results.Count == 0) { reachedEnd = true; run.StopReason = "reached the end of the search"; break; }

                run.PagesRead++;
                run.ApiTotal ??= Int(root["count"]);
                var maxPages = Int(root["maxPages"]);
                if (page == 1 && Bool(root["isResultsLimitReached"]) == true)
                    Ui.Warn("  Note: the site reports that the result limit is reached – not every ad may be reachable through this search.");

                var now = DateTimeOffset.Now;
                int onPage = 0, fresh = 0, changed = 0;
                foreach (var item in results)
                {
                    if (item is null) continue;
                    var l = Map(item);
                    if (l is null) continue;
                    if (!Matches(l)) { run.OtherTypes++; continue; }
                    if (!seen.Add(l.Id)) continue;          // the same ad twice in one scan
                    onPage++; run.Matching++;
                    matching.Add(l);
                    if (dryRun)
                    {
                        if (!store.Contains(l.Id)) { fresh++; run.New++; }
                        continue;
                    }
                    switch (store.Upsert(l, now))
                    {
                        case UpsertResult.New: fresh++; run.New++; break;
                        case UpsertResult.PriceChanged: changed++; run.PriceChanged++; break;
                        case UpsertResult.Reappeared: run.Reappeared++; break;
                    }
                }
                var of = maxPages is int mp ? $"/{mp}" : "";
                Ui.Info($"  page {page,2}{of}: {results.Count} ads, {onPage} building land in Zagreb ({fresh} new{(changed > 0 ? $", {changed} price change{(changed == 1 ? "" : "s")}" : "")})");
                if (!dryRun) store.Save();

                if (maxPages is int last && page >= last) { reachedEnd = true; run.StopReason = "reached the end of the search"; break; }
                if (page == _s.MaxPages) run.StopReason = $"reached MaxPages ({_s.MaxPages}) before the end of the search";
            }
        }
        catch (OperationCanceledException) { run.StopReason = "stopped by you (Ctrl+C)"; }
        catch (BlockedException ex) { run.StopReason = ex.Message; }
        catch (HttpRequestException ex) { run.StopReason = "network error: " + ex.Message; }

        run.Complete = reachedEnd;
        run.FinishedAt = DateTimeOffset.Now;
        if (!dryRun)
        {
            // Only a complete scan can tell which ads are gone
            if (reachedEnd)
            {
                run.Removed = store.MarkRemoved(seen, run.FinishedAt);
                store.Document.LastCompleteScan = run.FinishedAt;
            }
            store.Document.Runs.Add(run);
            store.Save();
        }
        return (run, matching);
    }

    // ---------------------------------------------------------------- probe
    public async Task ProbeAsync(string snapshotFolder, CancellationToken ct)
    {
        Ui.Step($"\nProbe – first page of the listing API:\n  {PageUrl(1)}");
        JsonNode root;
        try { root = await FetchPageAsync(1, ct); }
        catch (BlockedException ex) { Ui.Error($"  {ex.Message}"); return; }

        var results = root["results"] as JsonArray ?? new JsonArray();
        Ui.Info($"  total ads in search: {Int(root["count"])}   pages: {Int(root["maxPages"])}   on this page: {results.Count}");
        var listings = results.Where(r => r is not null).Select(r => Map(r!)).Where(l => l is not null).Select(l => l!).ToList();
        foreach (var g in listings.GroupBy(l => $"{l.TypologyId} {l.Typology}"))
            Ui.Info($"    {g.Count(),3} × {g.Key}{(_s.TypologyIds.Count == 0 || g.First().TypologyId is int t && _s.TypologyIds.Contains(t) ? "   ← building land (kept if in " + _s.Province + ")" : "")}");

        var first = listings.FirstOrDefault(Matches);
        if (first is not null)
        {
            Ui.Step("\n  First building-land listing:");
            Ui.Info($"    id / url:     {first.Id}  {first.Url}");
            Ui.Info($"    title:        {first.Title}");
            Ui.Info($"    price / area: {Fmt(first.PriceEur)} €  /  {Fmt(first.AreaM2)} m²  ({Fmt(first.PricePerM2)} €/m²)");
            Ui.Info($"    location:     {first.Address} | {first.Macrozone} | {first.Microzone} | {first.Province}");
            Ui.Info($"    coordinates:  {(first.Lat is null ? "(none)" : $"{first.Lat:0.00000}, {first.Lon:0.00000}")}");
            Ui.Info($"    utilities:    {(first.Utilities.Count == 0 ? "(none listed)" : string.Join(", ", first.Utilities))}");
            Ui.Info($"    agency:       {first.AgencyName ?? first.AdvertiserType ?? "-"}");
            Ui.Info($"    description:  {(first.Description is null ? "(empty in the list API)" : first.Description.Length + " characters")}");
        }

        Directory.CreateDirectory(snapshotFolder);
        var file = Path.Combine(snapshotFolder, "nekretnine-page1.json");
        Sanitize(root);
        await File.WriteAllTextAsync(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), ct);
        Ui.Ok($"\n  Snapshot of page 1 (agent names and phone numbers removed): {file}");
    }

    // ---------------------------------------------------------------- mapping
    bool Matches(NekretnineListing l) =>
        (_s.TypologyIds.Count == 0 || (l.TypologyId is int t && _s.TypologyIds.Contains(t))) &&
        (string.IsNullOrWhiteSpace(_s.Province) || l.Province is null || l.Province.Equals(_s.Province, StringComparison.OrdinalIgnoreCase));

    public static NekretnineListing? Map(JsonNode item)
    {
        var re = item["realEstate"];
        var id = Long(re?["id"]);
        if (re is null || id is null) return null;
        var p = (re["properties"] as JsonArray)?.FirstOrDefault();
        var loc = p?["location"];
        var agency = re["advertiser"]?["agency"];

        var price = Dec(re["price"]?["value"]) ?? Dec(p?["price"]?["value"]);
        if (Bool(re["price"]?["visible"]) == false) price = null;
        var surface = Str(p?["surface"]);
        var area = HtmlText.ParseNumber(surface);

        var l = new NekretnineListing
        {
            Id = id.Value,
            Uuid = Str(re["uuid"]),
            Url = Str(item["seo"]?["url"]),
            Title = Str(re["title"]),
            Caption = Str(p?["caption"]),
            TypologyId = Int(re["typology"]?["id"]) ?? Int(p?["typology"]?["id"]),
            Typology = Str(re["typology"]?["name"]) ?? Str(p?["typology"]?["name"]),
            PriceEur = price,
            PriceText = Str(re["price"]?["formattedValue"]),
            AreaM2 = area,
            SurfaceText = surface,
            PricePerM2 = price is > 0 && area is > 0 ? Math.Round(price.Value / area.Value, 2) : null,
            Address = Str(loc?["address"]),
            City = Str(loc?["city"]),
            Province = Str(loc?["province"]),
            Macrozone = Str(loc?["macrozone"]),
            Microzone = Str(loc?["microzone"]),
            Lat = Dbl(loc?["latitude"]),
            Lon = Dbl(loc?["longitude"]),
            Description = Str(p?["description"]) is { Length: > 0 } d ? NjuskaloParser.MaskContacts(d) : null,
            PhotoCount = (p?["multimedia"]?["photos"] as JsonArray)?.Count,
            AdvertiserType = Str(agency?["type"]),
            AgencyId = Long(agency?["id"]),
            AgencyName = Str(agency?["displayName"]),
            AgencyUrl = Str(agency?["agencyUrl"]),
            Visibility = Str(re["visibility"]),
            IsNew = Bool(re["isNew"]),
        };
        if (p?["ga4features"] is JsonArray features)
            foreach (var f in features)
                if (Str(f) is { Length: > 0 } name) l.Utilities.Add(UtilityNames.TryGetValue(name, out var hr) ? hr : name);

        var raw = item.DeepClone().AsObject();
        Sanitize(raw);
        l.Raw = raw;
        return l;
    }

    /// <summary>Removes agent/contact personal data (supervisor block, phone lists) from an API record.</summary>
    public static void Sanitize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (key is "supervisor" or "phones" or "agentPhone" or "email") { o.Remove(key); continue; }
                    if (key == "description" && o[key] is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        o[key] = NjuskaloParser.MaskContacts(text);
                        continue;
                    }
                    Sanitize(o[key]);
                }
                break;
            case JsonArray a:
                foreach (var x in a) Sanitize(x);
                break;
        }
    }

    static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
    static long? Long(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<long>(out var x) ? x : (v.TryGetValue<string>(out var s) && long.TryParse(s, out var y) ? y : null)) : null;
    static int? Int(JsonNode? n) => Long(n) is long x ? (int)x : null;
    static decimal? Dec(JsonNode? n) => n is JsonValue v && v.TryGetValue<decimal>(out var x) ? x : null;
    static double? Dbl(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var x) ? x : null;
    static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var x) ? x : null;
    static string Fmt(decimal? v) => v?.ToString("N0", Hr) ?? "?";
}
