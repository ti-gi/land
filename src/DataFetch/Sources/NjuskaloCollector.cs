using System.Globalization;
using DataFetch.Browser;
using DataFetch.Parsing;
using DataFetch.Storage;

namespace DataFetch.Sources;

/// <summary>
/// Reads Njuškalo search result pages newest-first, opens only ads not collected before,
/// and stops at MaxAds, MaxPages, already-known ads, Ctrl+C, or anything that looks like a block.
/// </summary>
public sealed class NjuskaloCollector
{
    static readonly CultureInfo Hr = CultureInfo.GetCultureInfo("hr-HR");

    readonly IPageBrowser _browser;
    readonly AppSettings _s;
    readonly string _profileFolder;
    bool _firstPage = true;

    public NjuskaloCollector(IPageBrowser browser, AppSettings settings, string profileFolder)
    {
        _browser = browser; _s = settings; _profileFolder = profileFolder;
    }

    // ---------------------------------------------------------------- collect / dry run
    public async Task<(RunInfo Run, List<Listing> New)> CollectAsync(ListingsFile store, bool dryRun, CancellationToken ct)
    {
        var run = new RunInfo { StartedAt = DateTimeOffset.Now, SearchUrl = _s.SearchUrl };
        var collected = new List<Listing>();
        var queued = new HashSet<string>();

        // Search results are newest first. A run first collects new ads at the top; once it reaches
        // ads it already has, it jumps to where an earlier capped run stopped and continues the backlog.
        var skipped = new HashSet<string>(store.Document.SkippedIds);
        var backlogFrom = store.Document.ResumePage;
        int? resume = backlogFrom;
        var inBacklog = false;
        var page = 1;
        HashSet<string>? previousIds = null;
        var layoutConfirmed = false;

        try
        {
            while (true)
            {
                if (run.PagesRead >= _s.MaxPages) { run.StopReason = $"reached MaxPages ({_s.MaxPages})"; resume = page; break; }

                var url = NjuskaloParser.WithPage(_s.SearchUrl, page);
                Ui.Step($"\nSearch page {page}{(inBacklog ? " (older ads)" : "")}: {url}");
                var result = await OpenSearchPageAsync(url, page, ct);
                var (links, fromList) = result is null ? (new List<AdLink>(), true) : NjuskaloParser.AdLinksWithSource(result.Html);
                if (links.Count > 0 && !fromList && !layoutConfirmed)
                {
                    if (!Ui.AskContinue("Njuškalo's result list was not recognised on this page (the layout may have changed). " +
                                        "Continuing takes every ad link on the page, which can include ads outside your search."))
                        throw new BlockedException("stopped: result list not recognised");
                    layoutConfirmed = true;
                }
                if (links.Count == 0)
                {
                    run.StopReason = page == 1 ? "no ads found on the first search page" : "reached the end of the search results";
                    if (page > 1) resume = null;
                    break;
                }
                var ids = links.Select(l => l.Id).ToHashSet();
                if (previousIds is not null && ids.SetEquals(previousIds))
                {
                    run.StopReason = "reached the end of the search results";   // the site repeated the last page
                    resume = null;
                    break;
                }
                previousIds = ids;
                run.PagesRead++;

                var now = DateTimeOffset.Now;
                var fresh = new List<AdLink>();
                foreach (var l in links)
                {
                    if (store.Contains(l.Id)) { store.MarkSeen(l.Id, now); run.KnownAdsSeen++; }
                    else if (!skipped.Contains(l.Id) && queued.Add(l.Id)) fresh.Add(l);
                }
                Ui.Info($"  {links.Count} ads on page, {fresh.Count} new");

                var processed = 0;
                foreach (var link in fresh)
                {
                    if (run.NewAds >= _s.MaxAds) break;
                    processed++;
                    if (dryRun)
                    {
                        run.NewAds++;
                        Ui.Info($"  new {run.NewAds,3}/{_s.MaxAds}: {link.Url}");
                        continue;
                    }

                    await PauseAsync(ct);
                    var listing = await ReadAdAsync(link, ct);
                    if (listing is null) { run.Unreadable++; continue; }
                    if (ListingFilter.SkipReason(listing) is { } why)
                    {
                        run.Skipped++;
                        skipped.Add(link.Id);
                        Ui.Info($"  skipped {link.Id}: {why}");
                        if (run.Skipped > _s.MaxAds)
                            throw new BlockedException($"more than {_s.MaxAds} ads did not match the filter – check that SearchUrl is limited to building land in Zagreb");
                        continue;
                    }

                    store.Upsert(listing);
                    collected.Add(listing);
                    run.NewAds++;
                    Ui.Info($"  ad {run.NewAds,3}/{_s.MaxAds}: {Describe(listing)}");
                    if (run.NewAds % 10 == 0) { store.Document.ResumePage = resume; store.Document.SkippedIds = skipped.ToList(); store.Save(); }
                }

                if (run.NewAds >= _s.MaxAds)
                {
                    run.StopReason = $"reached MaxAds ({_s.MaxAds})";
                    resume = processed < fresh.Count ? page : page + 1;   // where the next run continues
                    break;
                }

                if (fresh.Count == 0 && !inBacklog)
                {
                    if (backlogFrom is int from && from > page)
                    {
                        inBacklog = true;
                        page = Math.Max(page + 1, from - 1);   // one page earlier, in case new ads pushed older ones down
                        previousIds = null;
                        Ui.Info($"  up to date with new ads – continuing with older ads from page {page}");
                        await PauseAsync(ct);
                        continue;
                    }
                    run.StopReason = "up to date – reached ads that were already collected";
                    resume = null;
                    break;
                }
                page++;
                await PauseAsync(ct);
            }
        }
        catch (OperationCanceledException) { run.StopReason = "stopped by you (Ctrl+C)"; resume = Math.Max(page, resume ?? 0); }
        catch (BlockedException ex) { run.StopReason = ex.Message; resume = Math.Max(page, resume ?? 0); }
        catch (Exception ex)   // e.g. the browser window was closed – still save what was collected
        {
            run.StopReason = "error: " + ex.Message.Split('\n')[0];
            resume = Math.Max(page, resume ?? 0);
            Ui.Error($"\n{run.StopReason}");
        }

        run.FinishedAt = DateTimeOffset.Now;
        if (!dryRun)
        {
            store.Document.ResumePage = resume;
            store.Document.SkippedIds = skipped.ToList();
            store.Document.Runs.Add(run);
            store.Save();
        }
        return (run, collected);
    }

