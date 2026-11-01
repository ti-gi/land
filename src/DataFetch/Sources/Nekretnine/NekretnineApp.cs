using System.Globalization;

namespace DataFetch.Sources.Nekretnine;

/// <summary>Runs one nekretnine.hr pass: probe, dry run, or collect into listings-nekretnine.json.</summary>
public static class NekretnineApp
{
    static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    public static async Task<int> RunAsync(RunOptions o, HttpClient http, CancellationToken ct)
    {
        var s = o.Settings.Nekretnine;
        if (!Uri.TryCreate(s.ApiUrl, UriKind.Absolute, out _))
        {
            Ui.Error($"Nekretnine.ApiUrl is not a valid address: {s.ApiUrl}");
            return 2;
        }

        var collector = new NekretnineCollector(http, s);
        if (o.Mode == RunMode.Probe)
        {
            await collector.ProbeAsync(o.PathIn(o.Settings.SnapshotFolder), ct);
            return 0;
        }

        var outputPath = o.PathIn(s.OutputFile);
        var store = NekretnineStore.Open(outputPath);
        Ui.Info($"Source: nekretnine.hr listing API   (building land{(s.Province.Length > 0 ? " in " + s.Province : "")})");
        Ui.Info($"Results file: {outputPath} ({store.Document.Listings.Count} listings, {store.ActiveCount} active)");
        Ui.Info($"Reading all pages{(s.DelayMs > 0 ? $", {s.DelayMs} ms between requests" : ", no pause between requests")}. Ctrl+C stops and saves.\n");

        var (run, matching) = await collector.CollectAsync(store, o.Mode == RunMode.DryRun, ct);
        Print(run, matching, store, outputPath, o.Mode == RunMode.DryRun);
        return 0;
    }

    static void Print(NekretnineRun run, List<NekretnineListing> matching, NekretnineStore store, string outputPath, bool dryRun)
    {
        Ui.Step("\n══════════ Summary ══════════");
        Ui.Info($"Stopped because:        {run.StopReason}");
        Ui.Info($"Pages read:             {run.PagesRead}   (ads in the search: {run.ApiTotal?.ToString() ?? "?"}, skipped as other land type or area: {run.OtherTypes})");
        Ui.Info($"Building land, Zagreb:  {run.Matching}");
        Ui.Info($"  new:                  {run.New}");
        if (!dryRun)
        {
            Ui.Info($"  price changes:        {run.PriceChanged}");
            if (run.Reappeared > 0) Ui.Info($"  listed again:         {run.Reappeared}");
            Ui.Info(run.Complete
                ? $"  no longer listed:     {run.Removed}   (marked with removedAt)"
                : "  no longer listed:     not checked – the scan was not complete");
            Ui.Info($"Listings in file:       {store.Document.Listings.Count} ({store.ActiveCount} active)   → {outputPath}");
        }
        else Ui.Warn("Dry run: nothing was saved.");

        if (!run.Complete && (run.StopReason.Contains("HTTP") || run.StopReason.Contains("bot check") || run.StopReason.Contains("expected JSON")))
            Ui.Warn("The site refused or challenged a request. The app does not retry – try again later rather than repeatedly.");

        if (matching.Count == 0) return;
        int n = matching.Count;
        string Pct(int k) => $"{k,4} ({100.0 * k / n,3:0}%)";
        Ui.Step("\nData completeness (building land in this scan)");
        Ui.Info($"  coordinates          {Pct(matching.Count(l => l.Lat is not null))}");
        Ui.Info($"  price                {Pct(matching.Count(l => l.PriceEur is not null))}");
        Ui.Info($"  area                 {Pct(matching.Count(l => l.AreaM2 is not null))}");
        Ui.Info($"  utilities listed     {Pct(matching.Count(l => l.Utilities.Count > 0))}");
        Ui.Info($"  description          {Pct(matching.Count(l => l.Description is not null))}");

        var ppm = matching.Where(l => l.PricePerM2 is not null).Select(l => l.PricePerM2!.Value).OrderBy(v => v).ToList();
        if (ppm.Count > 0)
            Ui.Info($"\n  price per m²: min {ppm[0].ToString("N0", Hr)} €   median {ppm[ppm.Count / 2].ToString("N0", Hr)} €   max {ppm[^1].ToString("N0", Hr)} €   ({ppm.Count} ads)");

        var byZone = matching.Where(l => l.Macrozone is not null).GroupBy(l => l.Macrozone!).OrderByDescending(g => g.Count()).Take(8);
        Ui.Info("  by district: " + string.Join(", ", byZone.Select(g => $"{g.Key} {g.Count()}")));
    }
}
