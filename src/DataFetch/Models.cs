namespace DataFetch;

/// <summary>One listing as found on its ad page. Seller details are not stored; phone numbers and e-mails in the description are masked.</summary>
public sealed class Listing
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public decimal? PriceEur { get; set; }
    public decimal? AreaM2 { get; set; }
    public decimal? PricePerM2 { get; set; }
    public string? LandType { get; set; }
    public string? LocationText { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    /// <summary>"map_pin" when the ad page contained coordinates, otherwise "none".</summary>
    public string LocationSource { get; set; } = "none";
    /// <summary>True when Njuškalo marks the map pin as approximate.</summary>
    public bool? LocationApproximate { get; set; }
    public string? CadastralParcel { get; set; }
    public string? CadastralMunicipality { get; set; }
    public string? PublishedText { get; set; }
    /// <summary>All labelled fields on the ad page, with the labels as Njuškalo shows them.</summary>
    public Dictionary<string, string> Details { get; set; } = new();
    /// <summary>Ad description text (phone numbers and e-mail addresses masked).</summary>
    public string? Description { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public List<PricePoint> PriceHistory { get; set; } = new();
}

public sealed record PricePoint(DateTimeOffset SeenAt, decimal? PriceEur);

public sealed class RunInfo
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string SearchUrl { get; set; } = "";
    public int PagesRead { get; set; }
    public int NewAds { get; set; }
    public int KnownAdsSeen { get; set; }
    /// <summary>Ads that could not be read (no title/price/area found).</summary>
    public int Unreadable { get; set; }
    /// <summary>Ads opened but not saved because they are not building land in the City of Zagreb.</summary>
    public int Skipped { get; set; }
    public string StopReason { get; set; } = "";
}

/// <summary>Root of the Njuškalo results file.</summary>
public sealed class ListingsDocument
{
    public string Note { get; set; } = "Collected by DataFetch for personal research. Seller details not stored; phone numbers and e-mails in descriptions masked.";
    public DateTimeOffset Updated { get; set; }
    public List<RunInfo> Runs { get; set; } = new();
    public List<Listing> Listings { get; set; } = new();
    /// <summary>When a run stopped at MaxAds/MaxPages, the search page where older, not yet collected ads continue.</summary>
    public int? ResumePage { get; set; }
    /// <summary>Ads opened once and found not to be building land in the City of Zagreb – not opened again.</summary>
    public List<string> SkippedIds { get; set; } = new();
}
