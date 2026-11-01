# DataFetch.Tests

Self-checking tests for DataFetch. No test framework, no NuGet packages, no browser, no internet:

```powershell
cd tests\DataFetch.Tests
dotnet run                 # all tests; exit code 0 = passed
dotnet run -- nekretnine   # only tests whose name contains "nekretnine"
```

In Visual Studio: it is in `src/DataFetch/DataFetch.sln` – set **DataFetch.Tests** as startup project and run.

How it works:
- The project compiles DataFetch's own source files (everything except `Program.cs` and the Playwright browser),
  so it tests the real code without needing the Playwright package.
- `FakeNekretnineApi` answers HttpClient requests in memory: 60 synthetic ads in the real API shape
  (`../fixtures/nekretnine-listing.json`), plus modes for price changes/removals, HTTP 403, a DataDome-style
  bot check page and a maintenance page.
- `FakeNjuskalo` serves search and ad pages to a fake `IPageBrowser`: promoted ads, banners, a "latest ads" sidebar,
  seller names and phone numbers that must not be stored, a blocked page, an unknown layout and a repeated last page.
- Each test runs in a temporary folder shaped like the repository (`src/DataFetch` + `data/`), so the default
  relative output paths are tested too. Folders of failed tests are kept and printed.

All data is made up – no real listings, sellers or agents. When a site changes its pages or API, update the fakes
from a `--probe` snapshot first, then the parser, so the tests describe the new format.
