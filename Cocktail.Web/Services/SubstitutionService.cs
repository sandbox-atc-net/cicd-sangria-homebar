namespace Cocktail.Web.Services;

/// <summary>
/// Curated cocktail-ingredient substitution lookup. Pure, no I/O. The dictionary
/// is intentionally small and hand-tended; if it grows past a few dozen entries
/// it should move to a JSON file alongside cocktail-data.json.
/// </summary>
public class SubstitutionService
{
    private static readonly Dictionary<string, string[]> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["orange bitters"] = new[] { "Angostura bitters (less ideal)", "a thin strip of orange peel expressed over the drink" },
        ["angostura bitters"] = new[] { "Peychaud's bitters", "a few drops of red wine + dash of allspice" },
        ["peychaud's bitters"] = new[] { "Angostura bitters" },
        ["cointreau"] = new[] { "triple sec", "Grand Marnier", "Curaçao" },
        ["triple sec"] = new[] { "Cointreau", "Grand Marnier", "Curaçao" },
        ["grand marnier"] = new[] { "Cointreau", "triple sec" },
        ["sweet vermouth"] = new[] { "dry vermouth + ¼ tsp sugar", "Lillet Rouge" },
        ["dry vermouth"] = new[] { "Lillet Blanc", "dry white wine + a dash of sherry" },
        ["lillet blanc"] = new[] { "Cocchi Americano", "dry vermouth" },
        ["cocchi americano"] = new[] { "Lillet Blanc", "dry vermouth + dash of orange bitters" },
        ["campari"] = new[] { "Aperol (less bitter, sweeter)", "Cynar" },
        ["aperol"] = new[] { "Campari + a splash of simple syrup" },
        ["maraschino liqueur"] = new[] { "kirschwasser", "cherry brandy" },
        ["green chartreuse"] = new[] { "yellow Chartreuse + a dash of dry herbal liqueur" },
        ["yellow chartreuse"] = new[] { "green Chartreuse (more potent)", "Bénédictine" },
        ["benedictine"] = new[] { "Drambuie (sweeter)", "yellow Chartreuse" },
        ["bénédictine"] = new[] { "Drambuie (sweeter)", "yellow Chartreuse" },
        ["lime juice"] = new[] { "lemon juice (in a pinch)" },
        ["lemon juice"] = new[] { "lime juice (slightly more tart)" },
        ["simple syrup"] = new[] { "1:1 sugar dissolved in hot water", "agave syrup (use ¾ amount)" },
        ["agave syrup"] = new[] { "simple syrup", "honey syrup" },
        ["honey syrup"] = new[] { "1:1 honey + warm water", "agave syrup" },
        ["bourbon"] = new[] { "rye whiskey (drier)", "Tennessee whiskey" },
        ["rye whiskey"] = new[] { "bourbon (sweeter, less spicy)" },
        ["scotch"] = new[] { "bourbon (sweeter, no smoke)", "Irish whiskey" },
        ["gin"] = new[] { "vodka (loses botanical character)", "blanco tequila for a different drink entirely" },
        ["white rum"] = new[] { "light rum", "cachaça (grassier)" },
        ["light rum"] = new[] { "white rum", "cachaça" },
        ["dark rum"] = new[] { "aged rum", "spiced rum (sweeter)" },
        ["mezcal"] = new[] { "blanco tequila + a tiny dash of lapsang souchong tea" },
        ["tequila"] = new[] { "mezcal (smokier)", "blanco tequila" },
        ["blanco tequila"] = new[] { "reposado tequila (oakier)", "mezcal" },
        ["egg white"] = new[] { "aquafaba (chickpea brine, ½ oz)" },
        ["heavy cream"] = new[] { "half-and-half", "whole milk + ½ tsp butter" },
        ["club soda"] = new[] { "sparkling water", "tonic water (adds bitterness)" },
        ["tonic water"] = new[] { "club soda + a small pinch of quinine syrup" },
        ["ginger beer"] = new[] { "ginger ale + a dash of fresh lime", "ginger syrup + club soda" },
        ["crème de cacao"] = new[] { "chocolate liqueur", "cocoa-infused vodka" },
        ["creme de cacao"] = new[] { "chocolate liqueur", "cocoa-infused vodka" },
        ["crème de violette"] = new[] { "crème de yvette", "elderflower liqueur (different but floral)" },
        ["creme de violette"] = new[] { "crème de yvette", "elderflower liqueur (different but floral)" },
        ["absinthe"] = new[] { "Pernod", "Herbsaint" },
    };

    public IReadOnlyList<string> Suggest(string ingredientName)
    {
        if (string.IsNullOrWhiteSpace(ingredientName)) return Array.Empty<string>();
        var key = ingredientName.Trim();

        if (Map.TryGetValue(key, out var direct)) return direct;

        var lower = key.ToLowerInvariant();
        foreach (var entry in Map)
        {
            if (lower.Contains(entry.Key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }
        return Array.Empty<string>();
    }
}
