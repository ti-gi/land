using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataFetch.Storage;

/// <summary>Keeps all results in one JSON file (data/listings/listings-njuskalo.json by default).</summary>
public sealed class ListingsFile
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep č, ć, š, ž, đ readable
    };

    readonly string _path;
    public ListingsDocument Document { get; }
    readonly Dictionary<string, Listing> _byId;

    ListingsFile(string path, ListingsDocument doc)
    {
        _path = path;
        Document = doc;
        _byId = doc.Listings.ToDictionary(l => l.Id);
    }

    public static ListingsFile Open(string path)
    {
        var doc = File.Exists(path)
            ? JsonSerializer.Deserialize<ListingsDocument>(File.ReadAllText(path), Json) ?? new ListingsDocument()
            : new ListingsDocument();
        return new ListingsFile(path, doc);
    }

    public bool Contains(string id) => _byId.ContainsKey(id);

    public void MarkSeen(string id, DateTimeOffset when)
    {
        if (_byId.TryGetValue(id, out var l)) l.LastSeen = when;
    }

    /// <summary>Adds a new listing, or updates an existing one and records a price change.</summary>
    public void Upsert(Listing incoming)
    {
        if (!_byId.TryGetValue(incoming.Id, out var existing))
        {
            incoming.PriceHistory.Add(new PricePoint(incoming.LastSeen, incoming.PriceEur));
            Document.Listings.Add(incoming);
            _byId[incoming.Id] = incoming;
            return;
        }
        if (existing.PriceEur != incoming.PriceEur)
            existing.PriceHistory.Add(new PricePoint(incoming.LastSeen, incoming.PriceEur));
        incoming.FirstSeen = existing.FirstSeen;
        incoming.PriceHistory = existing.PriceHistory;
        Document.Listings[Document.Listings.IndexOf(existing)] = incoming;
        _byId[incoming.Id] = incoming;
    }

    /// <summary>Writes to a temporary file first, so an interrupted save never damages the existing file.</summary>
    public void Save()
    {
        Document.Updated = DateTimeOffset.Now;
        var tmp = _path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(tmp, JsonSerializer.Serialize(Document, Json));
        File.Move(tmp, _path, overwrite: true);
    }
}
