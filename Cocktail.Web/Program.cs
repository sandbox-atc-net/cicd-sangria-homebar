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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CocktailDb>>();
    using var db = factory.CreateDbContext();
    Seed.EnsureSeeded(db);
}

app.Run();
