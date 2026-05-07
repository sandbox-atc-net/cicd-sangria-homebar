using System.Globalization;
using System.Text.RegularExpressions;
using Cocktail.Web.Data;

namespace Cocktail.Web.Services;

/// <summary>
/// Pure heuristics that derive style tags, technique, prep-time, difficulty, and similarity from
/// the existing recipe shape (name, glassware, method, ingredients, steps). No I/O, no migrations.
/// Wrong on edge cases by design — the upstream data is uneven, so these are useful defaults
/// rather than ground truth.
/// </summary>
public static class RecipeFacets
{
    public static readonly IReadOnlyList<string> AllStyles = new[]
    {
        Style.Bitter, Style.Stirred, Style.LowAbv, Style.Brunch, Style.AfterDinner,
    };

    public static readonly IReadOnlyList<Technique> AllTechniques = new[]
    {
        Services.Technique.Shake, Services.Technique.Stir, Services.Technique.Build,
        Services.Technique.Muddle, Services.Technique.Blend, Services.Technique.Layer,
    };

    public static class Style
    {
        public const string Bitter = "Bitter";
        public const string Stirred = "Stirred";
        public const string LowAbv = "Low-ABV";
        public const string Brunch = "Brunch";
        public const string AfterDinner = "After-dinner";
    }

    public static IReadOnlyList<string> Tags(Recipe recipe)
    {
        var tags = new List<string>(5);
        if (IsBitter(recipe)) tags.Add(Style.Bitter);
        if (IsStirred(recipe)) tags.Add(Style.Stirred);
        if (IsLowAbv(recipe)) tags.Add(Style.LowAbv);
        if (IsBrunch(recipe)) tags.Add(Style.Brunch);
        if (IsAfterDinner(recipe)) tags.Add(Style.AfterDinner);
        return tags;
    }

    public static Technique Technique(Recipe recipe)
    {
        var stepText = string.Join(' ', recipe.Steps.Select(s => s.Instruction)).ToLowerInvariant();
        var method = (recipe.Method ?? "").ToLowerInvariant();

        if (Contains(stepText, "muddle")) return Services.Technique.Muddle;
        if (Contains(stepText, "blend") || method == "shake" && Contains(stepText, "blender")) return Services.Technique.Blend;
        if (Contains(stepText, "layer") || Contains(stepText, "float") || Contains(stepText, "pousse")) return Services.Technique.Layer;
        if (Contains(stepText, "shake") || method == "shake") return Services.Technique.Shake;
        if (Contains(stepText, "stir")) return Services.Technique.Stir;
        return Services.Technique.Build;
    }

    public static PrepTimeBucket PrepTime(Recipe recipe)
    {
        var ingredients = recipe.Ingredients.Count;
        var steps = recipe.Steps.Count;
        var technique = Technique(recipe);

        if (technique is Services.Technique.Muddle or Services.Technique.Blend
            || ingredients >= 7 || steps >= 5)
        {
            return PrepTimeBucket.Long;
        }
        if (ingredients <= 3 && steps <= 2 && technique is Services.Technique.Build) return PrepTimeBucket.Quick;
        return PrepTimeBucket.Medium;
    }

    public static DifficultyLevel Difficulty(Recipe recipe)
    {
        var ingredients = recipe.Ingredients.Count;
        var steps = recipe.Steps.Count;
        var technique = Technique(recipe);
        var stepText = string.Join(' ', recipe.Steps.Select(s => s.Instruction)).ToLowerInvariant();

        var advancedSignals =
            Contains(stepText, "flame") || Contains(stepText, "torch") || Contains(stepText, "fat-wash")
            || Contains(stepText, "infuse") || Contains(stepText, "clarify") || Contains(stepText, "bottle")
            || ingredients >= 8 || technique is Services.Technique.Layer;

        if (advancedSignals) return DifficultyLevel.Advanced;
        if (ingredients <= 3 && technique is Services.Technique.Build && steps <= 2) return DifficultyLevel.Easy;
        if (ingredients >= 6 || technique is Services.Technique.Muddle or Services.Technique.Blend) return DifficultyLevel.Advanced;
        return DifficultyLevel.Medium;
    }

    /// <summary>
    /// Canonicalise glassware so "Highball Glass" and "Highball glass" merge.
    /// Returns title-cased form trimmed of surplus whitespace; empty for missing.
    /// </summary>
    public static string CanonicalGlass(string? glass)
    {
        var trimmed = (glass ?? "").Trim();
        if (trimmed.Length == 0) return "";
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }

    /// <summary>
    /// Jaccard similarity over normalised ingredient names. 0 = no overlap, 1 = identical sets.
    /// </summary>
    public static double Similarity(Recipe a, Recipe b)
    {
        var aSet = a.Ingredients.Select(i => Normalise(i.IngredientName)).ToHashSet();
        var bSet = b.Ingredients.Select(i => Normalise(i.IngredientName)).ToHashSet();
        if (aSet.Count == 0 || bSet.Count == 0) return 0;

        aSet.IntersectWith(bSet);
        var intersect = aSet.Count;
        var union = a.Ingredients.Select(i => Normalise(i.IngredientName))
            .Union(b.Ingredients.Select(i => Normalise(i.IngredientName)))
            .Count();
        return union == 0 ? 0 : (double)intersect / union;
    }

