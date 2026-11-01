using System.Globalization;

namespace DataFetch.Output;

/// <summary>The data-quality summary printed at the end of a run.</summary>
public static class Summary
{
    static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    public static void Print(RunInfo run, IReadOnlyList<Listing> newListings, int totalInFile, string outputPath)
    {
        Ui.Step("\n══════════ Summary ══════════");
        Ui.Info($"Stopped because:      {run.StopReason}");
        Ui.Info($"Search pages read:    {run.PagesRead}");
        Ui.Info($"New ads collected:    {run.NewAds}   (already known, seen again: {run.KnownAdsSeen}; skipped by filter: {run.Skipped}; unreadable: {run.Unreadable})");
        Ui.Info($"Listings in file:     {totalInFile}   → {outputPath}");
        if (newListings.Count == 0) return;

        int n = newListings.Count;
        string Pct(int k) => $"{k,3} ({100.0 * k / n,4:0}%)";

        Ui.Step("\nData completeness (new ads)");
        var withPin = newListings.Count(l => l.LocationSource == "map_pin");
        var textOnly = newListings.Count(l => l.LocationSource != "map_pin" && !string.IsNullOrWhiteSpace(l.LocationText));
        Ui.Info($"  map coordinates       {Pct(withPin)}");
        Ui.Info($"  location text only    {Pct(textOnly)}");
        Ui.Info($"  no usable location    {Pct(n - withPin - textOnly)}");
        Ui.Info($"  k.č. number in ad     {Pct(newListings.Count(l => l.CadastralParcel is not null))}");
        Ui.Info($"  with description      {Pct(newListings.Count(l => l.Description is not null))}");
        Ui.Info($"  avg. labelled fields  {newListings.Average(l => l.Details.Count),5:0.0}");

        var ppm = newListings.Where(l => l.PricePerM2 is not null).Select(l => l.PricePerM2!.Value).OrderBy(v => v).ToList();
        if (ppm.Count > 0)
        {
            Ui.Step("\nPrice per m² (new ads)");
            Ui.Info($"  min {ppm[0].ToString("N0", Hr)} €   median {ppm[ppm.Count / 2].ToString("N0", Hr)} €   max {ppm[^1].ToString("N0", Hr)} €   ({ppm.Count} ads with price and area)");
        }
    }
}
