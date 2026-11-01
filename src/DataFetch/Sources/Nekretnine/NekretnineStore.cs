using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataFetch.Sources.Nekretnine;

public enum UpsertResult { New, PriceChanged, Unchanged, Reappeared }

/// <summary>All nekretnine.hr results in one JSON file (data/listings/listings-nekretnine.json by default).</summary>
public sealed class NekretnineStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    readonly string _path;
    readonly Dictionary<long, NekretnineListing> _byId;
    public NekretnineDocument Document { get; }

    NekretnineStore(string path, NekretnineDocument doc)
    {
        _path = path;
        Document = doc;
        _byId = doc.Listings.ToDictionary(l => l.Id);
    }

    public static NekretnineStore Open(string path) => new(path,
        File.Exists(path) ? JsonSerializer.Deserialize<NekretnineDocument>(File.ReadAllText(path), Json) ?? new() : new());

    public bool Contains(long id) => _byId.ContainsKey(id);
    public int ActiveCount => Document.Listings.Count(l => l.RemovedAt is null);

    public UpsertResult Upsert(NekretnineListing incoming, DateTimeOffset now)
    {
        if (!_byId.TryGetValue(incoming.Id, out var old))
        {
            incoming.FirstSeen = now;
            incoming.LastSeen = now;
            incoming.PriceHistory.Add(new PricePoint(now, incoming.PriceEur));
            Document.Listings.Add(incoming);
            _byId[incoming.Id] = incoming;
            return UpsertResult.New;
        }

        var result = old.RemovedAt is not null ? UpsertResult.Reappeared
            : old.PriceEur != incoming.PriceEur ? UpsertResult.PriceChanged
            : UpsertResult.Unchanged;
        incoming.FirstSeen = old.FirstSeen;
        incoming.LastSeen = now;
        incoming.RemovedAt = null;
        incoming.PriceHistory = old.PriceHistory;
        if (old.PriceEur != incoming.PriceEur) incoming.PriceHistory.Add(new PricePoint(now, incoming.PriceEur));
        Document.Listings[Document.Listings.IndexOf(old)] = incoming;
        _byId[incoming.Id] = incoming;
        return result;
    }

    /// <summary>After a complete scan: marks active listings that were not seen as removed. Returns how many.</summary>
    public int MarkRemoved(IReadOnlySet<long> seen, DateTimeOffset now)
    {
        var n = 0;
        foreach (var l in Document.Listings)
            if (l.RemovedAt is null && !seen.Contains(l.Id)) { l.RemovedAt = now; n++; }
        return n;
    }

    public void Save()
    {
        Document.Updated = DateTimeOffset.Now;
        var tmp = _path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(tmp, JsonSerializer.Serialize(Document, Json));
        File.Move(tmp, _path, overwrite: true);
    }
}
