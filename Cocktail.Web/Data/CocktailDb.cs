using Microsoft.EntityFrameworkCore;

namespace Cocktail.Web.Data;

public class CocktailDb : DbContext
{
    public CocktailDb(DbContextOptions<CocktailDb> options) : base(options) { }

    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<BarItem> BarItems => Set<BarItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Recipe>().HasIndex(r => r.Name).IsUnique();
        b.Entity<BarItem>().HasIndex(i => i.IngredientName).IsUnique();

        b.Entity<Recipe>()
            .HasMany(r => r.Ingredients)
            .WithOne()
            .HasForeignKey(i => i.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<Recipe>()
            .HasMany(r => r.Steps)
            .WithOne()
            .HasForeignKey(s => s.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class Recipe
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    // Free-form / legacy fields kept for compatibility with the external import path.
    public string Glassware { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Garnish { get; set; }
    public string? Notes { get; set; }

    // Sangria design fields. Glass is one of: coupe, rocks, tiki, highball, hurricane, marg.
    public string Glass { get; set; } = "rocks";
    // Tint is one of: papaya, hibiscus, mango, lime, jungle, bitters.
    public string Tint { get; set; } = "papaya";
    // Family is one of: Stirred, Shaken, Built, Blended.
    public string Family { get; set; } = "Stirred";
    // Abv is one of: Low, Medium, High.
    public string Abv { get; set; } = "Medium";
    public int TimeMinutes { get; set; } = 3;
    public string Difficulty { get; set; } = "Easy";
    public string Description { get; set; } = "";
    public string? Tip { get; set; }
    public string ColorHex { get; set; } = "#ff6b3d";
    public string Palette { get; set; } = "#ff6b3d";
    // Comma-separated tags for simple SQLite storage.
    public string TagsCsv { get; set; } = "";

    public int TasteSweet { get; set; }
    public int TasteSour { get; set; }
    public int TasteBitter { get; set; }
    public int TasteSpicy { get; set; }
    public int TasteStrong { get; set; }
    public int TasteRefreshing { get; set; }

    public List<RecipeIngredient> Ingredients { get; set; } = new();
    public List<RecipeStep> Steps { get; set; } = new();

    public IEnumerable<string> Tags =>
        string.IsNullOrEmpty(TagsCsv)
            ? Array.Empty<string>()
            : TagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string IngredientName { get; set; } = "";
    public string Amount { get; set; } = "";
    public int Order { get; set; }
    public string? Sub { get; set; }
    public bool IsGarnish { get; set; }
}

public class RecipeStep
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int Order { get; set; }
    public string Instruction { get; set; } = "";
    public string Title { get; set; } = "";
    public int? TimerSeconds { get; set; }
    public string? Action { get; set; }
}

public class BarItem
{
    public int Id { get; set; }
    public string IngredientName { get; set; } = "";
    public bool InStock { get; set; }
    public string Category { get; set; } = "Other";
}
