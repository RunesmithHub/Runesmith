using System.Globalization;
using System.Text.RegularExpressions;

namespace Runesmith.Sdk.Options;

/// <summary>The options of one form, in the order they show.</summary>
public sealed record OptionSet(IReadOnlyList<Option> Options)
{
    /// <summary>Gets a set with no options.</summary>
    public static OptionSet Empty { get; } = new([]);

    /// <summary>Gets the values a new form starts with.</summary>
    public OptionValues Defaults()
    {
        var values = new OptionValues();
        foreach (var option in Options)
        {
            if (option.Default is { } value)
                values.Set(option.Id, value);
        }

        return values;
    }

    /// <summary>Gets the problem with each visible option's value, by option id; an empty result means the values can be used.</summary>
    public IReadOnlyDictionary<string, string> Validate(OptionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in Options)
        {
            if (option.VisibleWhen is { } visible && !visible.IsMet(values))
                continue;

            var value = values.Get(option.Id);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (option.IsRequired)
                    problems[option.Id] = $"{option.Label} is required.";
                continue;
            }

            if (option.Pattern is { } pattern && !Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
                problems[option.Id] = option.PatternMessage ?? $"{option.Label} is not valid.";
            else if (option.Kind == OptionKind.Number && !IsInRange(option, value))
                problems[option.Id] = $"{option.Label} must be a number{RangeText(option)}.";
            else if (option.Kind == OptionKind.Choice && option.Choices.Count > 0 && !option.Choices.Any(choice => choice.Value == value))
                problems[option.Id] = $"{option.Label} must be one of the listed values.";
        }

        return problems;
    }

    private static bool IsInRange(Option option, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && (option.Minimum is not { } minimum || number >= minimum)
        && (option.Maximum is not { } maximum || number <= maximum);

    private static string RangeText(Option option) => (option.Minimum, option.Maximum) switch
    {
        ({ } minimum, { } maximum) => FormattableString.Invariant($" from {minimum} to {maximum}"),
        ({ } minimum, null) => FormattableString.Invariant($" of at least {minimum}"),
        (null, { } maximum) => FormattableString.Invariant($" of at most {maximum}"),
        _ => "",
    };
}
