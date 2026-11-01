using System.Text;
using DataFetch;
using DataFetch.Browser;
using DataFetch.Sources.Nekretnine;
using Microsoft.Playwright;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

RunOptions options;
try
{
    options = RunOptions.Load(args);
}
catch (ArgumentException ex)
{
    Ui.Error(ex.Message);
    return 2;
}

switch (options.Mode)
{
    case RunMode.Help:
        Console.WriteLine(RunOptions.HelpText);
        return 0;
    case RunMode.InstallBrowser:
        Ui.Info("Downloading Chromium for Playwright (one-time, ~150 MB)…");
        return Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
}

// Ctrl+C: finish the current step, save, and exit
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cts.IsCancellationRequested) return;        // second Ctrl+C exits immediately
    e.Cancel = true;
    Ui.Warn("\nStopping after the current step… (press Ctrl+C again to quit immediately)");
    cts.Cancel();
};

// Primary source: nekretnine.hr listing API (plain HTTP requests, no browser)
if (options.Settings.Source.Equals("nekretnine", StringComparison.OrdinalIgnoreCase))
{
    using var http = NekretnineCollector.CreateHttpClient();
    try
    {
        return await NekretnineApp.RunAsync(options, http, cts.Token);
    }
    catch (OperationCanceledException)
    {
        Ui.Warn("Stopped.");
        return 1;
    }
}

// Njuškalo: headed browser
try
{
    return await App.RunAsync(options, async profile => await PlaywrightBrowser.StartAsync(profile), cts.Token);
}
catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.OrdinalIgnoreCase))
{
    Ui.Error("The browser is not installed yet. Run once:  dotnet run -- --install-browser");
    return 3;
}
catch (PlaywrightException ex)
{
    Ui.Error($"Browser problem: {ex.Message.Split('\n')[0]}");
    return 1;
}
