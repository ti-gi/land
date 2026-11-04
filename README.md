# land

What may be built where in Zagreb – and which building land is for sale.

- **Web map** (`src/web`) – every zone of the city's urban plan (GUP 2025) with what may be built there, plus
  building land currently for sale.
- **DataFetch** (`src/DataFetch`) – collects building-land listings for the City of Zagreb from nekretnine.hr
  (and optionally Njuškalo) into `data/listings/`.
- **gup-extract** (`tools/gup-extract`) – turns the plan's map PDF and text into `data/gup/zones.geojson` and
  `data/gup/rules.json`.

```
land/
├─ CLAUDE.md                  project rules and facts for Claude
├─ README.md
├─ docs/
│  ├─ data-contract.md        file formats shared between the parts, web API
│  └─ decisions.md            why the API source, why no bot evasion, …
├─ src/
│  ├─ DataFetch/              .NET 8 console app (+ DataFetch.sln, which also holds the tests)
│  └─ web/                    Node + Express + MapLibre
├─ tools/gup-extract/         Python: PDF → zones.geojson, text → rules.json
├─ tests/
│  ├─ DataFetch.Tests/        in-memory fakes of both sites, 19 checks
│  └─ fixtures/               synthetic sample data
└─ data/
   ├─ gup/                    zones.geojson, rules.json (committed)
   ├─ listings/               collected listings (git-ignored)
   └─ datafetch/              snapshots, browser profile (git-ignored)
```

## Quick start

Requirements: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or Visual Studio 2022 17.8+),
[Node.js 18+](https://nodejs.org). Python only for re-extracting the plan.

```powershell
# 1. collect listings (about a minute)
cd src\DataFetch
dotnet run

# 2. start the map
cd ..\web
npm install
copy .env.example .env      # optional: add a Google Maps key for Map/Satellite/Terrain backgrounds
npm start                   # http://localhost:3000
```

Run DataFetch again whenever you want fresh listings, then reload the map page.

Tests: `cd tests\DataFetch.Tests` → `dotnet run` (no internet needed).

Each part has its own README with details. `docs/data-contract.md` describes every file they exchange.

## Notes

- GUP data is extracted from the official documents but is **not legally binding** – check the official plan and
  any detailed plan (UPU) for a location.
- Listings are collected for personal research. The sites' content is not republished; agent and seller names and
  phone numbers are not stored. See `docs/decisions.md`.
- Private repository. Never commit `.env` or anything from `data/listings/` / `data/datafetch/`.
