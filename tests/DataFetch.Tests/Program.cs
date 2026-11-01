using System.Text.Json;
using DataFetch;
using DataFetch.Browser;
using DataFetch.Parsing;
using DataFetch.Sources;
using DataFetch.Sources.Nekretnine;
using DataFetch.Tests;

// DataFetch tests – run with `dotnet run` in tests/DataFetch.Tests. Exit code 0 = all checks passed.
// Every scenario runs in its own temporary folder laid out like the repository (src/DataFetch + data/),
// so the default relative paths from appsettings are exercised too.

Ui.Interactive = false;                      // nobody answers questions: problems must stop the run
var runner = new Runner(args.FirstOrDefault());

// ------------------------------------------------------------------ helpers / parsing
runner.Test("Number parsing (Croatian formats)", t =>
{
    t.Equal(1250.50m, HtmlText.ParseNumber("1.250,50"));
    t.Equal(650m, HtmlText.ParseNumber("650,00 m²"));
    t.Equal(1250m, HtmlText.ParseNumber("1.250"));
    t.Equal(85000m, HtmlText.ParseNumber("85.000 €"));
    t.Equal(1300m, HtmlText.ParseNumber("1.300 m²"));
});

runner.Test("Phone numbers and e-mails are masked, other numbers kept", t =>
{
    var s = NjuskaloParser.MaskContacts("Zovite 091 123 4567 ili +385 98 765 432, 01/2345-678, mail ivan.horvat@gmail.com. k.č. 1234/5, cijena 85.000 €, 650 m2, godina 2024.");
    t.NotContains("123 4567", s); t.NotContains("765 432", s); t.NotContains("2345-678", s); t.NotContains("gmail", s);
    t.Contains("1234/5", s); t.Contains("85.000", s); t.Contains("650 m2", s); t.Contains("2024", s);
});

runner.Test("Njuškalo filter: building land in the City of Zagreb only", t =>
{
    string? Why(string title, string? land, string? loc) => ListingFilter.SkipReason(new Listing { Title = title, LandType = land, LocationText = loc });
    t.Null(Why("Građevinsko zemljište", "Građevinsko", "Grad Zagreb, Sesvete, Kraljevec"));
    t.Null(Why("Građevinsko zemljište", "građevinsko zemljište", "Zagreb, Trnje"));
    t.Null(Why("Građevinsko zemljište 500 m2", null, "Grad Zagreb, Podsljeme"));
    t.NotNull(Why("Građevinsko zemljište", "Građevinsko", "Zagrebačka, Samobor, Bregana"));
    t.NotNull(Why("Zemljište", "Poljoprivredno", "Grad Zagreb, Brezovica"));
    t.NotNull(Why("Zemljište 500 m2", null, "Grad Zagreb, Podsljeme"));
});

runner.Test("Njuškalo paging keeps the other query parameters", t =>
{
    t.Equal("https://www.njuskalo.hr/prodaja-zemljista/zagreb?landType=1&sort=new&page=3", NjuskaloParser.WithPage("https://www.njuskalo.hr/prodaja-zemljista/zagreb?landType=1&sort=new", 3));
    t.Contains("sort=new", NjuskaloParser.WithPage("https://www.njuskalo.hr/prodaja-zemljista/zagreb?page=3&sort=new", 1));
});

runner.Test("nekretnine.hr page URL: adds pag, keeps the configured address as written", t =>
{
    var c = new NekretnineCollector(new HttpClient(), new NekretnineSettings { ApiUrl = "https://x.test/api/?a=1&path=%2Fprodaja%2F&pag=7" });
    t.Equal("https://x.test/api/?a=1&path=%2Fprodaja%2F&pag=2", c.PageUrl(2));
});

// ------------------------------------------------------------------ nekretnine.hr
var template = Path.Combine(AppContext.BaseDirectory, "fixtures", "nekretnine-listing.json");

