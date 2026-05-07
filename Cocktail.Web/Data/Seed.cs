namespace Cocktail.Web.Data;

public static class Seed
{
    public static void EnsureSeeded(CocktailDb db)
    {
        db.Database.EnsureCreated();
        if (db.Recipes.Any()) return;

        db.Recipes.AddRange(
            new Recipe
            {
                Name = "Negroni",
                Glassware = "Rocks",
                Method = "Stir",
                Garnish = "Orange peel",
                Ingredients = new()
                {
                    new() { IngredientName = "Gin", Amount = "1 oz", Order = 0 },
                    new() { IngredientName = "Campari", Amount = "1 oz", Order = 1 },
                    new() { IngredientName = "Sweet vermouth", Amount = "1 oz", Order = 2 },
                },
                Steps = new()
                {
                    new() { Order = 0, Instruction = "Add gin, Campari, and sweet vermouth to a mixing glass with ice." },
                    new() { Order = 1, Instruction = "Stir for thirty seconds until well chilled." },
                    new() { Order = 2, Instruction = "Strain into a rocks glass over a large ice cube." },
                    new() { Order = 3, Instruction = "Express an orange peel over the drink and drop it in." },
                },
            },
            new Recipe
            {
                Name = "Old Fashioned",
                Glassware = "Rocks",
                Method = "Build",
                Garnish = "Orange peel",
                Ingredients = new()
                {
                    new() { IngredientName = "Bourbon", Amount = "2 oz", Order = 0 },
                    new() { IngredientName = "Demerara syrup", Amount = "0.25 oz", Order = 1 },
                    new() { IngredientName = "Angostura bitters", Amount = "2 dashes", Order = 2 },
                },
                Steps = new()
                {
                    new() { Order = 0, Instruction = "Add bourbon, demerara syrup, and Angostura bitters to a rocks glass." },
                    new() { Order = 1, Instruction = "Add a large ice cube and stir until chilled and slightly diluted." },
                    new() { Order = 2, Instruction = "Express an orange peel over the drink and drop it in." },
                },
            },
            new Recipe
            {
                Name = "Daiquiri",
                Glassware = "Coupe",
                Method = "Shake",
                Garnish = "Lime wheel",
                Ingredients = new()
                {
                    new() { IngredientName = "White rum", Amount = "2 oz", Order = 0 },
                    new() { IngredientName = "Lime juice", Amount = "0.75 oz", Order = 1 },
                    new() { IngredientName = "Simple syrup", Amount = "0.5 oz", Order = 2 },
                },
                Steps = new()
                {
                    new() { Order = 0, Instruction = "Add white rum, lime juice, and simple syrup to a shaker with ice." },
                    new() { Order = 1, Instruction = "Shake hard for ten to twelve seconds." },
                    new() { Order = 2, Instruction = "Double strain into a chilled coupe glass." },
                    new() { Order = 3, Instruction = "Garnish with a lime wheel." },
                },
            },
            new Recipe
            {
                Name = "Whiskey Sour",
                Glassware = "Rocks",
                Method = "Shake",
                Garnish = "Cherry and orange peel",
                Ingredients = new()
                {
                    new() { IngredientName = "Bourbon", Amount = "2 oz", Order = 0 },
                    new() { IngredientName = "Lemon juice", Amount = "0.75 oz", Order = 1 },
                    new() { IngredientName = "Simple syrup", Amount = "0.75 oz", Order = 2 },
                    new() { IngredientName = "Egg white", Amount = "1", Order = 3 },
                },
                Steps = new()
                {
                    new() { Order = 0, Instruction = "Add bourbon, lemon juice, simple syrup, and egg white to a shaker." },
                    new() { Order = 1, Instruction = "Dry shake without ice for ten seconds to emulsify the egg white." },
                    new() { Order = 2, Instruction = "Add ice and shake hard for another ten seconds." },
                    new() { Order = 3, Instruction = "Strain into a rocks glass over fresh ice." },
                    new() { Order = 4, Instruction = "Garnish with a cherry and an orange peel." },
                },
            });

        db.BarItems.AddRange(
            new BarItem { IngredientName = "Gin", InStock = true },
            new BarItem { IngredientName = "Campari", InStock = true },
            new BarItem { IngredientName = "Sweet vermouth", InStock = true },
            new BarItem { IngredientName = "Bourbon", InStock = true },
            new BarItem { IngredientName = "White rum", InStock = false },
            new BarItem { IngredientName = "Lime juice", InStock = true },
            new BarItem { IngredientName = "Lemon juice", InStock = true },
            new BarItem { IngredientName = "Simple syrup", InStock = true },
            new BarItem { IngredientName = "Demerara syrup", InStock = true },
            new BarItem { IngredientName = "Angostura bitters", InStock = true },
            new BarItem { IngredientName = "Egg white", InStock = false });

        db.SaveChanges();
    }
}
