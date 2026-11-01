using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;

namespace DataFetch.Parsing;

public sealed record AdLink(string Id, string Url);

public sealed class ParsedAd
{
    public string? Title { get; set; }
    public decimal? PriceEur { get; set; }
    public decimal? AreaM2 { get; set; }
    public string? LandType { get; set; }
    public string? LocationText { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>Njuškalo's own flag: true when the map pin only shows the approximate area.</summary>
    public bool? LocationApproximate { get; set; }
    public string? CadastralParcel { get; set; }
    public string? CadastralMunicipality { get; set; }
    public string? PublishedText { get; set; }
    public List<(string Label, string Value)> Details { get; set; } = new();
    /// <summary>All labelled fields as shown on the page (seller/contact fields left out, phones/e-mails masked).</summary>
    public Dictionary<string, string> RawDetails { get; set; } = new();
    public string? Description { get; set; }

    /// <summary>An ad page is usable when it has a title and at least a price or an area.</summary>
    public bool LooksValid => !string.IsNullOrWhiteSpace(Title) && (PriceEur is not null || AreaM2 is not null);
}

/// <summary>
/// Reads Njuškalo search and ad pages. Extraction is deliberately tolerant (labels and patterns
/// rather than exact CSS classes); run the app with --probe to check it against the live pages.
/// </summary>
public static partial class NjuskaloParser
{
    const string Host = "https://www.njuskalo.hr";

    // Ad URLs end in "-oglas-<number>", e.g. /nekretnine/gradevinsko-zemljiste-...-oglas-41234567
    [GeneratedRegex(@"href=""(?<href>(?:https?://(?:www\.)?njuskalo\.hr)?/[^""?#\s]*-oglas-(?<id>\d{5,}))(?:[?#][^""]*)?""", RegexOptions.IgnoreCase)]
    private static partial Regex AdHref();

