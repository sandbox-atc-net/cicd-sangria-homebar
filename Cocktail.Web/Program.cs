using Cocktail.Web.Components;
using Cocktail.Web.Data;
using Cocktail.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "cocktail.db");
builder.Services.AddDbContextFactory<CocktailDb>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<RecipeService>();
builder.Services.AddSingleton<SubstitutionService>();

var app = builder.Build();

// CLI: `dotnet run -- seed-from-file` — load the checked-in JSON into an empty DB
if (args.Length > 0 && args[0].Equals("seed-from-file", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CocktailDb>>();
    using var db = factory.CreateDbContext();
    Seed.EnsureSeeded(db, builder.Environment.ContentRootPath);
    return;
}

// CLI: `dotnet run -- export-data` — dumps the current DB to Data/cocktail-data.json
if (args.Length > 0 && args[0].Equals("export-data", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CocktailDb>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
    await Seed.ExportAsync(db, builder.Environment.ContentRootPath, CancellationToken.None);
    return;
}

// CLI: `dotnet run -- seed-from-api [--clear] [--max N]`
if (args.Length > 0 && args[0].Equals("seed-from-api", StringComparison.OrdinalIgnoreCase))
{
    var apiKey = builder.Configuration["CocktailDbApi:Key"] ?? "1";
    var version = builder.Configuration["CocktailDbApi:Version"] ?? "v1";
    var clear = args.Contains("--clear", StringComparer.OrdinalIgnoreCase);
    int? max = null;
    var maxIdx = Array.FindIndex(args, a => a.Equals("--max", StringComparison.OrdinalIgnoreCase));
    if (maxIdx >= 0 && maxIdx + 1 < args.Length && int.TryParse(args[maxIdx + 1], out var parsed))
    {
        max = parsed;
    }

    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CocktailDb>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();

    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var cocktailApi = new CocktailDbApi(http, apiKey, version);
    var importer = new CocktailDbImporter(cocktailApi, db);
    Console.WriteLine($"Importing from TheCocktailDB ({version}, key {apiKey})...");
    await importer.ImportAlcoholicAsync(max, clear, CancellationToken.None);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CocktailDb>>();
    using var db = factory.CreateDbContext();
    Seed.EnsureSeeded(db, builder.Environment.ContentRootPath);
}

app.Run();
