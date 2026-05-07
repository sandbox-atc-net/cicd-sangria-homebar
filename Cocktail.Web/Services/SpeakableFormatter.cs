using System.Globalization;
using System.Text.RegularExpressions;

namespace Cocktail.Web.Services;

/// <summary>
/// Turns ingredient amount strings ("2 oz", "1/2 oz", "0.75 oz") into
/// spoken English ("two ounces", "half an ounce", "three quarters of an ounce").
/// Unknown shapes fall back to the original text.
/// </summary>
public static class SpeakableFormatter
{
    private static readonly Regex AmountPattern = new(
        @"^\s*(?:(?<whole>\d+)\s+)?(?:(?<num>\d+)\s*/\s*(?<den>\d+)|(?<dec>\d+\.\d+)|(?<int>\d+))\s*(?<unit>[a-zA-Z]+(?:\s+[a-zA-Z]+)?)?\s*$",
        RegexOptions.Compiled);

    private static readonly string[] Cardinal0To19 =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen",
        "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety",
    };

    public static string FormatAmount(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();

        var m = AmountPattern.Match(raw);
        if (!m.Success) return raw;

        if (!TryExtractValue(m, out var value)) return raw;

        var unitRaw = m.Groups["unit"].Success ? m.Groups["unit"].Value : "";
        var unitKnown = !string.IsNullOrEmpty(unitRaw)
            && UnitConverter.Singular(unitRaw) != unitRaw.ToLowerInvariant().Trim();

        if (!unitKnown)
        {
            return string.IsNullOrEmpty(unitRaw)
                ? SpellOut(value)
                : $"{SpellOut(value)} {unitRaw}";
        }

        var singular = UnitConverter.Spoken(unitRaw, 1m);
        var article = StartsWithVowel(singular) ? "an" : "a";

        if (value == 0.5m) return $"half {article} {singular}";
        if (value == 0.25m) return $"a quarter of {article} {singular}";
        if (value == 0.75m) return $"three quarters of {article} {singular}";
        if (value == 1m) return $"one {singular}";

        return $"{SpellOut(value)} {UnitConverter.Spoken(unitRaw, value)}";
    }

    public static string SpellOut(decimal value)
    {
        if (value == 0.25m) return "a quarter";
        if (value == 0.5m) return "half";
        if (value == 0.75m) return "three quarters";

        var whole = (int)decimal.Truncate(value);
        var frac = value - whole;

        var fracText = frac switch
        {
            0m => null,
            0.25m => "a quarter",
            0.5m => "a half",
            0.75m => "three quarters",
            _ => null,
        };

        if (whole == 0 || (frac != 0m && fracText is null))
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
        if (fracText is null) return SpellInt(whole);
        return $"{SpellInt(whole)} and {fracText}";
    }

    private static bool TryExtractValue(Match m, out decimal value)
    {
        value = 0;
        if (m.Groups["num"].Success)
        {
            var num = decimal.Parse(m.Groups["num"].Value, CultureInfo.InvariantCulture);
            var den = decimal.Parse(m.Groups["den"].Value, CultureInfo.InvariantCulture);
            if (den == 0) return false;
            value = num / den;
            if (m.Groups["whole"].Success)
            {
                value += decimal.Parse(m.Groups["whole"].Value, CultureInfo.InvariantCulture);
            }
            return true;
        }
        if (m.Groups["dec"].Success)
        {
            value = decimal.Parse(m.Groups["dec"].Value, CultureInfo.InvariantCulture);
            return true;
        }
        if (m.Groups["int"].Success)
        {
            value = decimal.Parse(m.Groups["int"].Value, CultureInfo.InvariantCulture);
            return true;
        }
        return false;
    }

    private static string SpellInt(int n)
    {
        if (n < 0) return n.ToString(CultureInfo.InvariantCulture);
        if (n < 20) return Cardinal0To19[n];
        if (n < 100)
        {
            var t = n / 10;
            var o = n % 10;
            return o == 0 ? Tens[t] : $"{Tens[t]} {Cardinal0To19[o]}";
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private static bool StartsWithVowel(string s) =>
        s.Length > 0 && "aeiou".IndexOf(char.ToLowerInvariant(s[0])) >= 0;
}
