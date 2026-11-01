using Microsoft.Playwright;

namespace DataFetch.Browser;

/// <summary>
/// A visible Chromium window with its own profile folder (not your normal Chrome, not logged in anywhere).
/// The profile keeps cookies such as the consent choice between runs, like a normal returning visitor.
/// </summary>
public sealed class PlaywrightBrowser : IPageBrowser
{
    IPlaywright _playwright = null!;
    IBrowserContext _context = null!;
    IPage _page = null!;
    int? _lastStatus;

    public bool IsVisible => true;

    public static async Task<PlaywrightBrowser> StartAsync(string profileFolder)
    {
        Directory.CreateDirectory(profileFolder);
        var b = new PlaywrightBrowser { _playwright = await Playwright.CreateAsync() };
        b._context = await b._playwright.Chromium.LaunchPersistentContextAsync(profileFolder, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        b._page = b._context.Pages.Count > 0 ? b._context.Pages[0] : await b._context.NewPageAsync();
        return b;
    }

    public async Task<PageResult> OpenAsync(string url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = await _page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        _lastStatus = response?.Status;
        try
        {
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
        }
        catch (Exception) { /* busy pages never go fully idle – the content is there anyway */ }
        return await CurrentAsync(ct);
    }

    public async Task<PageResult> CurrentAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new PageResult(_page.Url, _lastStatus, await _page.TitleAsync(), await _page.ContentAsync());
    }

    public async Task ScrollThroughAsync(CancellationToken ct)
    {
        for (var i = 0; i < 6 && !ct.IsCancellationRequested; i++)
        {
            await _page.Mouse.WheelAsync(0, 800);
            await _page.WaitForTimeoutAsync(400);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await _context.CloseAsync(); } catch (Exception) { /* window may already be closed */ }
        _playwright?.Dispose();
    }
}
