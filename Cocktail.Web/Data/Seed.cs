using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Cocktail.Web.Data;

public static class Seed
{
    public const string DataFileName = "cocktail-data.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static void EnsureSeeded(CocktailDb db, string contentRoot)
    {
        db.Database.EnsureCreated();
        if (db.Recipes.Any()) return;

        var path = Path.Combine(contentRoot, "Data", DataFileName);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[seed] {path} not found — DB will start empty.");
            return;
        }

        using var fs = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<SeedFile>(fs, JsonOpts);
        if (data is null) return;

        foreach (var sr in data.Recipes)
        {
            db.Recipes.Add(new Recipe
            {
                Name = sr.Name,
                Glassware = sr.Glassware,
                Method = sr.Method,
                Garnish = sr.Garnish,
                Notes = sr.Notes,
                Ingredients = sr.Ingredients
                    .Select(i => new RecipeIngredient
                    {
                        IngredientName = i.Name,
                        Amount = i.Amount,
                        Order = i.Order,
                    })
                    .ToList(),
                Steps = sr.Steps
                    .Select(s => new RecipeStep { Order = s.Order, Instruction = s.Instruction })
                    .ToList(),
            });
        }

        foreach (var bi in data.BarItems)
        {
            db.BarItems.Add(new BarItem { IngredientName = bi.Name, InStock = bi.InStock });
        }

        db.SaveChanges();
        Console.WriteLine($"[seed] Loaded {data.Recipes.Count} recipes and {data.BarItems.Count} bar items from {path}.");
    }

    public static async Task ExportAsync(CocktailDb db, string contentRoot, CancellationToken ct)
    {
        var recipes = await db.Recipes
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var barItems = await db.BarItems.OrderBy(b => b.IngredientName).ToListAsync(ct);

        var data = new SeedFile
        {
            Recipes = recipes.Select(r => new SeedRecipe
            {
                Name = r.Name,
                Glassware = r.Glassware,
                Method = r.Method,
                Garnish = r.Garnish,
                Notes = r.Notes,
                Ingredients = r.Ingredients
                    .OrderBy(i => i.Order)
                    .Select(i => new SeedIngredient { Name = i.IngredientName, Amount = i.Amount, Order = i.Order })
                    .ToList(),
                Steps = r.Steps
                    .OrderBy(s => s.Order)
                    .Select(s => new SeedStep { Order = s.Order, Instruction = s.Instruction })
                    .ToList(),
            }).ToList(),
            BarItems = barItems
                .Select(b => new SeedBarItem { Name = b.IngredientName, InStock = b.InStock })
                .ToList(),
        };

        var path = Path.Combine(contentRoot, "Data", DataFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, data, JsonOpts, ct);
        Console.WriteLine($"[export] Wrote {data.Recipes.Count} recipes and {data.BarItems.Count} bar items to {path}.");
    }
}

public class SeedFile
{
    public List<SeedRecipe> Recipes { get; set; } = new();
    public List<SeedBarItem> BarItems { get; set; } = new();
}

public class SeedRecipe
{
    public string Name { get; set; } = "";
    public string Glassware { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Garnish { get; set; }
    public string? Notes { get; set; }
    public List<SeedIngredient> Ingredients { get; set; } = new();
    public List<SeedStep> Steps { get; set; } = new();
}

public class SeedIngredient
{
    public string Name { get; set; } = "";
    public string Amount { get; set; } = "";
    public int Order { get; set; }
}

public class SeedStep
{
    public int Order { get; set; }
    public string Instruction { get; set; } = "";
}

public class SeedBarItem
{
    public string Name { get; set; } = "";
    public bool InStock { get; set; }
}
