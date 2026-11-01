using System.Text.Json;

namespace DataFetch.Tests;

/// <summary>A tiny test runner: no framework needed, so the project builds without NuGet packages.</summary>
public sealed class Runner(string? filter)
{
    readonly List<(string Name, Func<TestContext, Task> Body)> _tests = new();

    public void Test(string name, Action<TestContext> body) => _tests.Add((name, t => { body(t); return Task.CompletedTask; }));
    public void Test(string name, Func<TestContext, Task> body) => _tests.Add((name, body));

    public async Task<int> RunAllAsync()
    {
        var realOut = Console.Out;
        int passed = 0, failed = 0;
        foreach (var (name, body) in _tests)
        {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var log = new StringWriter();
            var t = new TestContext();
            Console.SetOut(log);                     // keep the app's console output for failures only
            Exception? error = null;
            try { await body(t); }
            catch (Exception ex) { error = ex; }
            finally { Console.SetOut(realOut); }

            if (error is null && t.Failures.Count == 0)
            {
                passed++;
                Console.ForegroundColor = ConsoleColor.Green; Console.Write("  PASS  "); Console.ResetColor();
                Console.WriteLine(name);
                t.Cleanup();
            }
            else
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.Red; Console.Write("  FAIL  "); Console.ResetColor();
                Console.WriteLine(name);
                foreach (var f in t.Failures) Console.WriteLine($"        {f}");
                if (error is not null) Console.WriteLine($"        {error.GetType().Name}: {error.Message}\n{error.StackTrace}");
                Console.WriteLine($"        files: {t.Root}");
                Console.WriteLine("        --- app output ---");
                foreach (var line in log.ToString().Split('\n').TakeLast(40)) Console.WriteLine($"        {line.TrimEnd()}");
            }
        }
        Console.WriteLine($"\n{passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}

/// <summary>One scenario: a temporary folder shaped like the repository, and the checks made in it.</summary>
public sealed class TestContext
{
    public string Root { get; } = Directory.CreateTempSubdirectory("datafetch-test-").FullName;
    public string AppFolder => Directory.CreateDirectory(Path.Combine(Root, "src", "DataFetch")).FullName;
    public List<string> Failures { get; } = new();

    public string PathOf(string relativeToRoot) => Path.Combine(Root, relativeToRoot);
    public string ReadText(string relativeToRoot) => File.ReadAllText(PathOf(relativeToRoot));
    public JsonElement ReadJson(string relativeToRoot) => JsonDocument.Parse(ReadText(relativeToRoot)).RootElement;
    public void Cleanup() { try { Directory.Delete(Root, true); } catch { /* leave it */ } }

    void Fail(string message, string? where) => Failures.Add(where is null ? message : $"{message}  ({where})");

    public void Equal<T>(T expected, T actual, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) Fail($"expected {expected}, got {actual}", $"line {line}");
    }
    public void True(bool condition, string what, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!condition) Fail($"not true: {what}", $"line {line}");
    }
    public void False(bool condition, string what, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (condition) Fail($"not false: {what}", $"line {line}");
    }
    public void Null(object? value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (value is not null) Fail($"expected null, got {value}", $"line {line}");
    }
    public void NotNull(object? value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (value is null) Fail("expected a value, got null", $"line {line}");
    }
    public void Contains(string part, string text, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (!text.Contains(part)) Fail($"\"{Short(text)}\" does not contain \"{part}\"", $"line {line}");
    }
    public void NotContains(string part, string text, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    {
        if (text.Contains(part)) Fail($"must not contain \"{part}\"", $"line {line}");
    }
    static string Short(string s) => s.Length > 120 ? s[..120] + "…" : s;
}