    // ---------------------------------------------------------------- probe
    public async Task ProbeAsync(string snapshotFolder, CancellationToken ct)
    {
        Directory.CreateDirectory(snapshotFolder);
        Ui.Step($"\nProbe – opening search page: {_s.SearchUrl}");
        var search = await OpenSearchPageAsync(_s.SearchUrl, 1, ct);
        if (search is null) { Ui.Error("Could not read the search page."); return; }
        var searchFile = Path.Combine(snapshotFolder, "search.html");
        await File.WriteAllTextAsync(searchFile, search.Html, ct);

        var (links, fromList) = NjuskaloParser.AdLinksWithSource(search.Html);
        Ui.Info($"  page title:  {search.Title}");
        Ui.Info($"  results:     {links.Count} ads{(fromList ? "" : "  ⚠ result list not recognised – all ad links on the page were taken")}");
        foreach (var l in links.Take(5)) Ui.Info($"    {l.Id}  {l.Url}");
        Ui.Info($"  next page:   {NjuskaloParser.WithPage(_s.SearchUrl, 2)}");
        if (links.Count == 0) { Ui.Warn($"  {NjuskaloParser.DescribeProblem(search)}"); return; }

        await PauseAsync(ct);
        var first = links[0];
        Ui.Step($"\nProbe – opening first ad: {first.Url}");
        var page = await _browser.OpenAsync(first.Url, ct);
        var adFile = Path.Combine(snapshotFolder, $"ad-{first.Id}.html");
        await File.WriteAllTextAsync(adFile, page.Html, ct);

        var ad = NjuskaloParser.ParseAd(page.Html);
        Ui.Info($"  title:        {ad.Title ?? "(not found)"}");
        Ui.Info($"  price:        {Fmt(ad.PriceEur)} €");
        Ui.Info($"  area:         {Fmt(ad.AreaM2)} m²");
        Ui.Info($"  land type:    {ad.LandType ?? "(not found)"}");
        Ui.Info($"  location:     {ad.LocationText ?? "(not found)"}");
        Ui.Info($"  coordinates:  {(ad.Lat is null ? "(not found)" : $"{ad.Lat:0.00000}, {ad.Lon:0.00000}{(ad.LocationApproximate == true ? " (approximate)" : "")}")}");
        Ui.Info($"  k.č.:         {ad.CadastralParcel ?? "(none in ad)"}");
        Ui.Info($"  published:    {ad.PublishedText ?? "(not found)"}");
        Ui.Info($"  description:  {(ad.Description is null ? "(not found)" : Trim(ad.Description.Replace('\n', ' '), 90))}");
        var verdict = ListingFilter.SkipReason(new Listing { Title = ad.Title, LandType = ad.LandType, LocationText = ad.LocationText });
        Ui.Info($"  filter:       {(verdict is null ? "kept (building land in the City of Zagreb)" : "would be skipped – " + verdict)}");
        Ui.Info($"  labelled fields found on the page ({ad.RawDetails.Count}):");
        foreach (var (label, value) in ad.RawDetails.Take(25)) Ui.Info($"    {label}: {Trim(value, 70)}");
        Ui.Ok($"\nSnapshots saved for checking the parser:\n  {searchFile}\n  {adFile}");
        Ui.Warn("  They are full page copies and can include the seller's name or phone number – delete them when no longer needed.");
    }

