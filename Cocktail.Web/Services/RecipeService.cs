using Cocktail.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Cocktail.Web.Services;

public class RecipeService
{
    private readonly IDbContextFactory<CocktailDb> dbFactory;

    public RecipeService(IDbContextFactory<CocktailDb> dbFactory)
    {
        this.dbFactory = dbFactory;
    }

    public Task<List<RecipeListItem>> SearchAsync(string? query, bool onlyMakeable, CancellationToken ct)
        => SearchAsync(new RecipeSearch { Query = query, OnlyMakeable = onlyMakeable }, ct);

    public async Task<List<RecipeListItem>> SearchAsync(RecipeSearch criteria, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var recipes = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var stockNames = await db.BarItems
            .Where(b => b.InStock)
            .Select(b => b.IngredientName.ToLower())
            .ToListAsync(ct);
        var stockSet = stockNames.ToHashSet();

        var q = (criteria.Query ?? "").Trim();
        var tasteSet = (criteria.Tastes ?? new List<string>())
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
        var familyFilter = (criteria.Family ?? "").Trim().ToLowerInvariant();

        var hits = recipes
            .Where(r =>
            {
                if (!string.IsNullOrEmpty(q))
                {
                    var matches = r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.Ingredients.Any(i => i.IngredientName.Contains(q, StringComparison.OrdinalIgnoreCase));
                    if (!matches) return false;
                }
                if (!string.IsNullOrEmpty(familyFilter) && familyFilter != "all")
                {
                    var hay = (r.TagsCsv + "," + r.Family).ToLowerInvariant();
                    if (!hay.Contains(familyFilter)) return false;
                }
                if (tasteSet.Count > 0)
                {
                    foreach (var t in tasteSet)
                    {
                        var v = t switch
                        {
                            "sweet" => r.TasteSweet,
                            "sour" => r.TasteSour,
                            "bitter" => r.TasteBitter,
                            "spicy" => r.TasteSpicy,
                            "strong" => r.TasteStrong,
                            "refreshing" => r.TasteRefreshing,
                            _ => 0,
                        };
                        if (v < 3) return false;
                    }
                }
                if (!string.IsNullOrEmpty(criteria.ColorFamily))
                {
                    var fam = ColorFamilies.FirstOrDefault(f => f.Id == criteria.ColorFamily);
                    if (fam is null) return false;
                    var palette = string.IsNullOrEmpty(r.Palette) ? r.ColorHex : r.Palette;
                    if (!fam.Match.Contains(palette, StringComparer.OrdinalIgnoreCase)) return false;
                }
                return true;
            })
            .Select(r => Project(r, stockSet))
            .Where(r => !criteria.OnlyMakeable || r.MissingIngredients.Count == 0)
            .ToList();

        return hits;
    }

