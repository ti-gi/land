namespace DataFetch;

/// <summary>Console output and the few questions the app asks while running.</summary>
public static class Ui
{
    static void Write(ConsoleColor c, string s)
    {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = c;
        Console.WriteLine(s);
        Console.ForegroundColor = old;
    }

    public static void Info(string s) => Console.WriteLine(s);
    public static void Step(string s) => Write(ConsoleColor.Cyan, s);
    public static void Ok(string s) => Write(ConsoleColor.Green, s);
    public static void Warn(string s) => Write(ConsoleColor.Yellow, s);
    public static void Error(string s) => Write(ConsoleColor.Red, s);

    /// <summary>False when nobody can answer questions (input redirected, or set by tests).</summary>
    public static bool Interactive { get; set; } = !Console.IsInputRedirected;

    /// <summary>Asks whether to continue after a problem. Returns false (stop) when nobody can answer.</summary>
    public static bool AskContinue(string problem)
    {
        Warn($"\n  ⚠ {problem}");
        if (!Interactive) { Warn("  No one at the keyboard – stopping."); return false; }
        Warn("  Look at the browser window. If you can resolve it there (e.g. a consent or check page), do so.");
        Console.Write("  Press Enter to continue, or type q and Enter to stop: ");
        var line = Console.ReadLine();
        return line is not null && !line.Trim().Equals("q", StringComparison.OrdinalIgnoreCase);
    }

    public static void WaitForEnter(string message)
    {
        Warn($"\n  {message}");
        if (!Interactive) return;
        Console.Write("  Press Enter when ready: ");
        Console.ReadLine();
    }
}
