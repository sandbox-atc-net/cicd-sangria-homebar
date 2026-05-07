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
    public string Glassware { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Garnish { get; set; }
    public string? Notes { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = new();
    public List<RecipeStep> Steps { get; set; } = new();
}

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public string IngredientName { get; set; } = "";
    public string Amount { get; set; } = "";
    public int Order { get; set; }
}

public class RecipeStep
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int Order { get; set; }
    public string Instruction { get; set; } = "";
}

public class BarItem
{
    public int Id { get; set; }
    public string IngredientName { get; set; } = "";
    public bool InStock { get; set; }
}
