# DataFetch

Collects **raw listing data** for **building land in the City of Zagreb** and saves it to JSON files in the repository's `data/listings/` folder
(git-ignored), where the web map (`src/web`) reads them. Part of the `land` repository – see the root README.
You start it yourself – there is no scheduler.

Two sources:

| Source | How | Results file |
|---|---|---|
| **nekretnine.hr** (default) | the site's own listing API – plain HTTP requests, no browser, reads the **whole search** in one run (~45 requests, about a minute) | `data/listings/listings-nekretnine.json` |
| Njuškalo | a visible Chromium window at human pace (random 4–8 s pause), at most 100 new ads per run | `data/listings/listings-njuskalo.json` |

## Setup (once)

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) – Visual Studio 2022 (17.8 or later) already includes it.
nekretnine.hr needs nothing else. For Njuškalo also run `dotnet run -- --install-browser` once (downloads Chromium, ~150 MB).

## Visual Studio

Open **`src/DataFetch/DataFetch.sln`** (it also contains the test project) in **Visual Studio 2022** (17.8 or later) or Visual Studio 2026. The project targets **.NET 8**.
The start button's dropdown has ready-made profiles (in `Properties\launchSettings.json`):

| Profile | Same as |
|---|---|
| nekretnine.hr – Collect | `dotnet run` |
| nekretnine.hr – Probe | `dotnet run -- --probe` |
| nekretnine.hr – Dry run | `dotnet run -- --dry-run` |
| Njuškalo – Collect / Probe / Collect 10 ads / Dry run | `dotnet run -- --source njuskalo …` |
| Install browser (once, Njuškalo only) | `dotnet run -- --install-browser` |

Paths in `appsettings.json` are relative to `src/DataFetch`, so results land in `data/listings/` also when started
from Visual Studio.

## nekretnine.hr (primary source)

```powershell
dotnet run -- --probe      # first page only: totals, land types on the page, first listing, snapshot in data\datafetch\snapshots\
dotnet run                 # read the whole search and update data\listings\listings-nekretnine.json
dotnet run -- --dry-run    # read the whole search, report what is new, save nothing
```

The app calls the same address the nekretnine.hr search page uses
(`/api-next/search-list/listings/…`, land for sale in the City of Zagreb, newest first, 25 ads per page) and reads
page after page **without a pause** until the last page. You see one line per page, e.g.
`page  7/44: 25 ads, 21 building land in Zagreb (3 new)`, and a summary at the end.

Filter: the API cannot filter by land type (adding the type parameter breaks the location filter), so the app reads all
land in Zagreb and keeps only **typology 261 – Građevinsko zemljište** with province **Grad Zagreb**. Agricultural
and other land is counted as "skipped".

How runs fit together:
- Every run is a full scan. New ads are added, known ads are updated (`lastSeen`), price changes go to `priceHistory`.
- After a **complete** scan, ads that are no longer in the search get `removedAt` (sold or withdrawn); if one comes
  back later, `removedAt` is cleared. An incomplete scan (Ctrl+C, error, `MaxPages`) never marks anything as removed.
- The file is saved after every page.
- If the site answers with an error, a non-JSON page or a bot check (DataDome), the app **stops at once and does not
  retry**. Wait before trying again, rather than repeating the run. The app identifies itself honestly
  (`User-Agent: DataFetch/1.0 (personal research)`); it does not imitate a browser.

### Settings (`"Nekretnine"` in `appsettings.json`)

| Setting | Default | Meaning |
|---|---|---|
| `ApiUrl` | Zagreb land for sale, newest first | The API address without `pag` (the app adds the page number) |
| `TypologyIds` | `[261]` | Land types to keep (261 = građevinsko). `[]` keeps all |
| `Province` | `Grad Zagreb` | Keep only this province. `""` keeps all |
| `MaxPages` | 100 | Safety limit – the search has about 45 pages |
| `DelayMs` | 0 | Pause between requests in milliseconds (0 = none) |
| `OutputFile` | `../../data/listings/listings-nekretnine.json` | Results file (relative to `src/DataFetch`) |