runner.Test("nekretnine.hr: full scan saves building land in Grad Zagreb only", async t =>
{
    var (o, api) = NekretnineSetup(t, template);
    t.Equal(0, await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default));
    var doc = t.ReadJson("data/listings/listings-nekretnine.json");
    var listings = doc.GetProperty("listings").EnumerateArray().ToList();
    // 60 ads: 15 agricultural (every 4th), 1 in Zagrebačka → 44 kept
    t.Equal(44, listings.Count);
    t.True(listings.All(l => l.GetProperty("typologyId").GetInt32() == 261), "only typology 261");
    t.True(listings.All(l => l.GetProperty("province").GetString() == "Grad Zagreb"), "only Grad Zagreb");
    var run = doc.GetProperty("runs")[0];
    t.True(run.GetProperty("complete").GetBoolean(), "scan complete");
    t.Equal(3, run.GetProperty("pagesRead").GetInt32());
    t.Equal(16, run.GetProperty("otherTypes").GetInt32());
    t.Equal(3, api.Requests.Count);
    t.True(api.UserAgents.All(u => u.StartsWith("DataFetch/")), "honest User-Agent");
    t.True(doc.TryGetProperty("lastCompleteScan", out _), "lastCompleteScan set");
});

runner.Test("nekretnine.hr: fields mapped, agent names and phone numbers not stored", async t =>
{
    var (o, api) = NekretnineSetup(t, template);
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    var text = t.ReadText("data/listings/listings-nekretnine.json");
    t.NotContains("Ana Primjer", text); t.NotContains("091 000 0000", text); t.NotContains("+385 1 000 0000", text);
    t.NotContains("091 555 1234", text); t.NotContains("@primjer.hr", text);
    t.NotContains("\"supervisor\"", text); t.NotContains("\"phones\"", text);

    var l0 = t.ReadJson("data/listings/listings-nekretnine.json").GetProperty("listings").EnumerateArray().First(l => l.GetProperty("id").GetInt64() == 7000000);
    t.Equal(300m, l0.GetProperty("areaM2").GetDecimal());
    t.Equal(24000m, l0.GetProperty("priceEur").GetDecimal());
    t.Equal(80m, l0.GetProperty("pricePerM2").GetDecimal());
    t.Equal("Novi Zagreb - zapad", l0.GetProperty("macrozone").GetString());
    t.Equal("struja, voda", string.Join(", ", l0.GetProperty("utilities").EnumerateArray().Select(u => u.GetString())));
    t.Contains("[phone]", l0.GetProperty("description").GetString()!);
    t.Equal("Agencija 0", l0.GetProperty("agencyName").GetString());
    t.Equal("https://www.nekretnine.hr/oglasi/7000000/", l0.GetProperty("url").GetString());
    t.False(l0.TryGetProperty("lat", out _), "ad without coordinates has no lat");
    t.Equal("no_map", l0.GetProperty("raw").GetProperty("realEstate").GetProperty("properties")[0].GetProperty("location").GetProperty("marker").GetString());
    var hidden = t.ReadJson("data/listings/listings-nekretnine.json").GetProperty("listings").EnumerateArray().First(l => l.GetProperty("id").GetInt64() == 7000005);
    t.False(hidden.TryGetProperty("priceEur", out _), "hidden price is not stored");
});

runner.Test("nekretnine.hr: second scan records new ads, price changes and removals", async t =>
{
    var (o, api) = NekretnineSetup(t, template);
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    api.Current = FakeNekretnineApi.Mode.Changed;
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    var doc = t.ReadJson("data/listings/listings-nekretnine.json");
    var run = doc.GetProperty("runs")[1];
    t.Equal(1, run.GetProperty("new").GetInt32());
    t.Equal(1, run.GetProperty("priceChanged").GetInt32());
    t.Equal(2, run.GetProperty("removed").GetInt32());
    var byId = doc.GetProperty("listings").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetInt64());
    t.Equal(45, byId.Count);
    t.Equal(2, byId[7000000].GetProperty("priceHistory").GetArrayLength());
    t.True(byId[7000001].TryGetProperty("removedAt", out _), "7000001 marked removed");
    t.False(byId[7000004].TryGetProperty("removedAt", out _), "7000004 still active");
});

