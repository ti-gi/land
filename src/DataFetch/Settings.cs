using System.Text.Json;

namespace DataFetch;

/// <summary>Settings from appsettings.json, optionally overridden on the command line.</summary>
public sealed class AppSettings
{
    /// <summary>Which source a run uses: "nekretnine" (default) or "njuskalo".</summary>
    public string Source { get; set; } = "nekretnine";
    public NekretnineSettings Nekretnine { get; set; } = new();

    // Paths are relative to this project folder (src/DataFetch). Collected data goes to the repository's data/ folder,
    // which git ignores, so the web map (src/web) reads it from there without copying.
    /// <summary>Page copies saved by --probe (they may contain seller data – delete after use).</summary>
    public string SnapshotFolder { get; set; } = "../../data/datafetch/snapshots";

    // ---- Njuškalo (secondary source, read with a visible browser) ----
    public string SearchUrl { get; set; } = "";
    public int MaxAds { get; set; } = 100;
    public int MaxPages { get; set; } = 10;
    /// <summary>Shortest pause before each page load, in milliseconds.</summary>
    public int MinDelayMs { get; set; } = 4000;
    /// <summary>Longest pause before each page load, in milliseconds (a random value in between is used each time).</summary>
    public int MaxDelayMs { get; set; } = 8000;
    public string OutputFile { get; set; } = "../../data/listings/listings-njuskalo.json";
    public string BrowserProfileFolder { get; set; } = "../../data/datafetch/browser-profile";
}

/// <summary>nekretnine.hr: read through the site's listing API (no browser).</summary>
public sealed class NekretnineSettings
{
    /// <summary>Listing search for land for sale in Grad Zagreb, newest first. The page number (pag) is added by the app.</summary>
    public string ApiUrl { get; set; } =
        "https://www.nekretnine.hr/api-next/search-list/listings/?fkRegione=HR_3&idProvincia=HR_10&idComune=743&idNazione=HR" +
        "&idContratto=1&idCategoria=24&__lang=hr&criterio=data&ordine=desc&path=%2Fprodaja-zemljista%2Fzagreb%2F";
    /// <summary>Land types to keep (typology.id). 261 = Građevinsko zemljište. Empty = keep all land types.</summary>
    public List<int> TypologyIds { get; set; } = new() { 261 };
    /// <summary>Keep only listings in this province (location.province). Empty = no check.</summary>
    public string Province { get; set; } = "Grad Zagreb";
    public int MaxPages { get; set; } = 100;
    /// <summary>Pause between API requests in milliseconds (0 = none).</summary>
    public int DelayMs { get; set; } = 0;
    public string OutputFile { get; set; } = "../../data/listings/listings-nekretnine.json";
}

public enum RunMode { Collect, DryRun, Probe, InstallBrowser, Help }

public sealed class RunOptions
{
    public RunMode Mode { get; set; } = RunMode.Collect;
    public string AppFolder { get; set; } = "";
    public AppSettings Settings { get; set; } = new();

    public string PathIn(string relative) => Path.GetFullPath(Path.Combine(AppFolder, relative));

    public const string HelpText = """
        DataFetch – collects building-land listings for the City of Zagreb.

        Usage:  dotnet run -- [options]

          --source nekretnine   (default) read nekretnine.hr through its listing API → data/listings/listings-nekretnine.json
          --source njuskalo     read Njuškalo with a visible browser → data/listings/listings-njuskalo.json

          (no options)          collect: read the whole search and save new/changed listings
          --probe               fetch the first page only and show what was read (saves a snapshot)
          --dry-run             read the search and report what is new, without saving
          --max-pages <n>       override MaxPages of the selected source

        Njuškalo only:
          --max-ads <n>         override MaxAds (default 100)
          --delay-ms <min>-<max>  pause before each page load, random between min and max ms (e.g. 4000-8000)
          --search "<url>"      use this Njuškalo search URL instead of the one in appsettings.json
          --install-browser     download the Chromium browser used for Njuškalo (run once)

          --help                show this text

        Press Ctrl+C at any time to stop; everything collected so far is saved.
        """;

    public static RunOptions Load(string[] args)
    {
        var o = new RunOptions { AppFolder = FindAppFolder() };
        var settingsFile = Path.Combine(o.AppFolder, "appsettings.json");
        if (File.Exists(settingsFile))
        {
            o.Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsFile), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new AppSettings();
        }

        int? maxPages = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value after {args[i]}");
            switch (args[i].ToLowerInvariant())
            {
                case "--probe": o.Mode = RunMode.Probe; break;
                case "--dry-run": o.Mode = RunMode.DryRun; break;
                case "--install-browser": o.Mode = RunMode.InstallBrowser; break;
                case "--help": case "-h": case "/?": o.Mode = RunMode.Help; break;
                case "--max-ads": o.Settings.MaxAds = PositiveInt(Next(), "--max-ads"); break;
                case "--max-pages": maxPages = PositiveInt(Next(), "--max-pages"); break;
                case "--delay-ms":
                    var range = Next().Split('-', StringSplitOptions.TrimEntries);
                    if (range.Length != 2 || !int.TryParse(range[0], out var min) || !int.TryParse(range[1], out var max))
                        throw new ArgumentException("--delay-ms needs a range in milliseconds, e.g. --delay-ms 4000-8000");
                    o.Settings.MinDelayMs = min; o.Settings.MaxDelayMs = max; break;
                case "--search": o.Settings.SearchUrl = Next(); break;
                case "--source": o.Settings.Source = Next().ToLowerInvariant(); break;
                default: throw new ArgumentException($"Unknown option: {args[i]} (use --help)");
            }
        }
        if (o.Settings.Source is not ("nekretnine" or "njuskalo"))
            throw new ArgumentException($"Unknown source \"{o.Settings.Source}\" – use nekretnine or njuskalo");
        if (maxPages is int mp)
        {
            if (o.Settings.Source == "nekretnine") o.Settings.Nekretnine.MaxPages = mp; else o.Settings.MaxPages = mp;
        }
        if (o.Settings.Nekretnine.DelayMs < 0) throw new ArgumentException("Nekretnine.DelayMs must be 0 or more");
        if (o.Settings.MinDelayMs < 0 || o.Settings.MaxDelayMs < o.Settings.MinDelayMs)
            throw new ArgumentException($"Invalid delay: MinDelayMs ({o.Settings.MinDelayMs}) must be ≥ 0 and ≤ MaxDelayMs ({o.Settings.MaxDelayMs})");
        return o;
    }

    static int PositiveInt(string s, string name) =>
        int.TryParse(s, out var n) && n > 0 ? n : throw new ArgumentException($"{name} needs a positive whole number");

    // The app folder is the one holding DataFetch.csproj + appsettings.json, so results are
    // written there whether the app is started with `dotnet run` or from bin\.
    static string FindAppFolder()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "appsettings.json")) && File.Exists(Path.Combine(d.FullName, "DataFetch.csproj")))
                    return d.FullName;
            }
        }
        return Directory.GetCurrentDirectory();
    }
}