    // One search result: <li class="listing"> or <li class="listing isPromoted"> (not banners, not the "latest ads" sidebar)
    [GeneratedRegex(@"<li\b[^>]*\bclass=""listing(?:\s[^""]*)?""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ResultItem();

    [GeneratedRegex(@"<ul\b[^>]*\bclass=""listings(?:\s[^""]*)?""", RegexOptions.IgnoreCase)]
    private static partial Regex ResultList();

    [GeneratedRegex(@"""isApproximateLocationOnMap""\s*:\s*(?<v>true|false)", RegexOptions.IgnoreCase)]
    private static partial Regex ApproximateFlag();

    [GeneratedRegex(@"<h1\b[^>]*>(?<t>.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex H1();

    [GeneratedRegex(@"class=""[^""]*price[^""]*""[^>]*>(?<p>.*?)</", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PriceElement();

    // "85.000 €", "85 000 EUR", "1.250,50 €" – but not "130 €/m²"
    [GeneratedRegex(@"(?<n>\d{1,3}(?:[.\s ]\d{3})+|\d+)(?:,(?<d>\d{1,2}))?\s*(?:€|EUR)(?!\s*/)", RegexOptions.IgnoreCase)]
    private static partial Regex EuroAmount();

    [GeneratedRegex(@"površina[^0-9\n]{0,25}(?<a>\d[\d.,\s]*)\s*m(?:2|²)", RegexOptions.IgnoreCase)]
    private static partial Regex AreaInText();

    // Coordinates in embedded JSON, e.g. "lat":45.81,"lng":15.97
    [GeneratedRegex(@"""(?:lat|latitude)""\s*:\s*""?(?<lat>4[5-6]\.\d{3,})""?\s*,\s*""(?:lng|lon|longitude)""\s*:\s*""?(?<lon>1[5-6]\.\d{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex JsonLatLon();

    [GeneratedRegex(@"\b(?:data-)?lat(?:itude)?[""']?\s*[:=]\s*[""']?(?<v>4[5-6]\.\d{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex LatAttr();

    [GeneratedRegex(@"\b(?:data-)?(?:lng|lon|longitude)[""']?\s*[:=]\s*[""']?(?<v>1[5-6]\.\d{3,})", RegexOptions.IgnoreCase)]
    private static partial Regex LonAttr();

    // "k.č. 1234/5", "kč br. 123", "kat. čest. 456", "katastarska čestica br. 789/1"
    [GeneratedRegex(@"(?:\bk\.?\s*č\.?(?:\s*br\.?)?|kat(?:astarsk\w*)?\.?\s*čest(?:ic\w*)?\.?(?:\s*br\.?)?)\s*:?\s*(?<kc>\d+(?:/\d+)?(?:\s*(?:,|i)\s*\d+(?:/\d+)?)*)", RegexOptions.IgnoreCase)]
    private static partial Regex ParcelNumber();

    [GeneratedRegex(@"\bk\.?\s*o\.?\s+(?<ko>[A-ZČĆŽŠĐ][\p{L}]+(?:\s+[A-ZČĆŽŠĐ][\p{L}]+)?)")]
    private static partial Regex CadastralMunicipalityName();

    [GeneratedRegex(@"oglas objavljen[:\s]*(?<d>\d{1,2}\.\d{1,2}\.\d{4}\.?(?:\s*u\s*\d{1,2}:\d{2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedInText();

    /// <summary>Ad links of the search results on a page, in page order, without duplicates.</summary>
    public static List<AdLink> AdLinks(string html) => AdLinksWithSource(html).Links;

    /// <summary>
    /// Ad links taken from the result items (li.listing). A page with an empty result list returns none
    /// (end of results). Only when the page has no result list at all (a changed layout) are all ad
    /// links on the page used – FromResultList is then false.
    /// </summary>
    public static (List<AdLink> Links, bool FromResultList) AdLinksWithSource(string html)
    {
        var items = ResultItem().Matches(html);
        if (items.Count > 0 || ResultList().IsMatch(html))
        {
            var links = new List<AdLink>();
            var seen = new HashSet<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var start = items[i].Index;
                var end = i + 1 < items.Count ? items[i + 1].Index : Math.Min(html.Length, start + 30_000);
                var m = AdHref().Match(html, start, end - start);
                if (m.Success && seen.Add(m.Groups["id"].Value)) links.Add(ToLink(m));
            }
            return (links, true);
        }

        var all = new List<AdLink>();
        var ids = new HashSet<string>();
        foreach (Match m in AdHref().Matches(html))
            if (ids.Add(m.Groups["id"].Value)) all.Add(ToLink(m));
        return (all, false);
    }

    static AdLink ToLink(Match m)
    {
        var href = m.Groups["href"].Value;
        return new AdLink(m.Groups["id"].Value, href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Host + href);
    }

    /// <summary>The search URL for a given result page (Njuškalo uses ?page=N).</summary>
    public static string WithPage(string searchUrl, int page)
    {
        var b = new UriBuilder(searchUrl);
        var q = HttpUtility.ParseQueryString(b.Query);
        if (page <= 1) q.Remove("page"); else q["page"] = page.ToString(CultureInfo.InvariantCulture);
        b.Query = q.ToString() ?? "";
        return b.Uri.ToString();
    }

    public static ParsedAd ParseAd(string html)
    {
        var ad = new ParsedAd();
        var text = HtmlText.Visible(html);
        ad.Details = HtmlText.LabelledValues(html);
        string? Detail(params string[] keys) =>
            ad.Details.FirstOrDefault(d => keys.Any(k => d.Label.Contains(k))).Value is { Length: > 0 } v ? v : null;

        var h1 = H1().Match(html);
        ad.Title = h1.Success ? HtmlText.Clean(h1.Groups["t"].Value) : null;

        ad.LocationText = Detail("lokacija", "adresa");
        ad.LandType = Detail("vrsta zemljišta", "vrsta zemljista", "tip zemljišta", "tip zemljista");
        ad.AreaM2 = HtmlText.ParseNumber(Detail("površina", "povrsina"));
        if (ad.AreaM2 is null && AreaInText().Match(text) is { Success: true } am) ad.AreaM2 = HtmlText.ParseNumber(am.Groups["a"].Value);

        ad.PriceEur = ParsePrice(Detail("cijena")) ?? FirstPrice(html) ?? ParsePrice(text);

        (ad.Lat, ad.Lon) = Coordinates(html);
        if (ad.Lat is not null && ApproximateFlag().Match(html) is { Success: true } ap)
            ad.LocationApproximate = ap.Groups["v"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

        var kc = ParcelNumber().Match(text);
        if (kc.Success) ad.CadastralParcel = kc.Groups["kc"].Value.Trim();
        var ko = CadastralMunicipalityName().Match(text);
        if (ko.Success) ad.CadastralMunicipality = ko.Groups["ko"].Value.Trim();

        foreach (var (label, value) in HtmlText.LabelledValuesAsShown(html))
        {
            if (label.Length == 0 || IsSellerField(label)) continue;
            var key = label;
            for (var i = 2; ad.RawDetails.ContainsKey(key); i++) key = $"{label} ({i})";
            ad.RawDetails[key] = MaskContacts(value);
        }

        var desc = HtmlText.ElementByClass(html, "Description-text") ?? HtmlText.ElementByClass(html, "description");
        if (desc is not null && HtmlText.Visible(desc) is { Length: > 0 } d) ad.Description = MaskContacts(d);

        ad.PublishedText = Detail("oglas objavljen", "objavljen");
        if (ad.PublishedText is null && PublishedInText().Match(text) is { Success: true } pm) ad.PublishedText = pm.Groups["d"].Value;
        return ad;
    }

    static readonly string[] SellerLabels = { "prodava", "oglašivač", "oglasivac", "kontakt", "telefon", "mobitel", "e-mail", "email", "ime i prezime" };
    static bool IsSellerField(string label) => SellerLabels.Any(s => label.Contains(s, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"(?<!\d)(?:(?:\+|00)\s*385[\s/.\-]*\(?0?\)?\s*|0)\d{1,2}[\s/.\-]*\d{3,4}[\s/.\-]*\d{3,4}(?!\d)")]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+")]
    private static partial Regex Email();

    /// <summary>Replaces phone numbers and e-mail addresses with placeholders.</summary>
    public static string MaskContacts(string text) => Email().Replace(PhoneNumber().Replace(text, "[phone]"), "[e-mail]");

    static decimal? FirstPrice(string html)
    {
        foreach (Match m in PriceElement().Matches(html))
            if (ParsePrice(HtmlText.Clean(m.Groups["p"].Value)) is { } p) return p;
        return null;
    }

    static decimal? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = EuroAmount().Match(text);
        if (!m.Success) return null;
        var n = Regex.Replace(m.Groups["n"].Value, @"[.\s ]", "");
        var s = m.Groups["d"].Success ? $"{n}.{m.Groups["d"].Value}" : n;
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    static (double?, double?) Coordinates(string html)
    {
        static bool InZagrebArea(double lat, double lon) => lat is > 45.6 and < 46.05 && lon is > 15.7 and < 16.35;
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        foreach (Match m in JsonLatLon().Matches(html))
        {
            double lat = D(m.Groups["lat"].Value), lon = D(m.Groups["lon"].Value);
            if (InZagrebArea(lat, lon)) return (lat, lon);
        }
        var la = LatAttr().Match(html);
        var lo = LonAttr().Match(html);
        if (la.Success && lo.Success)
        {
            double lat = D(la.Groups["v"].Value), lon = D(lo.Groups["v"].Value);
            if (InZagrebArea(lat, lon)) return (lat, lon);
        }
        return (null, null);
    }

    static readonly string[] BlockMarkers =
    {
        "captcha", "access denied", "pristup odbijen", "verify you are human", "are you a robot", "not a robot",
        "pardon our interruption", "request unsuccessful", "unusual traffic", "neuobičajen", "provjera", "blocked",
    };

    static string? BlockMarker(Browser.PageResult page)
    {
        var t = (page.Title + "\n" + HtmlText.Visible(page.Html)).ToLowerInvariant();
        if (t.Length > 4000) t = t[..4000];
        return BlockMarkers.FirstOrDefault(t.Contains);
    }

    /// <summary>True when a page without the expected content looks like a block or check page rather than an empty result.</summary>
    public static bool LooksBlocked(Browser.PageResult page) => page.Status is 403 or 429 or 503 || BlockMarker(page) is not null;

    /// <summary>Describes why a page looks like a block or check page (only called when expected content is missing).</summary>
    public static string DescribeProblem(Browser.PageResult page)
    {
        if (page.Status is 403 or 429 or 503) return $"Njuškalo answered HTTP {page.Status}.";
        var marker = BlockMarker(page);
        return marker is null
            ? $"The page did not contain the expected content (title: \"{page.Title}\")."
            : $"The page looks like a check or block page (contains \"{marker}\").";
    }
}