runner.Test("nekretnine.hr: a block mid-scan stops at once, saves, and marks nothing as removed", async t =>
{
    var (o, api) = NekretnineSetup(t, template);
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    api.Current = FakeNekretnineApi.Mode.Changed;
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);          // 7000001/2 removed
    api.Current = FakeNekretnineApi.Mode.Http403OnPage3;
    var before = api.Requests.Count;
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    t.Equal(3, api.Requests.Count - before);                                 // pages 1, 2, 3 – no retry
    var doc = t.ReadJson("data/listings/listings-nekretnine.json");
    var run = doc.GetProperty("runs")[2];
    t.False(run.GetProperty("complete").GetBoolean(), "incomplete");
    t.Contains("403", run.GetProperty("stopReason").GetString()!);
    t.Equal(0, run.GetProperty("removed").GetInt32());
    t.Equal(2, run.GetProperty("reappeared").GetInt32());                    // back on page 1–2 in the normal list
    var byId = doc.GetProperty("listings").EnumerateArray().ToDictionary(l => l.GetProperty("id").GetInt64());
    t.False(byId[7999999].TryGetProperty("removedAt", out _), "not seen in an incomplete scan, but not marked removed");
});

foreach (var (mode, expect) in new[]
{
    (FakeNekretnineApi.Mode.Http403OnPage1, "HTTP 403"),
    (FakeNekretnineApi.Mode.CaptchaPage, "bot check"),
    (FakeNekretnineApi.Mode.HtmlMaintenancePage, "expected JSON"),
})
{
    runner.Test($"nekretnine.hr: {mode} → stops after one request, file unchanged", async t =>
    {
        var (o, api) = NekretnineSetup(t, template);
        await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
        var listingsBefore = t.ReadJson("data/listings/listings-nekretnine.json").GetProperty("listings").GetArrayLength();
        api.Current = mode;
        var before = api.Requests.Count;
        await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
        t.Equal(1, api.Requests.Count - before);
        var doc = t.ReadJson("data/listings/listings-nekretnine.json");
        t.Equal(listingsBefore, doc.GetProperty("listings").GetArrayLength());
        t.Contains(expect, doc.GetProperty("runs")[1].GetProperty("stopReason").GetString()!);
        t.True(doc.GetProperty("listings").EnumerateArray().All(l => !l.TryGetProperty("removedAt", out _)), "nothing removed");
    });
}

runner.Test("nekretnine.hr: dry run and probe write no results file", async t =>
{
    var (o, api) = NekretnineSetup(t, template);
    o.Mode = RunMode.DryRun;
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    o.Mode = RunMode.Probe;
    await NekretnineApp.RunAsync(o, NekretnineCollector.CreateHttpClient(api), default);
    t.False(File.Exists(t.PathOf("data/listings/listings-nekretnine.json")), "no results file");
    var snapshot = t.ReadText("data/datafetch/snapshots/nekretnine-page1.json");
    t.NotContains("Ana Primjer", snapshot); t.NotContains("091 555 1234", snapshot);
});

// ------------------------------------------------------------------ Njuškalo
const string Search = "https://www.njuskalo.hr/prodaja-zemljista?geo%5BlocationIds%5D=1153&landTypeId=235&sort=new";

runner.Test("Njuškalo: collects 5 ads, filters, masks contacts, no seller data", async t =>
{
    var (o, site) = NjuskaloSetup(t, Search, maxAds: 5);
    var browser = new FakeBrowser(site);
    t.Equal(0, await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default));
    var doc = t.ReadJson("data/listings/listings-njuskalo.json");
    var listings = doc.GetProperty("listings").EnumerateArray().ToList();
    t.Equal(5, listings.Count);
    t.True(listings.All(l => l.GetProperty("priceEur").GetDecimal() > 0 && l.GetProperty("areaM2").GetDecimal() > 0), "price and area read");
    t.True(listings.All(l => l.GetProperty("locationText").GetString()!.StartsWith("Grad Zagreb")), "Grad Zagreb only");
    var text = t.ReadText("data/listings/listings-njuskalo.json");
    t.NotContains("Ivan Horvat", text); t.NotContains("091 123 4567", text); t.NotContains("primjer.hr", text);
    t.Contains("1234/5", text);                                             // k.č. kept
    t.Equal(1, doc.GetProperty("resumePage").GetInt32());                    // capped on page 1
    t.Equal(1, doc.GetProperty("skippedIds").GetArrayLength());              // ad 40000003 (agricultural)
});

