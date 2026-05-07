using System.Globalization;
using System.Text.RegularExpressions;

namespace Cocktail.Web.Services;

public abstract record VoiceIntent;

public sealed record WakeWordIntent : VoiceIntent;
public sealed record NextStepIntent : VoiceIntent;
public sealed record PrevStepIntent : VoiceIntent;
public sealed record RepeatIntent : VoiceIntent;
public sealed record PauseTtsIntent : VoiceIntent;
public sealed record StartOverIntent : VoiceIntent;
public sealed record JumpToStepIntent(int StepNumber) : VoiceIntent;
public sealed record ReadIngredientsIntent : VoiceIntent;
public sealed record StartTimerIntent(TimeSpan Duration) : VoiceIntent;
public sealed record CancelTimerIntent : VoiceIntent;
public sealed record TimerStatusIntent : VoiceIntent;
public sealed record ConvertIntent(decimal Amount, string FromUnit, string ToUnit) : VoiceIntent;
public sealed record AddIngredientIntent(string IngredientName) : VoiceIntent;
public sealed record RemoveIngredientIntent(string IngredientName) : VoiceIntent;
public sealed record UnknownIntent(string Transcript) : VoiceIntent;

/// <summary>
/// Maps free-form voice transcripts onto a small command grammar. Pure;
/// no I/O; safe to unit-test.
/// </summary>
public static class VoiceParser
{
    private static readonly Dictionary<string, int> Numbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4,
        ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9,
        ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13,
        ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17,
        ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70,
        ["eighty"] = 80, ["ninety"] = 90,
        ["a"] = 1, ["an"] = 1,
    };

    private const string UnitAlternation =
        @"ml|cl|oz|fluid\s+ounces?|ounces?|milliliters?|centiliters?|tsp|teaspoons?|tbsp|tablespoons?|dashes?|barspoons?|cups?";

    private static readonly Regex ConvertPattern = new(
        @"(?<amount>\d+(?:\.\d+)?|[a-z]+(?:[\s\-][a-z]+)?)\s+(?<from>" + UnitAlternation
        + @")\s+(?:in|to|into)\s+(?<to>" + UnitAlternation + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TimerUnitPattern = new(
        @"\b(?<unit>seconds?|secs?|minutes?|mins?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepJumpPattern = new(
        @"\bstep\s+(?<n>\d+|[a-z]+(?:[\s\-][a-z]+)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Ingredient name regexes are anchored to the start of the (trimmed, lowercased)
    // transcript so we don't accidentally match the word "add" or "remove" appearing
    // inside a longer recipe-walkthrough phrase.
    private static readonly Regex AddIngredientPattern = new(
        @"^(?:please\s+)?(?:add|stock)\s+(?<name>.+?)(?:\s+to\s+(?:the\s+|my\s+)?bar)?[\.\!\?\s]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AddIngredientHavePattern = new(
        @"^(?:i\s+have|i've\s+got|i\s+have\s+got)\s+(?<name>.+?)[\.\!\?\s]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemoveIngredientPattern = new(
        @"^(?:please\s+)?(?:remove|delete)\s+(?<name>.+?)(?:\s+from\s+(?:the\s+|my\s+)?bar)?[\.\!\?\s]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RemoveIngredientOutOfPattern = new(
        @"^(?:i'?m\s+out\s+of|out\s+of)\s+(?<name>.+?)[\.\!\?\s]*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static VoiceIntent Parse(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return new UnknownIntent(transcript ?? "");
        var t = transcript.ToLowerInvariant().Trim();

        if (Contains(t, "hey bartender", "okay bartender", "ok bartender", "hi bartender"))
            return new WakeWordIntent();

        if (Contains(t, "cancel timer", "stop timer", "kill timer", "abort timer"))
            return new CancelTimerIntent();
        if (Contains(t, "time left", "time remaining", "how long left", "how much time"))
            return new TimerStatusIntent();

        if (TryParseConvert(t, out var convertIntent)) return convertIntent!;
        if (TryParseTimer(t, out var dur)) return new StartTimerIntent(dur);

        if (TryParseRemoveIngredient(t, out var removeName))
            return new RemoveIngredientIntent(removeName);
        if (TryParseAddIngredient(t, out var addName))
            return new AddIngredientIntent(addName);

        if (Contains(t, "ingredients", "what's in this", "what do i need", "read recipe", "read the recipe"))
            return new ReadIngredientsIntent();

        if (TryParseStepJump(t, out var stepN))
            return new JumpToStepIntent(stepN);

        if (Contains(t, "start over", "restart", "from the top"))
            return new StartOverIntent();
        if (Contains(t, "go back", "previous"))
            return new PrevStepIntent();
        if (ContainsWord(t, "back"))
            return new PrevStepIntent();
        if (ContainsWord(t, "next", "continue", "forward"))
            return new NextStepIntent();
        if (ContainsWord(t, "repeat", "again"))
            return new RepeatIntent();
        if (Contains(t, "say again", "what was that"))
            return new RepeatIntent();
        if (ContainsWord(t, "stop", "pause", "quiet", "silence"))
            return new PauseTtsIntent();
        if (Contains(t, "shut up"))
            return new PauseTtsIntent();

        return new UnknownIntent(transcript);
    }

    private static bool TryParseAddIngredient(string text, out string name)
    {
        name = "";
        var m = AddIngredientPattern.Match(text);
        if (!m.Success) m = AddIngredientHavePattern.Match(text);
        if (!m.Success) return false;
        var captured = CleanIngredientName(m.Groups["name"].Value);
        if (captured.Length == 0) return false;
        name = captured;
        return true;
    }

    private static bool TryParseRemoveIngredient(string text, out string name)
    {
        name = "";
        var m = RemoveIngredientPattern.Match(text);
        if (!m.Success) m = RemoveIngredientOutOfPattern.Match(text);
        if (!m.Success) return false;
        var captured = CleanIngredientName(m.Groups["name"].Value);
        if (captured.Length == 0) return false;
        name = captured;
        return true;
    }

    private static string CleanIngredientName(string raw)
    {
        var s = raw.Trim().Trim('.', '!', '?', ',', ';');
        if (s.EndsWith(" please", StringComparison.OrdinalIgnoreCase))
            s = s[..^" please".Length].TrimEnd();
        return s;
    }

    private static bool TryParseStepJump(string text, out int stepNumber)
    {
        stepNumber = 0;
        var match = StepJumpPattern.Match(text);
        if (!match.Success) return false;
        var n = match.Groups["n"].Value;
        if (TryNumber(n, out var v) && v >= 1 && v == decimal.Truncate(v) && v <= 100)
        {
            stepNumber = (int)v;
            return true;
        }
        return false;
    }

    private static bool TryParseTimer(string text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = TimerUnitPattern.Match(text);
        if (!match.Success) return false;

        var unit = match.Groups["unit"].Value.ToLowerInvariant();
        var isMinutes = unit.StartsWith('m');

        var prefix = text[..match.Index].TrimEnd();
        if (prefix.Length == 0) return false;

        var tokens = prefix.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
        for (var take = Math.Min(3, tokens.Length); take >= 1; take--)
        {
            var slice = string.Join(' ', tokens, tokens.Length - take, take);
            if (TryNumber(slice, out var n) && n > 0)
            {
                duration = isMinutes
                    ? TimeSpan.FromMinutes((double)n)
                    : TimeSpan.FromSeconds((double)n);
                return true;
            }
        }
        return false;
    }

    private static bool TryParseConvert(string text, out ConvertIntent? intent)
    {
        intent = null;
        var match = ConvertPattern.Match(text);
        if (!match.Success) return false;
        if (!TryNumber(match.Groups["amount"].Value, out var amount)) return false;
        intent = new ConvertIntent(
            amount,
            NormalizeUnit(match.Groups["from"].Value),
            NormalizeUnit(match.Groups["to"].Value));
        return true;
    }

    private static string NormalizeUnit(string s) =>
        Regex.Replace(s.ToLowerInvariant().Trim(), @"\s+", " ");

    private static bool TryNumber(string s, out decimal value)
    {
        value = 0;
        s = s.Trim().ToLowerInvariant();

        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return true;

        switch (s)
        {
            case "half":
            case "half a":
            case "half an":
            case "a half":
                value = 0.5m;
                return true;
            case "quarter":
            case "a quarter":
                value = 0.25m;
                return true;
            case "three quarters":
            case "three-quarters":
                value = 0.75m;
                return true;
        }

        var parts = s.Replace('-', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            if (Numbers.TryGetValue(parts[0], out var v))
            {
                value = v;
                return true;
            }
            return false;
        }
        if (parts.Length == 2 &&
            Numbers.TryGetValue(parts[0], out var tens) &&
            Numbers.TryGetValue(parts[1], out var ones))
        {
            if (tens >= 20 && tens <= 90 && tens % 10 == 0 && ones is > 0 and < 10)
            {
                value = tens + ones;
                return true;
            }
        }
        return false;
    }

    private static bool Contains(string text, params string[] phrases)
    {
        foreach (var p in phrases)
        {
            if (text.Contains(p, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool ContainsWord(string text, params string[] words)
    {
        foreach (var w in words)
        {
            var i = 0;
            while (i < text.Length)
            {
                var idx = text.IndexOf(w, i, StringComparison.Ordinal);
                if (idx < 0) break;
                var before = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
                var after = idx + w.Length >= text.Length || !char.IsLetterOrDigit(text[idx + w.Length]);
                if (before && after) return true;
                i = idx + 1;
            }
        }
        return false;
    }
}
