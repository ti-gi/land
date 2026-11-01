using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;

namespace DataFetch.Tests;

/// <summary>
/// In-memory stand-in for the nekretnine.hr listing API (plugged into HttpClient, no network).
/// 60 synthetic ads, 25 per page: every 4th is agricultural land (269), one is in Zagrebačka županija,
/// one hides its price, every 6th has no coordinates, every 7th is promoted with phone/e-mail in its description.
/// </summary>
public sealed class FakeNekretnineApi : HttpMessageHandler
{
    public enum Mode
    {
        Normal,
        /// <summary>Second scan: ad 7000000 cheaper, 7000001/7000002 gone, new ad 7999999 at the top.</summary>
        Changed,
        Http403OnPage1,
        Http403OnPage3,
        CaptchaPage,
        HtmlMaintenancePage,
    }

    public const int PerPage = 25;
    static readonly string[] Zones = { "Novi Zagreb - zapad", "Sesvete", "Podsljeme", "Brezovica", "Trešnjevka - jug", "Stenjevec", "Gornja Dubrava" };

    readonly JsonObject _template;
    public Mode Current { get; set; } = Mode.Normal;
    public List<string> Requests { get; } = new();
    public List<string> UserAgents { get; } = new();

    public FakeNekretnineApi(string templateFile) =>
        _template = JsonNode.Parse(File.ReadAllText(templateFile))!.AsObject();

    public List<JsonObject> Ads()
    {
        var ads = Enumerable.Range(0, 60).Select(Ad).ToList();
        if (Current == Mode.Changed)
        {
            ads.RemoveAll(a => Id(a) is 7000001 or 7000002);
            var fresh = Ad(0);
            fresh["realEstate"]!["id"] = 7999999;
            fresh["seo"]!["url"] = "https://www.nekretnine.hr/oglasi/7999999/";
            ads.Insert(0, fresh);
        }
        return ads;
    }

    static long Id(JsonObject ad) => long.Parse(ad["realEstate"]!["id"]!.ToJsonString());

    JsonObject Ad(int i)
    {
        var a = _template.DeepClone().AsObject();
        a.Remove("_comment");
        var re = a["realEstate"]!.AsObject();
        var p = re["properties"]![0]!.AsObject();
        var loc = p["location"]!.AsObject();

        var id = 7000000 + i;
        var agricultural = i % 4 == 3;
        var (typId, typName) = agricultural ? (269, "Poljoprivredno zemljište") : (261, "Građevinsko zemljište");
        var area = 300 + (i * 137) % 2700;
        var price = area * (80 + (i * 53) % 320);
        if (Current == Mode.Changed && i == 0) price -= 5000;
        var zone = Zones[i % Zones.Length];
        var promoted = i % 7 == 0;

        re["id"] = id;
        re["uuid"] = $"00000000-0000-0000-0000-{id:D12}";
        re["title"] = $"{typName}, {zone}, Zagreb";
        re["typology"] = new JsonObject { ["id"] = typId, ["name"] = typName };
        p["typology"] = new JsonObject { ["id"] = typId, ["name"] = typName };
        var priceObj = new JsonObject { ["visible"] = i != 5, ["value"] = price, ["formattedValue"] = $"€ {price:N0}".Replace(",", "."), ["priceRange"] = "x" };
        re["price"] = priceObj;
        p["price"] = priceObj.DeepClone();
        re["visibility"] = promoted ? "top" : "premium";
        re["isNew"] = i < 3;
        re["advertiser"]!["agency"]!["id"] = 900 + i % 3;
        re["advertiser"]!["agency"]!["displayName"] = $"Agencija {i % 3}";
        p["caption"] = $"Zemljište {area} m², {zone}";
        p["description"] = promoted ? $"Prodaje se zemljište. Info na 091 555 1234 ili prodaja{i}@primjer.hr" : "";
        p["ga4features"] = i % 2 == 0 ? new JsonArray("allaccio elettricità", "allaccio acqua")
                         : i % 3 == 0 ? new JsonArray("allaccio fogne", "illuminazione pubblica") : new JsonArray();
        p["surface"] = $"{area:N0} m²".Replace(",", ".");
        var photos = new JsonArray();
        for (var k = 0; k < i % 5; k++) photos.Add(new JsonObject { ["id"] = id * 10 + k });
        p["multimedia"]!["photos"] = photos;
        loc["province"] = i == 10 ? "Zagrebačka" : "Grad Zagreb";
        loc["macrozone"] = zone;
        loc["latitude"] = i % 6 == 0 ? null : 45.75 + i / 1000.0;
        loc["longitude"] = i % 6 == 0 ? null : 15.90 + i / 1000.0;
        loc["marker"] = i % 6 == 0 ? "no_map" : i % 5 == 0 ? "only_area" : "marker";
        a["seo"]!["url"] = $"https://www.nekretnine.hr/oglasi/{id}/";
        return a;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        Requests.Add(uri.ToString());
        UserAgents.Add(request.Headers.UserAgent.ToString());
        var page = int.Parse(HttpUtility.ParseQueryString(uri.Query)["pag"] ?? "1");

        if (!uri.AbsolutePath.StartsWith("/api-next/search-list/listings/"))
            return Respond(HttpStatusCode.NotFound, "text/plain", "not found");
        if (Current == Mode.Http403OnPage1 || (Current == Mode.Http403OnPage3 && page == 3))
            return Respond(HttpStatusCode.Forbidden, "application/json", "{\"url\":\"https://geo.captcha-delivery.com/captcha/?initialCid=x\"}");
        if (Current == Mode.CaptchaPage)
            return Respond(HttpStatusCode.OK, "text/html", "<html><head><title>nekretnine.hr</title></head><script src=\"https://ct.captcha-delivery.com/c.js\"></script></html>");
        if (Current == Mode.HtmlMaintenancePage)
            return Respond(HttpStatusCode.OK, "text/html", "<html><head><title>Održavanje</title></head></html>");

        var ads = Ads();
        var maxPages = (ads.Count + PerPage - 1) / PerPage;
        var results = new JsonArray(ads.Skip((page - 1) * PerPage).Take(PerPage).Select(a => (JsonNode)a).ToArray());
        var body = new JsonObject
        {
            ["count"] = ads.Count, ["totalAds"] = ads.Count, ["currentPage"] = page, ["maxPages"] = maxPages,
            ["isResultsLimitReached"] = false, ["results"] = results, ["seoData"] = new JsonObject(),
        };
        return Respond(HttpStatusCode.OK, "application/json; charset=utf-8", body.ToJsonString());
    }

    static Task<HttpResponseMessage> Respond(HttpStatusCode code, string contentType, string body) =>
        Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8) { Headers = { ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType) } } });
}
