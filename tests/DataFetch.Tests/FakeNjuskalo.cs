using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using DataFetch.Browser;

namespace DataFetch.Tests;

/// <summary>
/// In-memory Njuškalo look-alike: search pages (25 ads + a promoted ad, a banner and a "latest ads" sidebar)
/// and ad pages (dt/dd details, seller name and phone numbers that must not end up in the results).
/// The markup follows the real pages as seen with --probe; it is synthetic, not copied.
/// </summary>
public sealed class FakeNjuskaloSite
{
    public sealed record Ad(long Id, double? Lat, double? Lon, string? Land, string Title, int Area, int Price, string Location, bool CadastralInText);

    public const int PerPage = 25;
    public List<Ad> Ads { get; } = new();
    public bool OldLayout { get; set; }
    public bool RepeatLastPage { get; set; }

    public FakeNjuskaloSite()
    {
        for (var i = 0; i < 125; i++)
        {
            var kind = i % 10;
            double? lat = kind < 8 ? 45.75 + (i * 0.0013) % 0.15 : null;
            double? lon = kind < 8 ? 15.85 + (i * 0.0029) % 0.30 : null;
            Ads.Add(new Ad(40000000 + i, lat, lon,
                Land: i % 25 == 3 ? "Poljoprivredno" : i % 25 is 13 or 18 ? null : "Građevinsko",
                Title: i % 25 == 18 ? "Zemljište" : "Građevinsko zemljište",
                Area: 300 + (i * 211) % 2700, Price: (20 + (i * 37) % 380) * 1000,
                Location: i % 25 == 8 ? "Zagrebačka, Samobor, Bregana" : i % 2 == 1 ? "Grad Zagreb, Sesvete, Kraljevec" : "Grad Zagreb, Novi Zagreb - zapad, Lučko",
                CadastralInText: i % 4 == 0));
        }
    }

    /// <summary>New ads appear at the top of the newest-first search.</summary>
    public void AddNewAds(int n)
    {
        var start = Ads.Count;
        for (var k = 0; k < n; k++)
            Ads.Insert(0, new Ad(50000000 + start + k, 45.80, 15.95, "Građevinsko", "Građevinsko zemljište", 800, 120000, "Grad Zagreb, Stenjevec", false));
    }

    public (int Status, string Html) Get(string url)
    {
        var u = new Uri(url);
        if (u.AbsolutePath.StartsWith("/blocked"))
            return (403, "<html><head><title>Access Denied</title></head><body>Access Denied. Please verify you are human (captcha).</body></html>");
        if (u.AbsolutePath.StartsWith("/prodaja-zemljista"))
        {
            var page = int.Parse(HttpUtility.ParseQueryString(u.Query)["page"] ?? "1");
            var last = (Ads.Count + PerPage - 1) / PerPage;
            if (RepeatLastPage && page > last) page = last;
            return (200, SearchPage(page));
        }
        var m = Regex.Match(u.AbsolutePath, @"-oglas-(\d+)$");
        if (m.Success && Ads.FirstOrDefault(a => a.Id == long.Parse(m.Groups[1].Value)) is { } ad)
            return (200, AdPage(ad));
        return (404, "<html><head><title>Nema oglasa</title></head></html>");
    }

    string SearchPage(int page)
    {
        var ads = Ads.Skip((page - 1) * PerPage).Take(PerPage).ToList();
        static string Item(Ad a, bool promoted) =>
            $"<li class=\"{(promoted ? "listing isPromoted" : "listing")}\"><article class=\"container\"><h3 class=\"title\">" +
            $"<a class=\"link\" href=\"/nekretnine/gradevinsko-zemljiste-zagreb-oglas-{a.Id}\">Zemljište</a></h3>" +
            $"<div class=\"price\">{a.Price} €</div><ul class=\"features\"><li class=\"feature map\">karta</li></ul></article></li>";
        var promo = ads.Count > 0 ? Item(Ads[1], true) : "";
        var items = string.Concat(ads.Select(a => Item(a, false)));
        var sidebar = "<ul class=\"EntityList EntityList--Latest\">" +
                      string.Concat(Enumerable.Range(0, 6).Select(k => $"<li class=\"EntityList-item\"><a href=\"/nekretnine/stan-split-oglas-{90000000 + k}\">Stan</a></li>")) + "</ul>";
        var html = "<html><head><title>Prodaja zemljišta</title></head><body><nav><a href=\"/nekretnine\">Nekretnine</a></nav>" +
                   $"<ul class=\"listings\">{promo}<li class=\"listingsBanner BannerAlignment\">reklama</li>{items}</ul>{sidebar}</body></html>";
        if (OldLayout)
            html = html.Replace("<ul class=\"listings\">", "<div class=\"results\">")
                       .Replace("class=\"listing isPromoted\"", "class=\"item\"").Replace("class=\"listing\"", "class=\"item\"");
        return html;
    }

    static string AdPage(Ad a)
    {
        var inv = CultureInfo.InvariantCulture;
        var script = a.Lat is double lat
            ? $"<script>var x={{\"location\":{{\"latitude\":{lat.ToString("0.000000", inv)},\"longitude\":{a.Lon!.Value.ToString("0.000000", inv)}}},\"isApproximateLocationOnMap\":true}};</script>"
            : "";
        var text = a.CadastralInText
            ? "<p>Zemljište k.č. 1234/5 k.o. Sesvete, svi papiri uredni.</p><div class=\"inner\">Struja i voda na parceli.</div><p>Info: 091 123 4567, prodaja@primjer.hr</p>"
            : "<p>Lijepo zemljište, mirna lokacija.</p>";
        var price = a.Price.ToString("N0", inv).Replace(",", ".");
        var land = a.Land is null ? "" : $"<dt><span>Tip zemljišta</span></dt><dd>{a.Land}</dd>";
        return $"""
            <html><head><title>Oglas {a.Id}</title></head><body>
              <h1 class="ClassifiedDetailSummary-title">{a.Title} {a.Area} m²</h1>
              <dl><dt>Cijena</dt><dd class="ClassifiedDetailSummary-priceDomestic">{price} €</dd></dl>
              <dl class="ClassifiedDetailBasicDetails-list">
                <dt><span>Lokacija</span></dt><dd><span>{a.Location}</span></dd>
                {land}
                <dt><span>Površina</span></dt><dd>{a.Area},00 m²</dd>
                <dt>Pristup</dt><dd>Asfaltirani put</dd>
                <dt>Prodavatelj</dt><dd>Ivan Horvat</dd>
                <dt>Kontakt</dt><dd>091 123 4567</dd>
              </dl>
              <div class="ClassifiedDetailDescription-text">{text}</div>
              <div class="seller">Prodavatelj: Ivan Horvat, tel 091 123 4567</div>
              <ul><li>Oglas objavljen: 12.10.2026. u 10:15</li></ul>{script}</body></html>
            """;
    }
}

/// <summary>Stands in for the Playwright browser window: serves the fake site.</summary>
public sealed class FakeBrowser(FakeNjuskaloSite site) : IPageBrowser
{
    PageResult _current = new("", null, "", "");
    public int PagesOpened { get; private set; }
    public bool IsVisible => false;

    public Task<PageResult> OpenAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        PagesOpened++;
        var (status, html) = site.Get(url);
        var title = Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value;
        return Task.FromResult(_current = new PageResult(url, status, title, html));
    }

    public Task<PageResult> CurrentAsync(CancellationToken ct) => Task.FromResult(_current);
    public Task ScrollThroughAsync(CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
