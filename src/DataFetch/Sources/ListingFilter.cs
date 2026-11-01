using System.Text.RegularExpressions;

namespace DataFetch.Sources;

/// <summary>Keeps only building land (građevinsko zemljište) in the City of Zagreb.</summary>
public static partial class ListingFilter
{
    // "Grad Zagreb, Sesvete, …" or the word "Zagreb" on its own – but not "Zagrebačka" (Zagreb County)
    [GeneratedRegex(@"\bGrad Zagreb\b|\bZagreb\b", RegexOptions.IgnoreCase)]
    private static partial Regex CityOfZagreb();

    static bool IsBuildingLand(string? s) =>
        s is not null && (s.Contains("građevin", StringComparison.OrdinalIgnoreCase) || s.Contains("gradevin", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why a listing does not match, or null when it should be kept.</summary>
    public static string? SkipReason(Listing l)
    {
        if (l.LandType is { Length: > 0 } type)
        {
            if (!IsBuildingLand(type)) return $"land type is \"{type}\", not building land";
        }
        else if (!IsBuildingLand(l.Title))
        {
            return "not marked as building land (no land type on the page)";
        }

        var where = l.LocationText ?? l.Title;
        if (where is null || !CityOfZagreb().IsMatch(where))
            return l.LocationText is null ? "location not given" : $"location \"{l.LocationText}\" is not in the City of Zagreb";
        return null;
    }
}
