using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cocktail.Web.Services;

public class CocktailDbApi
{
    private readonly HttpClient http;
    private readonly string baseUrl;

    public CocktailDbApi(HttpClient http, string apiKey, string version)
    {
        this.http = http;
        baseUrl = $"https://www.thecocktaildb.com/api/json/{version}/{apiKey}";
    }

    public async Task<List<CocktailSummary>> ListAlcoholicAsync(CancellationToken ct)
    {
        var resp = await GetWithRetryAsync<DrinkList<CocktailSummary>>(
            $"{baseUrl}/filter.php?a=Alcoholic", ct);
        return resp?.Drinks ?? new();
    }

    public async Task<CocktailDetail?> LookupAsync(string id, CancellationToken ct)
    {
        var resp = await GetWithRetryAsync<DrinkList<CocktailDetail>>(
            $"{baseUrl}/lookup.php?i={id}", ct);
        return resp?.Drinks?.FirstOrDefault();
    }

    private async Task<T?> GetWithRetryAsync<T>(string url, CancellationToken ct)
    {
        // TheCocktailDB returns 429 under load. Back off and retry a few times.
        var delays = new[] { 2_000, 5_000, 15_000, 30_000 };
        for (var attempt = 0; ; attempt++)
        {
            using var resp = await http.GetAsync(url, ct);
            if ((int)resp.StatusCode == 429 && attempt < delays.Length)
            {
                Console.WriteLine($"  ...rate-limited, sleeping {delays[attempt] / 1000}s");
                await Task.Delay(delays[attempt], ct);
                continue;
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
    }
}

public class DrinkList<T>
{
    [JsonPropertyName("drinks")]
    public List<T>? Drinks { get; set; }
}

public class CocktailSummary
{
    [JsonPropertyName("idDrink")] public string IdDrink { get; set; } = "";
    [JsonPropertyName("strDrink")] public string StrDrink { get; set; } = "";
}

public class CocktailDetail
{
    [JsonPropertyName("idDrink")] public string IdDrink { get; set; } = "";
    [JsonPropertyName("strDrink")] public string StrDrink { get; set; } = "";
    [JsonPropertyName("strGlass")] public string? StrGlass { get; set; }
    [JsonPropertyName("strCategory")] public string? StrCategory { get; set; }
    [JsonPropertyName("strInstructions")] public string? StrInstructions { get; set; }

    // The API returns strIngredient1..15 and strMeasure1..15 — capture them via extension data.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public IEnumerable<(string Name, string Amount)> EnumerateIngredients()
    {
        if (Extra is null) yield break;
        for (var i = 1; i <= 15; i++)
        {
            if (!Extra.TryGetValue($"strIngredient{i}", out var ingEl)) continue;
            var name = ingEl.ValueKind == JsonValueKind.String ? ingEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var amount = "";
            if (Extra.TryGetValue($"strMeasure{i}", out var mEl) && mEl.ValueKind == JsonValueKind.String)
            {
                amount = (mEl.GetString() ?? "").Trim();
            }
            yield return (name.Trim(), amount);
        }
    }
}
