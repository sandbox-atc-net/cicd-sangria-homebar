using System.Globalization;

namespace Cocktail.Web.Services;

/// <summary>
/// Cocktail-relevant volume conversions. Canonical unit is millilitres.
/// </summary>
public static class UnitConverter
{
    private static readonly Dictionary<string, decimal> ToMl = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ml"] = 1m,
        ["milliliter"] = 1m,
        ["milliliters"] = 1m,
        ["cl"] = 10m,
        ["centiliter"] = 10m,
        ["centiliters"] = 10m,
        ["oz"] = 29.5735m,
        ["ounce"] = 29.5735m,
        ["ounces"] = 29.5735m,
        ["fluid ounce"] = 29.5735m,
        ["fluid ounces"] = 29.5735m,
        ["tsp"] = 4.92892m,
        ["teaspoon"] = 4.92892m,
        ["teaspoons"] = 4.92892m,
        ["tbsp"] = 14.7868m,
        ["tablespoon"] = 14.7868m,
        ["tablespoons"] = 14.7868m,
        ["dash"] = 0.92m,
        ["dashes"] = 0.92m,
        ["barspoon"] = 5m,
        ["barspoons"] = 5m,
        ["bsp"] = 5m,
        ["cup"] = 236.588m,
        ["cups"] = 236.588m,
    };

    public static bool TryConvert(decimal amount, string fromUnit, string toUnit, out decimal result)
    {
        result = 0;
        if (!ToMl.TryGetValue(fromUnit, out var fromFactor)) return false;
        if (!ToMl.TryGetValue(toUnit, out var toFactor)) return false;
        result = amount * fromFactor / toFactor;
        return true;
    }

    /// <summary>
    /// Returns a singular display form for a unit (e.g. "ounces" → "ounce").
    /// </summary>
    public static string Singular(string unit)
    {
        unit = unit.Trim().ToLowerInvariant();
        return unit switch
        {
            "ml" or "milliliters" or "milliliter" => "ml",
            "cl" or "centiliters" or "centiliter" => "cl",
            "oz" or "ounces" or "ounce" or "fluid ounces" or "fluid ounce" => "oz",
            "tsp" or "teaspoons" or "teaspoon" => "tsp",
            "tbsp" or "tablespoons" or "tablespoon" => "tbsp",
            "dash" or "dashes" => "dash",
            "barspoon" or "barspoons" or "bsp" => "barspoon",
            "cup" or "cups" => "cup",
            _ => unit,
        };
    }

    /// <summary>
    /// Returns a TTS-friendly unit name (e.g. "ml" → "millilitres",
    /// pluralised for the given amount).
    /// </summary>
    public static string Spoken(string unit, decimal amount)
    {
        var s = Singular(unit);
        var plural = amount != 1m;
        return s switch
        {
            "ml" => plural ? "millilitres" : "millilitre",
            "cl" => plural ? "centilitres" : "centilitre",
            "oz" => plural ? "ounces" : "ounce",
            "tsp" => plural ? "teaspoons" : "teaspoon",
            "tbsp" => plural ? "tablespoons" : "tablespoon",
            "dash" => plural ? "dashes" : "dash",
            "barspoon" => plural ? "barspoons" : "barspoon",
            "cup" => plural ? "cups" : "cup",
            _ => s,
        };
    }

    /// <summary>
    /// Friendly numeric formatting: integers stay clean, otherwise round to
    /// two decimals and trim trailing zeros.
    /// </summary>
    public static string FormatNumber(decimal value)
    {
        if (value == decimal.Truncate(value)) return value.ToString("0", CultureInfo.InvariantCulture);
        var rounded = Math.Round(value, 2);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
