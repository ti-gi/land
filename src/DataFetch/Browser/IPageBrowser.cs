namespace DataFetch.Browser;

public sealed record PageResult(string Url, int? Status, string Title, string Html);

/// <summary>The browser the collector reads pages through.</summary>
public interface IPageBrowser : IAsyncDisposable
{
    /// <summary>Navigates to a URL and returns the rendered page.</summary>
    Task<PageResult> OpenAsync(string url, CancellationToken ct);

    /// <summary>Re-reads the page currently shown (e.g. after the user resolved something in the window).</summary>
    Task<PageResult> CurrentAsync(CancellationToken ct);

    /// <summary>Scrolls down the page so content that loads on scroll is rendered.</summary>
    Task ScrollThroughAsync(CancellationToken ct);

    /// <summary>True when a person can see and use the browser window.</summary>
    bool IsVisible { get; }
}
