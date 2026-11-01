using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace DataFetch.Parsing;

/// <summary>Small HTML helpers based on regular expressions (enough for reading labelled values).</summary>
public static partial class HtmlText
{
    [GeneratedRegex(@"<(script|style|noscript|svg)\b[^>]*>.*?</\1\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NonVisible();

    [GeneratedRegex(@"<(br|/p|/div|/li|/dd|/dt|/h\d|/tr|/td|/th|/section|/article)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex Lines();

    [GeneratedRegex(@"<dt\b[^>]*>(?<k>.*?)</dt>\s*<dd\b[^>]*>(?<v>.*?)</dd>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DtDd();

    [GeneratedRegex(@"<th\b[^>]*>(?<k>.*?)</th>\s*<td\b[^>]*>(?<v>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThTd();

    /// <summary>Visible text of a page: scripts/styles removed, tags stripped, one line per block.</summary>
    public static string Visible(string html)
    {
        var s = NonVisible().Replace(html, " ");
        s = BlockEnd().Replace(s, "\n");
        s = Tag().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Spaces().Replace(s, " ");
        return Lines().Replace(s, "\n").Trim();
    }

    /// <summary>Plain text of an HTML fragment on one line.</summary>
    public static string Clean(string fragment)
    {
        var s = Tag().Replace(NonVisible().Replace(fragment, " "), " ");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>Label/value pairs from definition lists and two-column tables.</summary>
    public static List<(string Label, string Value)> LabelledValues(string html)
    {
        var list = new List<(string, string)>();
        foreach (Match m in DtDd().Matches(html)) list.Add((NormalizeLabel(m.Groups["k"].Value), Clean(m.Groups["v"].Value)));
        foreach (Match m in ThTd().Matches(html)) list.Add((NormalizeLabel(m.Groups["k"].Value), Clean(m.Groups["v"].Value)));
        return list;
    }

    /// <summary>Label/value pairs with labels as shown on the page (for the raw Details field).</summary>
    public static List<(string Label, string Value)> LabelledValuesAsShown(string html)
    {
        var list = new List<(string, string)>();
        foreach (Match m in DtDd().Matches(html)) list.Add((Clean(m.Groups["k"].Value).TrimEnd(':', ' '), Clean(m.Groups["v"].Value)));
        foreach (Match m in ThTd().Matches(html)) list.Add((Clean(m.Groups["k"].Value).TrimEnd(':', ' '), Clean(m.Groups["v"].Value)));
        return list;
    }

    /// <summary>Inner HTML of the first element whose class contains <paramref name="classPart"/>, with nested divs balanced.</summary>
    public static string? ElementByClass(string html, string classPart)
    {
        var m = Regex.Match(html, $@"<(?<tag>div|section|p)\b[^>]*class=""[^""]*{Regex.Escape(classPart)}[^""]*""[^>]*>", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var tag = m.Groups["tag"].Value.ToLowerInvariant();
        int depth = 1, pos = m.Index + m.Length;
        var open = new Regex($@"<{tag}\b", RegexOptions.IgnoreCase);
        var close = new Regex($@"</{tag}\s*>", RegexOptions.IgnoreCase);
        while (depth > 0)
        {
            var c = close.Match(html, pos);
            if (!c.Success) return html[(m.Index + m.Length)..];
            var o = open.Match(html, pos);
            if (o.Success && o.Index < c.Index) { depth++; pos = o.Index + o.Length; }
            else { depth--; if (depth == 0) return html[(m.Index + m.Length)..c.Index]; pos = c.Index + c.Length; }
        }
        return null;
    }

    public static string NormalizeLabel(string raw) => Clean(raw).TrimEnd(':', ' ').ToLowerInvariant();

    /// <summary>Parses Croatian-style numbers: "1.250,50" → 1250.50, "650,00" → 650, "1.250" → 1250.</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\d[\d.\s ]*(?:,\d+)?");
        if (!m.Success) return null;
        var s = Regex.Replace(m.Value, @"[\s ]", "");
        if (s.Contains(','))
            s = s.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(s, @"^\d{1,3}(\.\d{3})+$"))
            s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