runner.Test("Njuškalo: follow-up runs pick up new ads first, then continue older ones", async t =>
{
    var (o, site) = NjuskaloSetup(t, Search, maxAds: 5);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(new FakeBrowser(site)), default);
    site.AddNewAds(3);
    o.Settings.MaxAds = 200;
    var browser = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default);
    var doc = t.ReadJson("data/listings/listings-njuskalo.json");
    var ids = doc.GetProperty("listings").EnumerateArray().Select(l => l.GetProperty("id").GetString()).ToList();
    t.Equal(ids.Count, ids.Distinct().Count());
    t.True(ids.Contains("50000125") && ids.Contains("50000127"), "new ads at the top collected");
    // 125 + 3 ads; per 25: agricultural (3), Zagrebačka (8), no land type + title "Zemljište" (18) → 3 × 5 skipped
    t.Equal(128 - 15, ids.Count);
    t.False(doc.TryGetProperty("resumePage", out var rp) && rp.ValueKind != JsonValueKind.Null, "no resume page – reached the end");
    var run = doc.GetProperty("runs")[1];
    t.Contains("end of the search", run.GetProperty("stopReason").GetString()!);
    // third run: nothing new → stops on page 1 without opening ads
    var b3 = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(b3), default);
    t.Equal(1, b3.PagesOpened);
});

runner.Test("Njuškalo: access-denied page stops the run (no retry, nothing saved)", async t =>
{
    var (o, site) = NjuskaloSetup(t, "https://www.njuskalo.hr/blocked/zagreb?sort=new", maxAds: 5);
    var browser = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default);
    t.Equal(1, browser.PagesOpened);
    var doc = t.ReadJson("data/listings/listings-njuskalo.json");
    t.Equal(0, doc.GetProperty("listings").GetArrayLength());
});

runner.Test("Njuškalo: unknown result-list layout stops when nobody can confirm", async t =>
{
    var (o, site) = NjuskaloSetup(t, Search, maxAds: 5);
    site.OldLayout = true;
    var browser = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default);
    var doc = t.ReadJson("data/listings/listings-njuskalo.json");
    t.Equal(0, doc.GetProperty("listings").GetArrayLength());
    t.Contains("not recognised", doc.GetProperty("runs")[0].GetProperty("stopReason").GetString()!);
});

runner.Test("Njuškalo: a site that repeats its last page does not loop", async t =>
{
    var (o, site) = NjuskaloSetup(t, Search, maxAds: 500);
    site.RepeatLastPage = true;
    o.Settings.MaxPages = 50;
    var browser = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default);
    var run = t.ReadJson("data/listings/listings-njuskalo.json").GetProperty("runs")[0];
    t.Equal(5, run.GetProperty("pagesRead").GetInt32());
    t.Contains("end of the search", run.GetProperty("stopReason").GetString()!);
});

runner.Test("Njuškalo: dry run opens no ads and saves nothing", async t =>
{
    var (o, site) = NjuskaloSetup(t, Search, maxAds: 10);
    o.Mode = RunMode.DryRun;
    var browser = new FakeBrowser(site);
    await App.RunAsync(o, _ => Task.FromResult<IPageBrowser>(browser), default);
    t.Equal(1, browser.PagesOpened);
    t.False(File.Exists(t.PathOf("data/listings/listings-njuskalo.json")), "no results file");
});

return await runner.RunAllAsync();

// ------------------------------------------------------------------ setup
static (RunOptions, FakeNekretnineApi) NekretnineSetup(TestContext t, string template)
{
    var o = new RunOptions { AppFolder = t.AppFolder, Settings = new AppSettings() };   // default paths: ../../data/…
    o.Settings.Nekretnine.ApiUrl = new NekretnineSettings().ApiUrl;                       // the real address; the fake answers it
    return (o, new FakeNekretnineApi(template));
}

static (RunOptions, FakeNjuskaloSite) NjuskaloSetup(TestContext t, string searchUrl, int maxAds)
{
    var o = new RunOptions
    {
        AppFolder = t.AppFolder,
        Settings = new AppSettings { Source = "njuskalo", SearchUrl = searchUrl, MaxAds = maxAds, MinDelayMs = 0, MaxDelayMs = 0 },
    };
    return (o, new FakeNjuskaloSite());
}
