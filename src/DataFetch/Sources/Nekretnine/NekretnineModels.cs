using System.Text.Json.Nodes;

namespace DataFetch.Sources.Nekretnine;

/// <summary>
/// One nekretnine.hr listing, as returned by the listing API. Agent names and phone numbers are removed;
/// phone numbers and e-mails in the description are masked. <see cref="Raw"/> keeps the full API record (cleaned).
/// </summary>
public sealed class NekretnineListing
{
    public long Id { get; set; }
    public string? Uuid { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Caption { get; set; }
    public int? TypologyId { get; set; }
    public string? Typology { get; set; }
    public decimal? PriceEur { get; set; }
    public string? PriceText { get; set; }
    public decimal? AreaM2 { get; set; }
    public string? SurfaceText { get; set; }
    public decimal? PricePerM2 { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Macrozone { get; set; }
    public string? Microzone { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>Utility connections as listed by the site (Croatian where known, otherwise the site's own label).</summary>
    public List<string> Utilities { get; set; } = new();
    /// <summary>Usually empty in the list API (only promoted ads carry a shortened text).</summary>
    public string? Description { get; set; }
    public int? PhotoCount { get; set; }
    public string? AdvertiserType { get; set; }
    public long? AgencyId { get; set; }
    public string? AgencyName { get; set; }
    public string? AgencyUrl { get; set; }
    public string? Visibility { get; set; }
    public bool? IsNew { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    /// <summary>Set when a complete scan no longer found the ad (sold or withdrawn); cleared if it reappears.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
    public List<PricePoint> PriceHistory { get; set; } = new();
    public JsonObject? Raw { get; set; }
}

public sealed class NekretnineRun
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public bool Complete { get; set; }
    public int PagesRead { get; set; }
    public int? ApiTotal { get; set; }
    public int Matching { get; set; }
    public int New { get; set; }
    public int PriceChanged { get; set; }
    public int Removed { get; set; }
    public int Reappeared { get; set; }
    public int OtherTypes { get; set; }
    public string StopReason { get; set; } = "";
}

public sealed class NekretnineDocument
{
    public string Note { get; set; } =
        "nekretnine.hr listings collected by DataFetch for personal research. Agent names/phones removed; phones and e-mails in descriptions masked.";
    public string Source { get; set; } = "nekretnine.hr";
    public DateTimeOffset Updated { get; set; }
    public DateTimeOffset? LastCompleteScan { get; set; }
    public List<NekretnineRun> Runs { get; set; } = new();
    public List<NekretnineListing> Listings { get; set; } = new();
}
