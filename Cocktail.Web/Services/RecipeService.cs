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

    public async Task AddBarItemAsync(string ingredientName, CancellationToken ct)
    {
        var name = (ingredientName ?? "").Trim();
        if (string.IsNullOrEmpty(name)) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.BarItems.FirstOrDefaultAsync(b => b.IngredientName == name, ct);
        if (existing is not null)
        {
            existing.InStock = true;
        }
        else
        {
            db.BarItems.Add(new BarItem { IngredientName = name, InStock = true });
        }
        await db.SaveChangesAsync(ct);
    }
}

public record RecipeListItem(int Id, string Name, string Glassware, string Method, List<string> MissingIngredients);