    public async Task<Recipe?> GetAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Recipes
            .Include(r => r.Ingredients.OrderBy(i => i.Order))
            .Include(r => r.Steps.OrderBy(s => s.Order))
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<List<BarItem>> GetBarItemsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.BarItems.OrderBy(b => b.Category).ThenBy(b => b.IngredientName).ToListAsync(ct);
    }

    public async Task ToggleBarItemAsync(int id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.BarItems.FindAsync(new object?[] { id }, ct);
        if (item is null) return;
        item.InStock = !item.InStock;
        await db.SaveChangesAsync(ct);
    }

    public Task AddBarItemAsync(string ingredientName, CancellationToken ct)
        => SetBarItemStockAsync(ingredientName, true, ct);

    public Task AddBarItemAsync(string ingredientName, string? category, CancellationToken ct)
        => SetBarItemStockAsync(ingredientName, true, category, ct);

    public Task SetBarItemStockAsync(string ingredientName, bool inStock, CancellationToken ct)
        => SetBarItemStockAsync(ingredientName, inStock, null, ct);

    public async Task SetBarItemStockAsync(string ingredientName, bool inStock, string? category, CancellationToken ct)
    {
        var name = (ingredientName ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lower = name.ToLower();
        var existing = await db.BarItems.FirstOrDefaultAsync(b => b.IngredientName.ToLower() == lower, ct);
        if (existing is not null)
        {
            existing.InStock = inStock;
            if (!string.IsNullOrWhiteSpace(category)) existing.Category = category!;
        }
        else if (inStock)
        {
            db.BarItems.Add(new BarItem
            {
                IngredientName = name,
                InStock = true,
                Category = string.IsNullOrWhiteSpace(category) ? "Other" : category!,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<string>> GetInStockNamesAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var names = await db.BarItems
            .Where(b => b.InStock)
            .Select(b => b.IngredientName.ToLower())
            .ToListAsync(ct);
        return names.ToHashSet();
    }

    public async Task<BarStats> GetBarStatsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var bottles = await db.BarItems.ToListAsync(ct);
        var recipes = await db.Recipes.Include(r => r.Ingredients).ToListAsync(ct);

        var stockSet = bottles.Where(b => b.InStock)
            .Select(b => b.IngredientName.ToLowerInvariant())
            .ToHashSet();

        int Missing(Recipe r) => r.Ingredients
            .Where(i => !i.IsGarnish)
            .Count(i => !stockSet.Contains(i.IngredientName.ToLowerInvariant()));

        var ready = recipes.Count(r => Missing(r) == 0);
        var oneAway = recipes.Count(r => Missing(r) == 1);

        return new BarStats(
            BottlesInStock: bottles.Count(b => b.InStock),
            BottlesTotal: bottles.Count,
            ReadyToMake: ready,
            OneBottleAway: oneAway);
    }

    public async Task<List<ShoppingListItem>> GetShoppingListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var recipes = await db.Recipes
            .Include(r => r.Ingredients)
            .ToListAsync(ct);

        var stockNames = await db.BarItems
            .Where(b => b.InStock)
            .Select(b => b.IngredientName.ToLower())
            .ToListAsync(ct);
        var stockSet = stockNames.ToHashSet();

        var byIngredient = new Dictionary<string, ShoppingListAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var recipe in recipes)
        {
            var missing = recipe.Ingredients
                .Where(i => !i.IsGarnish)
                .Select(i => i.IngredientName)
                .Where(n => !stockSet.Contains(n.ToLower()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count == 0) continue;
            var unlocksRecipe = missing.Count == 1;

            foreach (var ingredient in missing)
            {
                if (!byIngredient.TryGetValue(ingredient, out var acc))
                {
                    acc = new ShoppingListAccumulator { DisplayName = ingredient };
                    byIngredient[ingredient] = acc;
                }
                acc.RecipesNeedingThis++;
                if (unlocksRecipe)
                {
                    acc.RecipesUnlockedIfBought++;
                    if (acc.UnlockSamples.Count < 5) acc.UnlockSamples.Add(recipe.Name);
                }
                if (acc.AnySamples.Count < 5) acc.AnySamples.Add(recipe.Name);
            }
        }

        return byIngredient.Values
            .OrderByDescending(a => a.RecipesUnlockedIfBought)
            .ThenByDescending(a => a.RecipesNeedingThis)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(a => new ShoppingListItem(
                a.DisplayName,
                a.RecipesNeedingThis,
                a.RecipesUnlockedIfBought,
                a.RecipesUnlockedIfBought > 0 ? a.UnlockSamples : a.AnySamples))
            .ToList();
    }

    private sealed class ShoppingListAccumulator
    {
        public string DisplayName { get; set; } = "";
        public int RecipesNeedingThis { get; set; }
        public int RecipesUnlockedIfBought { get; set; }
        public List<string> UnlockSamples { get; } = new();
        public List<string> AnySamples { get; } = new();
    }

    private static RecipeListItem Project(Recipe r, HashSet<string> stockSet)
    {
        var missing = r.Ingredients
            .Where(i => !i.IsGarnish && !stockSet.Contains(i.IngredientName.ToLowerInvariant()))
            .Select(i => i.IngredientName)
            .ToList();
        var taste = new TasteProfile(
            r.TasteSweet, r.TasteSour, r.TasteBitter,
            r.TasteSpicy, r.TasteStrong, r.TasteRefreshing);
        return new RecipeListItem(
            Id: r.Id,
            Name: r.Name,
            Glassware: r.Glassware,
            Method: r.Method,
            Glass: r.Glass,
            Tint: r.Tint,
            Family: r.Family,
            Abv: r.Abv,
            TimeMinutes: r.TimeMinutes,
            Difficulty: r.Difficulty,
            ColorHex: r.ColorHex,
            Palette: string.IsNullOrEmpty(r.Palette) ? r.ColorHex : r.Palette,
            Description: r.Description,
            Tags: r.Tags.ToList(),
            Taste: taste,
            MissingIngredients: missing);
    }

    public static readonly IReadOnlyList<ColorFamily> ColorFamilies = new[]
    {
        new ColorFamily("amber",   "Amber",   "linear-gradient(135deg, #ffb347, #a86b2c)", new[]{"#a0521b","#a86b2c","#ffb347"}),
        new ColorFamily("red",     "Ruby",    "linear-gradient(135deg, #ff2d6f, #6b1d2e)", new[]{"#c8264a","#ff8a9a"}),
        new ColorFamily("green",   "Verdant", "linear-gradient(135deg, #c7e84f, #1f6b4a)", new[]{"#c7e84f"}),
        new ColorFamily("pale",    "Pale",    "linear-gradient(135deg, #fff5d6, #ffd980)", new[]{"#fff5d6","#f5e8c8","#ffd980"}),
        new ColorFamily("pink",    "Pink",    "linear-gradient(135deg, #ff8a9a, #ff2d6f)", new[]{"#ff8a9a"}),
    };
}

public record RecipeSearch
{
    public string? Query { get; init; }
    public bool OnlyMakeable { get; init; }
    public List<string>? Tastes { get; init; }
    public string? ColorFamily { get; init; }
    public string? Family { get; init; }
}

public record TasteProfile(int Sweet, int Sour, int Bitter, int Spicy, int Strong, int Refreshing)
{
    public IEnumerable<(string Key, int Value)> Pairs()
    {
        yield return ("sweet", Sweet);
        yield return ("sour", Sour);
        yield return ("bitter", Bitter);
        yield return ("spicy", Spicy);
        yield return ("strong", Strong);
        yield return ("refreshing", Refreshing);
    }

    public string Dominant() => Pairs().OrderByDescending(p => p.Value).First().Key;
}

public record RecipeListItem(
    int Id,
    string Name,
    string Glassware,
    string Method,
    string Glass,
    string Tint,
    string Family,
    string Abv,
    int TimeMinutes,
    string Difficulty,
    string ColorHex,
    string Palette,
    string Description,
    List<string> Tags,
    TasteProfile Taste,
    List<string> MissingIngredients);

public record BarStats(int BottlesInStock, int BottlesTotal, int ReadyToMake, int OneBottleAway);

public record ColorFamily(string Id, string Label, string Swatch, IReadOnlyList<string> Match);

public record ShoppingListItem(
    string IngredientName,
    int RecipesNeedingThis,
    int RecipesUnlockedIfBought,
    List<string> SampleRecipes);

