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
        => SearchAsync(new SearchCriteria(Query: query, OnlyMakeable: onlyMakeable), ct);

    public async Task<List<RecipeListItem>> SearchAsync(SearchCriteria criteria, CancellationToken ct)
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

        var hits = ApplyFilters(recipes, criteria, stockSet).ToList();
        return hits;
    }

    public async Task<RecipeListItem?> GetSurpriseAsync(SearchCriteria criteria, CancellationToken ct)
    {
        var matches = await SearchAsync(criteria, ct);
        if (matches.Count == 0) return null;

        // Use a per-call RNG so concurrent picks don't collide.
        var rng = Random.Shared;
        return matches[rng.Next(matches.Count)];
    }

    public async Task<List<RecipeVariation>> GetVariationsAsync(int recipeId, int max, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var anchor = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == recipeId, ct);
        if (anchor is null) return new List<RecipeVariation>();

        var others = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Where(r => r.Id != recipeId)
            .ToListAsync(ct);

        return others
            .Select(r => new
            {
                Recipe = r,
                Score = RecipeFacets.Similarity(anchor, r),
            })
            .Where(x => x.Score >= 0.34)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Recipe.Name)
            .Take(max)
            .Select(x => new RecipeVariation(
                x.Recipe.Id,
                x.Recipe.Name,
                x.Recipe.Glassware,
                x.Recipe.Method,
                Math.Round(x.Score, 2),
                SharedIngredients(anchor, x.Recipe),
                NewIngredients(anchor, x.Recipe)))
            .ToList();
    }

    public async Task<RecipeFacetsSummary> GetFacetsSummaryAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recipes = await db.Recipes.AsNoTracking().ToListAsync(ct);

        var glassware = recipes
            .Select(r => RecipeFacets.CanonicalGlass(r.Glassware))
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var methods = recipes
            .Select(r => r.Method.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RecipeFacetsSummary(glassware, methods);
    }

    private static IEnumerable<RecipeListItem> ApplyFilters(
        IReadOnlyList<Recipe> recipes,
        SearchCriteria criteria,
        HashSet<string> stockSet)
    {
        var nameQuery = (criteria.Query ?? "").Trim();
        var explicitIngredients = (criteria.Ingredients ?? Array.Empty<string>())
            .Select(i => i.Trim().ToLowerInvariant())
            .Where(i => i.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // If the free-text query reads like "rye and Campari" / "rye, lime", treat it as a
        // multi-ingredient AND-search instead of a single substring match.
        var derivedIngredients = explicitIngredients.Count == 0
            ? RecipeFacets.ParseIngredientList(nameQuery)
            : Array.Empty<string>();
        var ingredientFilter = explicitIngredients.Count > 0
            ? explicitIngredients
            : derivedIngredients.Count > 1 ? derivedIngredients : Array.Empty<string>();

        // When we've redirected the query to multi-ingredient mode, drop it as a name match.
        var effectiveNameQuery = ingredientFilter.Count > 0 ? "" : nameQuery;

        var glass = RecipeFacets.CanonicalGlass(criteria.Glassware);
        var method = (criteria.Method ?? "").Trim();
        var styles = (criteria.Styles ?? Array.Empty<string>())
            .Select(s => s.Trim()).Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var techniques = criteria.Techniques ?? Array.Empty<Technique>();

        foreach (var r in recipes)
        {
            // Name / single-substring text query (only when not interpreting as ingredients).
            if (!string.IsNullOrEmpty(effectiveNameQuery)
                && !r.Name.Contains(effectiveNameQuery, StringComparison.OrdinalIgnoreCase)
                && !r.Ingredients.Any(i => i.IngredientName.Contains(effectiveNameQuery, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Multi-ingredient AND match.
            if (ingredientFilter.Count > 0)
            {
                var ingredientNames = r.Ingredients
                    .Select(i => i.IngredientName.ToLowerInvariant())
                    .ToList();
                if (!ingredientFilter.All(needle => ingredientNames.Any(n => n.Contains(needle))))
                {
                    continue;
                }
            }

            // Glassware (canonicalised, case-insensitive).
            if (!string.IsNullOrEmpty(glass)
                && !string.Equals(RecipeFacets.CanonicalGlass(r.Glassware), glass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Method (source-DB category — exact match after trim).
            if (!string.IsNullOrEmpty(method)
                && !string.Equals(r.Method.Trim(), method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Technique (derived from steps).
            if (techniques.Count > 0)
            {
                var t = RecipeFacets.Technique(r);
                if (!techniques.Contains(t)) continue;
            }

            // Prep-time bucket.
            if (criteria.PrepTime is { } prep && RecipeFacets.PrepTime(r) != prep) continue;

            // Difficulty bucket.
            if (criteria.Difficulty is { } diff && RecipeFacets.Difficulty(r) != diff) continue;

            // Style/flavor (any-of: recipe must hit at least one of the requested styles).
            if (styles.Count > 0)
            {
                var tags = RecipeFacets.Tags(r);
                if (!styles.Any(s => tags.Contains(s, StringComparer.OrdinalIgnoreCase))) continue;
            }

            var missing = r.Ingredients
                .Where(i => !stockSet.Contains(i.IngredientName.ToLower()))
                .Select(i => i.IngredientName)
                .ToList();

            if (criteria.OnlyMakeable && missing.Count > 0) continue;

            yield return new RecipeListItem(r.Id, r.Name, r.Glassware, r.Method, missing);
        }
    }

    private static List<string> SharedIngredients(Recipe a, Recipe b)
    {
        var aSet = a.Ingredients.Select(i => RecipeFacets.Normalise(i.IngredientName)).ToHashSet();
        return b.Ingredients
            .Where(i => aSet.Contains(RecipeFacets.Normalise(i.IngredientName)))
            .Select(i => i.IngredientName)
            .ToList();
    }

    private static List<string> NewIngredients(Recipe anchor, Recipe variant)
    {
        var anchorSet = anchor.Ingredients.Select(i => RecipeFacets.Normalise(i.IngredientName)).ToHashSet();
        return variant.Ingredients
            .Where(i => !anchorSet.Contains(RecipeFacets.Normalise(i.IngredientName)))
            .Select(i => i.IngredientName)
            .ToList();
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
        return await db.BarItems.OrderBy(b => b.IngredientName).ToListAsync(ct);
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

    public async Task SetBarItemStockAsync(string ingredientName, bool inStock, CancellationToken ct)
    {
        var name = (ingredientName ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lower = name.ToLower();
        var existing = await db.BarItems.FirstOrDefaultAsync(b => b.IngredientName.ToLower() == lower, ct);
        if (existing is not null)
        {
            existing.InStock = inStock;
        }
        else if (inStock)
        {
            db.BarItems.Add(new BarItem { IngredientName = name, InStock = true });
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
}

public record RecipeListItem(int Id, string Name, string Glassware, string Method, List<string> MissingIngredients);

public record ShoppingListItem(
    string IngredientName,
    int RecipesNeedingThis,
    int RecipesUnlockedIfBought,
    List<string> SampleRecipes);

public sealed record SearchCriteria(
    string? Query = null,
    bool OnlyMakeable = false,
    IReadOnlyList<string>? Ingredients = null,
    IReadOnlyList<string>? Styles = null,
    IReadOnlyList<Technique>? Techniques = null,
    string? Glassware = null,
    string? Method = null,
    PrepTimeBucket? PrepTime = null,
    DifficultyLevel? Difficulty = null);

public sealed record RecipeFacetsSummary(
    IReadOnlyList<string> Glassware,
    IReadOnlyList<string> Methods);

public sealed record RecipeVariation(
    int Id,
    string Name,
    string Glassware,
    string Method,
    double Similarity,
    IReadOnlyList<string> SharedIngredients,
    IReadOnlyList<string> NewIngredients);