    // ---------------------------------------------------------------- helpers
    async Task<PageResult?> OpenSearchPageAsync(string url, int pageNumber, CancellationToken ct)
    {
        var page = await _browser.OpenAsync(url, ct);
        if (_firstPage)
        {
            _firstPage = false;
            await FirstVisitConsentAsync(ct);
            page = await _browser.CurrentAsync(ct);
        }
        await _browser.ScrollThroughAsync(ct);
        page = await _browser.CurrentAsync(ct);

        if (NjuskaloParser.AdLinks(page.Html).Count > 0) return page;
        if (pageNumber > 1 && !NjuskaloParser.LooksBlocked(page)) return page;   // normal page past the last result
        if (!Ui.AskContinue(NjuskaloParser.DescribeProblem(page)))
            throw new BlockedException("stopped at a page without ads: " + NjuskaloParser.DescribeProblem(page));
        page = await _browser.CurrentAsync(ct);
        return NjuskaloParser.AdLinks(page.Html).Count > 0 ? page : null;
    }

    async Task<Listing?> ReadAdAsync(AdLink link, CancellationToken ct)
    {
        var page = await _browser.OpenAsync(link.Url, ct);
        var ad = NjuskaloParser.ParseAd(page.Html);
        if (!ad.LooksValid)
        {
            if (!Ui.AskContinue($"Ad {link.Id}: {NjuskaloParser.DescribeProblem(page)}"))
                throw new BlockedException($"stopped at ad {link.Id}: " + NjuskaloParser.DescribeProblem(page));
            ad = NjuskaloParser.ParseAd((await _browser.CurrentAsync(ct)).Html);
            if (!ad.LooksValid) { Ui.Warn($"  ad {link.Id}: could not read title/price/area – not saved"); return null; }
        }

        var now = DateTimeOffset.Now;
        var listing = new Listing
        {
            Id = link.Id,
            Url = link.Url,
            Title = ad.Title,
            PriceEur = ad.PriceEur,
            AreaM2 = ad.AreaM2,
            PricePerM2 = ad.PriceEur is > 0 && ad.AreaM2 is > 0 ? Math.Round(ad.PriceEur.Value / ad.AreaM2.Value, 2) : null,
            LandType = ad.LandType,
            LocationText = ad.LocationText,
            Lat = ad.Lat,
            Lon = ad.Lon,
            LocationSource = ad.Lat is null ? "none" : "map_pin",
            LocationApproximate = ad.LocationApproximate,
            CadastralParcel = ad.CadastralParcel,
            CadastralMunicipality = ad.CadastralMunicipality,
            PublishedText = ad.PublishedText,
            Details = ad.RawDetails,
            Description = ad.Description,
            FirstSeen = now,
            LastSeen = now,
        };
        return listing;
    }

    async Task FirstVisitConsentAsync(CancellationToken ct)
    {
        var marker = Path.Combine(_profileFolder, ".consent-confirmed");
        if (!_browser.IsVisible || File.Exists(marker)) return;
        Ui.WaitForEnter("First visit with this browser profile: if Njuškalo shows a cookie/consent banner, choose your option in the browser window.");
        Directory.CreateDirectory(_profileFolder);
        await File.WriteAllTextAsync(marker, DateTimeOffset.Now.ToString("O"), ct);
    }

    /// <summary>Waits a random time between MinDelayMs and MaxDelayMs (inclusive) before the next page load.</summary>
    Task PauseAsync(CancellationToken ct) => Task.Delay(NextDelayMs(_s.MinDelayMs, _s.MaxDelayMs), ct);

    public static int NextDelayMs(int minMs, int maxMs) => Random.Shared.Next(minMs, maxMs + 1);

    static string Describe(Listing l)
    {
        return $"{Fmt(l.AreaM2)} m², {Fmt(l.PriceEur)} €, {Trim(l.LocationText ?? "?", 45)}{(l.Lat is null ? "" : " (map pin)")}";
    }

    static string Fmt(decimal? v) => v?.ToString("N0", Hr) ?? "?";
    static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";
}

public sealed class BlockedException(string message) : Exception(message);