`"Source": "nekretnine"` at the top selects the default source; `--source njuskalo` overrides it for one run.

### Results file (`data/listings/listings-nekretnine.json`)

| Field | Content |
|---|---|
| `id`, `uuid`, `url` | nekretnine.hr ad number and link |
| `title`, `caption` | Ad title and short caption |
| `typologyId`, `typology` | Land type (261 Građevinsko zemljište) |
| `priceEur`, `priceText` | Price (empty if the advertiser hides it) |
| `areaM2`, `surfaceText`, `pricePerM2` | Area and price per m² |
| `address`, `city`, `province`, `macrozone`, `microzone` | Location as the site gives it (macrozone ≈ district) |
| `lat`, `lon` | Map coordinates, when the ad has them |
| `utilities` | Connections listed by the site: `struja`, `voda`, `kanalizacija`, `plin`, … |
| `description` | Usually empty in the API (only promoted ads carry a short text); phones/e-mails masked |
| `photoCount`, `visibility`, `isNew` | Number of photos, promotion level, "new" badge |
| `advertiserType`, `agencyId`, `agencyName`, `agencyUrl` | The agency (business) – agent names and phone numbers are **not** stored |
| `firstSeen`, `lastSeen`, `removedAt`, `priceHistory` | Tracking across runs |
| `raw` | The full API record for the ad, minus agent and phone data |

The API has no publication date; `firstSeen` is the first run that found the ad. The file also logs every run
(`runs`) and the time of the last complete scan (`lastCompleteScan`).

## Njuškalo (second source)

Run everything below with `--source njuskalo` (or the Njuškalo profiles in Visual Studio). Setup:

1. In `src\DataFetch` (PowerShell):
   ```powershell
   dotnet build
   dotnet run -- --install-browser
   ```
   The second command downloads the Chromium browser the app uses (~150 MB). It is separate from your normal Chrome.
2. The search is already set in `appsettings.json` – building land in Grad Zagreb, newest first:
   ```
   https://www.njuskalo.hr/prodaja-zemljista?geo%5BlocationIds%5D=1153&landTypeId=235&sort=new
   ```
   `geo[locationIds]=1153` is Grad Zagreb (1170 would be Zagrebačka županija), `landTypeId=235` is *građevinsko*,
   `sort=new` is newest first. Njuškalo's address bar does not always show the dropdown choices, so the app warns
   when the URL has no location or land-type filter.

### First: probe

```powershell
dotnet run -- --source njuskalo --probe
```

