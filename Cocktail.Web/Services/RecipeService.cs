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

    public async Task<List<RecipeListItem>> SearchAsync(string? query, bool onlyMakeable, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var recipes = await db.Recipes
            .Include(r => r.Ingredients)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var stockNames = await db.BarItems
            .Where(b => b.InStock)
            .Select(b => b.IngredientName.ToLower())
            .ToListAsync(ct);
        var stockSet = stockNames.ToHashSet();

        var q = (query ?? "").Trim();
        var hits = recipes
            .Where(r => string.IsNullOrEmpty(q)
                || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || r.Ingredients.Any(i => i.IngredientName.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .Select(r =>
            {
                var missing = r.Ingredients
                    .Where(i => !stockSet.Contains(i.IngredientName.ToLower()))
                    .Select(i => i.IngredientName)
                    .ToList();
                return new RecipeListItem(r.Id, r.Name, r.Glassware, r.Method, missing);
            })
            .Where(r => !onlyMakeable || r.MissingIngredients.Count == 0)
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