    /// <summary>
    /// Pull ingredient terms out of free-form input like "what can I make with rye and Campari?".
    /// Strips the common interrogative wrapper, splits on commas, '&', '+', and " and ".
    /// Returns lowercased, deduped, non-empty terms in original order.
    /// </summary>
    public static IReadOnlyList<string> ParseIngredientList(string? input)
    {
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return Array.Empty<string>();

        // Drop a leading "what can I make with " / "show me drinks with " etc.
        raw = WrapperPrefix.Replace(raw, "");
        raw = raw.TrimEnd('?', '.', '!').Trim();

        var parts = SplitPattern.Split(raw)
            .Select(p => p.Trim().Trim('"', '\''))
            .Where(p => p.Length > 0)
            .Select(p => p.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return parts;
    }

    private static readonly Regex WrapperPrefix = new(
        @"^(what\s+can\s+i\s+(make|mix)\s+with\s+|show\s+me\s+(drinks|cocktails|recipes)\s+with\s+|drinks?\s+with\s+|cocktails?\s+with\s+|using\s+|with\s+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SplitPattern = new(
        @"\s*(?:,|\band\b|\+|&|/)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Normalise(string name)
        => (name ?? "").Trim().ToLowerInvariant();

    private static bool Contains(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsBitter(Recipe r)
    {
        foreach (var ing in r.Ingredients)
        {
            var name = ing.IngredientName.ToLowerInvariant();
            if (name.Contains("campari") || name.Contains("aperol") || name.Contains("cynar")
                || name.Contains("fernet") || name.Contains("amaro") || name.Contains("bitters")
                || name.Contains("suze") || name.Contains("punt e mes"))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsStirred(Recipe r)
    {
        var stepText = string.Join(' ', r.Steps.Select(s => s.Instruction));
        if (Contains(stepText, "stir")) return true;
        // Spirit-forward stirred classics rarely contain juice or egg white.
        var noCitrusOrEgg = !r.Ingredients.Any(i =>
        {
            var n = i.IngredientName.ToLowerInvariant();
            return n.Contains("juice") || n.Contains("egg") || n.Contains("cream") || n.Contains("milk");
        });
        var spiritForward = r.Ingredients.Count is >= 2 and <= 4
            && r.Ingredients.Any(i => IsSpirit(i.IngredientName));
        return noCitrusOrEgg && spiritForward
            && Technique(r) is not Services.Technique.Shake and not Services.Technique.Blend;
    }

    private static bool IsLowAbv(Recipe r)
    {
        var ingredients = r.Ingredients.Select(i => i.IngredientName.ToLowerInvariant()).ToList();
        if (ingredients.Count == 0) return false;

        var spirits = ingredients.Count(IsSpirit);
        if (spirits == 0) return false;                  // 0 spirits = mocktail, not low-ABV
        if (spirits >= 2) return false;                  // multiple spirits ⇒ probably stiff

        var hasLengthener = ingredients.Any(n =>
            n.Contains("soda") || n.Contains("tonic") || n.Contains("ginger beer")
            || n.Contains("ginger ale") || n.Contains("sparkling") || n.Contains("prosecco")
            || n.Contains("champagne") || n.Contains("beer") || n.Contains("wine")
            || n.Contains("vermouth") || n.Contains("sherry") || n.Contains("lillet"));

        return hasLengthener;
    }

    private static bool IsBrunch(Recipe r)
    {
        var ingredients = r.Ingredients.Select(i => i.IngredientName.ToLowerInvariant()).ToList();
        return ingredients.Any(n =>
            n.Contains("coffee") || n.Contains("espresso") || n.Contains("orange juice")
            || n.Contains("tomato") || n.Contains("champagne") || n.Contains("prosecco")
            || n.Contains("sparkling wine") || n.Contains("egg") || n.Contains("milk")
            || n.Contains("grapefruit"));
    }

    private static bool IsAfterDinner(Recipe r)
    {
        var ingredients = r.Ingredients.Select(i => i.IngredientName.ToLowerInvariant()).ToList();
        return ingredients.Any(n =>
            n.Contains("crème de cacao") || n.Contains("creme de cacao")
            || n.Contains("coffee liqueur") || n.Contains("kahlua") || n.Contains("kahlúa")
            || n.Contains("amaretto") || n.Contains("baileys") || n.Contains("irish cream")
            || n.Contains("cream") || n.Contains("cognac") || n.Contains("port")
            || n.Contains("sherry") || n.Contains("fernet") || n.Contains("amaro")
            || n.Contains("chocolate"));
    }

    private static bool IsSpirit(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("gin") || n.Contains("vodka") || n.Contains("rum")
            || n.Contains("tequila") || n.Contains("mezcal") || n.Contains("whisk")
            || n.Contains("bourbon") || n.Contains("rye") || n.Contains("scotch")
            || n.Contains("brandy") || n.Contains("cognac") || n.Contains("pisco")
            || n.Contains("cachaça") || n.Contains("cachaca") || n.Contains("absinthe");
    }
}

public enum PrepTimeBucket
{
    Quick,
    Medium,
    Long,
}

public enum DifficultyLevel
{
    Easy,
    Medium,
    Advanced,
}

public enum Technique
{
    Build,
    Shake,
    Stir,
    Muddle,
    Blend,
    Layer,
}
