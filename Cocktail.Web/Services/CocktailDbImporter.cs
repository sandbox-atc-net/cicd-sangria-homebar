using Cocktail.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Cocktail.Web.Services;

public class CocktailDbImporter
{
    private readonly CocktailDbApi api;
    private readonly CocktailDb db;

    public CocktailDbImporter(CocktailDbApi api, CocktailDb db)
    {
        this.api = api;
        this.db = db;
    }

    public async Task<int> ImportAlcoholicAsync(int? maxCount, bool clear, CancellationToken ct)
    {
        if (clear)
        {
            db.RecipeSteps.RemoveRange(db.RecipeSteps);
            db.RecipeIngredients.RemoveRange(db.RecipeIngredients);
            db.Recipes.RemoveRange(db.Recipes);
            db.BarItems.RemoveRange(db.BarItems);
            await db.SaveChangesAsync(ct);
            Console.WriteLine("Cleared existing recipes and bar items.");
        }

        var summaries = await api.ListAlcoholicAsync(ct);
        Console.WriteLine($"Found {summaries.Count} alcoholic drinks in TheCocktailDB.");
        if (maxCount is { } m) summaries = summaries.Take(m).ToList();

        var ingredientNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var skipped = 0;

        foreach (var summary in summaries)
        {
            ct.ThrowIfCancellationRequested();

            if (await db.Recipes.AnyAsync(r => r.Name == summary.StrDrink, ct))
            {
                skipped++;
                continue;
            }

            CocktailDetail? detail;
            try
            {
                detail = await api.LookupAsync(summary.IdDrink, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ! lookup failed for {summary.StrDrink}: {ex.Message}");
                continue;
            }
            if (detail is null) continue;

            var recipe = new Recipe
            {
                Name = detail.StrDrink,
                Glassware = detail.StrGlass ?? "",
                Method = detail.StrCategory ?? "",
            };

            var order = 0;
            foreach (var (name, amount) in detail.EnumerateIngredients())
            {
                recipe.Ingredients.Add(new RecipeIngredient
                {
                    IngredientName = name,
                    Amount = amount,
                    Order = order++,
                });
                ingredientNames.Add(name);
            }

            var stepOrder = 0;
            foreach (var step in SplitSteps(detail.StrInstructions))
            {
                recipe.Steps.Add(new RecipeStep
                {
                    Order = stepOrder++,
                    Instruction = step,
                });
            }

            db.Recipes.Add(recipe);
            imported++;

            if (imported % 25 == 0)
            {
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"  ...imported {imported} so far");
            }

            // Be polite to the API.
            await Task.Delay(250, ct);
        }

        // Auto-create BarItems for every distinct ingredient — out of stock by default.
        var existing = await db.BarItems.Select(b => b.IngredientName).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var addedItems = 0;
        foreach (var name in ingredientNames)
        {
            if (existingSet.Contains(name)) continue;
            db.BarItems.Add(new BarItem { IngredientName = name, InStock = false });
            addedItems++;
        }

        await db.SaveChangesAsync(ct);

        Console.WriteLine($"Done. Imported {imported} recipes ({skipped} already present), added {addedItems} bar items.");
        return imported;
    }

    private static IEnumerable<string> SplitSteps(string? instructions)
    {
        if (string.IsNullOrWhiteSpace(instructions)) yield break;

        var parts = instructions
            .Replace("\r\n", "\n")
            .Replace("\n", " ")
            .Split(new[] { ". ", "! " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim().TrimEnd('.', '!'))
            .Where(p => p.Length > 0);

        foreach (var p in parts)
        {
            yield return p + ".";
        }
    }
}