Opens the search page and the first ad, shows how many search results it found (only the result list – not the
"latest ads" sidebar), and what it could read (price, area, location, coordinates,
description, all labelled fields) and saves two page copies in `data\datafetch\snapshots\`.
The parser was written without access to the live site, so **check this output first** – if fields show
"(not found)", the snapshots let the parser be adjusted to Njuškalo's real page layout.
The snapshots are full page copies (they may contain seller names or phone numbers) – delete them afterwards.

On the very first visit the app pauses so you can answer Njuškalo's cookie banner in the browser window;
the app's own browser profile (`data\datafetch\browser-profile\`) remembers it.

### Collect

```powershell
dotnet run -- --source njuskalo                 # up to 100 new ads
dotnet run -- --source njuskalo --max-ads 20   # smaller run
dotnet run -- --source njuskalo --dry-run      # only list new ads from the search pages; opens no ads, saves nothing
dotnet run -- --help
```

What you will see: a Chromium window opening the search pages and then each new ad, and progress in the console,
e.g. `ad  23/100: 650 m², 85.000 €, Grad Zagreb, Sesvete (map pin)`. Don't click in that window while it runs.
At the end a short summary shows how complete the data is (coordinates, k.č. numbers, descriptions, price per m²).

Filter – only these ads are saved:
- **building land**: the "Vrsta zemljišta" field says *građevinsko* (or, if the page has no such field, the title does);
- **in the City of Zagreb**: the location says *Grad Zagreb* or *Zagreb* – Zagreb County locations
  ("Zagrebačka, Samobor, …") are not included.

Other ads are counted as "skipped", remembered (`skippedIds`) and not opened again. Keep the same filter in your
Njuškalo search so the app opens as few unneeded ads as possible; if more than `MaxAds` ads in one run don't match,
the app stops and asks you to check `SearchUrl`. `dotnet run -- --source njuskalo --probe` shows whether the first ad passes the filter.

How runs fit together:
- Search results are read newest first; ads already in `listings-njuskalo.json` are not opened again.
- A run stops at `MaxAds`, at `MaxPages`, when it reaches ads it already has, or at the end of the results.
- If a run stopped at the limit, the next run first picks up any new ads at the top and then continues with the
  older ads where the previous run stopped (`resumePage` in the file).
- **Ctrl+C** stops after the current step and saves; the file is also saved every 10 ads.
- If Njuškalo shows a CAPTCHA, an access-denied or any unexpected page, the app **stops and asks you**.
  It never retries on its own. Press Enter to continue (after resolving it in the window) or `q` to stop.

### Settings (`appsettings.json`, top level)

| Setting | Default | Meaning |
|---|---|---|
| `SearchUrl` | – | Your Njuškalo search (sorted newest first) |
| `MaxAds` | 100 | Maximum new ads opened per run |
| `MaxPages` | 10 | Maximum search result pages read per run |
| `MinDelayMs`, `MaxDelayMs` | 4000, 8000 | Pause before each page load: a random number of milliseconds in this range |
| `OutputFile` | `../../data/listings/listings-njuskalo.json` | Results file (relative to `src/DataFetch`) |
| `BrowserProfileFolder` | `../../data/datafetch/browser-profile` | The app's own browser profile (cookie choice etc.) |
| `SnapshotFolder` | `../../data/datafetch/snapshots` | Page copies saved by `--probe` (both sources) |

### Results file (`data/listings/listings-njuskalo.json`)

Per listing:

| Field | Content |
|---|---|
| `id`, `url` | Njuškalo ad number and link |
| `title` | Ad title |
| `details` | **All labelled fields on the ad page, as Njuškalo shows them** (e.g. `"Lokacija"`, `"Površina"`, `"Vrsta zemljišta"`, …) |
| `description` | Ad text, with phone numbers and e-mail addresses replaced by `[phone]` / `[e-mail]` |
| `priceEur`, `areaM2`, `pricePerM2` | Numbers read from the page, for convenience |
| `locationText` | The "Lokacija" field |
| `lat`, `lon`, `locationSource` | Map coordinates if the ad page contains them (`map_pin`), otherwise `none` |
| `cadastralParcel`, `cadastralMunicipality` | k.č. number and k.o. if written in the ad |
| `publishedText` | "Oglas objavljen" date as shown |
| `firstSeen`, `lastSeen`, `priceHistory` | When the app first/last saw the ad, and price changes |

The file also logs every run (`runs`). Seller fields (name, contact, phone) are not stored.

## Notes

- **.NET 8 support ends on 10 November 2026** (no more security updates after that). It is fine for this local tool,
  but plan the move: with Visual Studio 2026 change `<TargetFramework>net8.0</TargetFramework>` to `net10.0`
  in `DataFetch.csproj` – no code changes are needed.

- Use this for personal research, and check both sites' terms of use. nekretnine.hr's listing API is an internal
  interface of their website, not a public API – it can change or be closed without notice, and the site uses bot
  protection. Republishing collected listings (e.g. in a public web app) needs the site's permission or a licensed data source.
- Files: `Program.cs` (start-up, picks the source), `Sources\Nekretnine\` (API client, mapping, results file, summary),
  `App.cs` (Njuškalo run flow), `Sources\NjuskaloCollector.cs` (paging, limits, stopping),
  `Parsing\NjuskaloParser.cs` (reading pages), `Storage\ListingsFile.cs` (the results file),
  `Output\Summary.cs`, `Browser\PlaywrightBrowser.cs` (the visible Chromium window).
