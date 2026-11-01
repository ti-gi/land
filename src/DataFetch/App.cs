using DataFetch.Browser;
using DataFetch.Output;
using DataFetch.Sources;
using DataFetch.Storage;

namespace DataFetch;

/// <summary>Runs one collection pass. The browser is passed in so the pipeline can be tested without a real one.</summary>
public static class App
{
    public static async Task<int> RunAsync(RunOptions o, Func<string, Task<IPageBrowser>> startBrowser, CancellationToken ct)
    {
        var s = o.Settings;
        if (string.IsNullOrWhiteSpace(s.SearchUrl))
        {
            Ui.Error("No search URL set.");
            Ui.Info("Open Njuškalo in your browser, set up the search you want (e.g. Nekretnine → Zemljišta, Grad Zagreb),");
            Ui.Info("sort by newest, then copy the address bar into \"SearchUrl\" in appsettings.json (or pass --search \"<url>\").");
            return 2;
        }
        if (!Uri.TryCreate(s.SearchUrl, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("njuskalo.hr", StringComparison.OrdinalIgnoreCase))
        {
            Ui.Error($"SearchUrl must be a njuskalo.hr address: {s.SearchUrl}");
            return 2;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["geo[locationIds]"] is null || query["landTypeId"] is null)
            Ui.Warn("Note: SearchUrl has no location and/or land-type filter – Njuškalo will return ads from everywhere. " +
                    "For building land in Grad Zagreb use: https://www.njuskalo.hr/prodaja-zemljista?geo%5BlocationIds%5D=1153&landTypeId=235&sort=new");
        if (query["sort"] != "new")
            Ui.Warn("Note: SearchUrl is not sorted newest first (sort=new) – follow-up runs rely on that order.");

        var outputPath = o.PathIn(s.OutputFile);
        var store = ListingsFile.Open(outputPath);
        Ui.Info($"Results file: {outputPath} ({store.Document.Listings.Count} listings so far)");
        Ui.Info($"Limits: {s.MaxAds} new ads, {s.MaxPages} search pages, {s.MinDelayMs}–{s.MaxDelayMs} ms (random) before each page load. Ctrl+C stops and saves.");

        var profile = o.PathIn(s.BrowserProfileFolder);
        await using var browser = await startBrowser(profile);
        var collector = new NjuskaloCollector(browser, s, profile);

        if (o.Mode == RunMode.Probe)
        {
            await collector.ProbeAsync(o.PathIn(s.SnapshotFolder), ct);
            return 0;
        }

        var (run, fresh) = await collector.CollectAsync(store, o.Mode == RunMode.DryRun, ct);
        Summary.Print(run, fresh, store.Document.Listings.Count, outputPath);
        if (o.Mode == RunMode.DryRun) Ui.Warn("\nDry run: no ads were opened and nothing was saved.");
        return 0;
    }
}
