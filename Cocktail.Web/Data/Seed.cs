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
                Glass = sr.Glass,
                Tint = sr.Tint,
                Family = sr.Family,
                Abv = sr.Abv,
                TimeMinutes = sr.TimeMinutes,
                Difficulty = sr.Difficulty,
                Description = sr.Description,
                Tip = sr.Tip,
                ColorHex = sr.ColorHex,
                Palette = sr.Palette,
                TagsCsv = string.Join(",", sr.Tags ?? new List<string>()),
                TasteSweet = sr.Taste?.Sweet ?? 0,
                TasteSour = sr.Taste?.Sour ?? 0,
                TasteBitter = sr.Taste?.Bitter ?? 0,
                TasteSpicy = sr.Taste?.Spicy ?? 0,
                TasteStrong = sr.Taste?.Strong ?? 0,
                TasteRefreshing = sr.Taste?.Refreshing ?? 0,
                Ingredients = sr.Ingredients
                    .Select(i => new RecipeIngredient
                    {
                        IngredientName = i.Name,
                        Amount = i.Amount,
                        Order = i.Order,
                        Sub = i.Sub,
                        IsGarnish = i.IsGarnish,
                    })
                    .ToList(),
                Steps = sr.Steps
                    .Select(s => new RecipeStep
                    {
                        Order = s.Order,
                        Instruction = s.Instruction,
                        Title = s.Title ?? "",
                        TimerSeconds = s.TimerSeconds,
                        Action = s.Action,
                    })
                    .ToList(),
            });
        }

        foreach (var bi in data.BarItems)
        {
            db.BarItems.Add(new BarItem
            {
                IngredientName = bi.Name,
                InStock = bi.InStock,
                Category = bi.Category ?? "Other",
            });
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
                Glass = r.Glass,
                Tint = r.Tint,
                Family = r.Family,
                Abv = r.Abv,
                TimeMinutes = r.TimeMinutes,
                Difficulty = r.Difficulty,
                Description = r.Description,
                Tip = r.Tip,
                ColorHex = r.ColorHex,
                Palette = r.Palette,
                Tags = r.Tags.ToList(),
                Taste = new SeedTaste
                {
                    Sweet = r.TasteSweet,
                    Sour = r.TasteSour,
                    Bitter = r.TasteBitter,
                    Spicy = r.TasteSpicy,
                    Strong = r.TasteStrong,
                    Refreshing = r.TasteRefreshing,
                },
                Ingredients = r.Ingredients
                    .OrderBy(i => i.Order)
                    .Select(i => new SeedIngredient
                    {
                        Name = i.IngredientName,
                        Amount = i.Amount,
                        Order = i.Order,
                        Sub = i.Sub,
                        IsGarnish = i.IsGarnish,
                    })
                    .ToList(),
                Steps = r.Steps
                    .OrderBy(s => s.Order)
                    .Select(s => new SeedStep
                    {
                        Order = s.Order,
                        Title = s.Title,
                        Instruction = s.Instruction,
                        TimerSeconds = s.TimerSeconds,
                        Action = s.Action,
                    })
                    .ToList(),
            }).ToList(),
            BarItems = barItems
                .Select(b => new SeedBarItem
                {
                    Name = b.IngredientName,
                    InStock = b.InStock,
                    Category = b.Category,
                })
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
    public string Glass { get; set; } = "rocks";
    public string Tint { get; set; } = "papaya";
    public string Family { get; set; } = "Stirred";
    public string Abv { get; set; } = "Medium";
    public int TimeMinutes { get; set; } = 3;
    public string Difficulty { get; set; } = "Easy";
    public string Description { get; set; } = "";
    public string? Tip { get; set; }
    public string ColorHex { get; set; } = "#ff6b3d";
    public string Palette { get; set; } = "#ff6b3d";
    public List<string> Tags { get; set; } = new();
    public SeedTaste? Taste { get; set; }
    public List<SeedIngredient> Ingredients { get; set; } = new();
    public List<SeedStep> Steps { get; set; } = new();
}

public class SeedTaste
{
    public int Sweet { get; set; }
    public int Sour { get; set; }
    public int Bitter { get; set; }
    public int Spicy { get; set; }
    public int Strong { get; set; }
    public int Refreshing { get; set; }
}

public class SeedIngredient
{
    public string Name { get; set; } = "";
    public string Amount { get; set; } = "";
    public int Order { get; set; }
    public string? Sub { get; set; }
    public bool IsGarnish { get; set; }
}

public class SeedStep
{
    public int Order { get; set; }
    public string? Title { get; set; }
    public string Instruction { get; set; } = "";
    public int? TimerSeconds { get; set; }
    public string? Action { get; set; }
}

public class SeedBarItem
{
    public string Name { get; set; } = "";
    public bool InStock { get; set; }
    public string? Category { get; set; }
}
