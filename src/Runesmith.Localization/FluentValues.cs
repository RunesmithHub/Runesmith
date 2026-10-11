using System.Globalization;
using Linguini.Bundle.Types;
using Linguini.Shared.Types.Bundle;
using Runesmith.Sdk.Localization;

namespace Runesmith.Localization;

/// <summary>Turns arguments into Fluent values, and gives the <c>NUMBER</c> and <c>DATETIME</c> functions, all formatting in one culture.</summary>
internal sealed class FluentValues(CultureInfo culture)
{
    public CultureInfo Culture { get; } = culture;

    public Dictionary<string, IFluentType>? Convert(LocalizationArgs? args)
    {
        if (args is null || args.Count == 0)
            return null;

        var values = new Dictionary<string, IFluentType>(args.Count, StringComparer.Ordinal);
        foreach (var (name, value) in args)
        {
            values[name] = value switch
            {
                string text => (FluentString)text,
                long number => new CultureNumber(number, number, Culture, NumberOptions.Default),
                double number => new CultureNumber(number, number, Culture, NumberOptions.Default),
                decimal number => new CultureNumber((double)number, number, Culture, NumberOptions.Default),
                DateTimeOffset date => new CultureDate(date, Culture, DateOptions.Default),
                _ => (FluentString)(System.Convert.ToString(value, Culture) ?? ""),
            };
        }

        return values;
    }

    /// <summary>Formats a number: <c>NUMBER($size, minimumFractionDigits: 1, maximumFractionDigits: 2, useGrouping: "false",
    /// style: "percent")</c>.</summary>
    public IFluentType Number(IList<IFluentType> positional, IDictionary<string, IFluentType> named)
    {
        var (value, original) = positional.Count == 0 ? (double.NaN, null) : positional[0] switch
        {
            CultureNumber number => (number.Value, number.Original),
            FluentNumber number => (number.Value, (object?)number.Value),
            FluentString text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => (parsed, parsed),
            _ => (double.NaN, null),
        };
        if (original is null)
            return new FluentErrType("NUMBER() needs a number.");

        var options = NumberOptions.Default with
        {
            MinimumFractionDigits = Integer(named, "minimumFractionDigits"),
            MaximumFractionDigits = Integer(named, "maximumFractionDigits"),
            MinimumIntegerDigits = Integer(named, "minimumIntegerDigits"),
            UseGrouping = Text(named, "useGrouping") is not ("false" or "0"),
            Style = Text(named, "style") ?? "decimal",
            Currency = Text(named, "currency"),
        };
        return new CultureNumber(value, original, Culture, options);
    }

    /// <summary>Formats a date and time: <c>DATETIME($when, dateStyle: "long", timeStyle: "short")</c>.</summary>
    public IFluentType DateTime(IList<IFluentType> positional, IDictionary<string, IFluentType> named)
    {
        DateTimeOffset? value = positional.Count == 0 ? null : positional[0] switch
        {
            CultureDate given => given.Value,
            FluentString text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) => parsed,
            _ => null,
        };
        if (value is not { } date)
            return new FluentErrType("DATETIME() needs a date.");

        var dateStyle = Text(named, "dateStyle");
        var timeStyle = Text(named, "timeStyle");
        return new CultureDate(date, Culture, dateStyle is null && timeStyle is null ? DateOptions.Default : new DateOptions(dateStyle, timeStyle));
    }

    private static int? Integer(IDictionary<string, IFluentType> named, string name) =>
        named.TryGetValue(name, out var value) && value is FluentNumber number && number.Value is >= 0 and <= 20 ? (int)number.Value : null;

    private static string? Text(IDictionary<string, IFluentType> named, string name) =>
        named.TryGetValue(name, out var value) ? value.AsString() : null;
}

/// <summary>How <c>NUMBER</c> writes a number.</summary>
internal sealed record NumberOptions(int? MinimumFractionDigits, int? MaximumFractionDigits, int? MinimumIntegerDigits, bool UseGrouping, string Style, string? Currency)
{
    public static readonly NumberOptions Default = new(null, null, null, true, "decimal", null);
}

/// <summary>How <c>DATETIME</c> writes a date: <c>full</c>, <c>long</c>, <c>medium</c> or <c>short</c> for the date and the time; neither
/// writes the short date.</summary>
internal sealed record DateOptions(string? DateStyle, string? TimeStyle)
{
    public static readonly DateOptions Default = new("short", null);
}

/// <summary>A number that the culture formats, and that still picks plural variants and matches number keys such as <c>[0]</c> as a Fluent
/// number does.</summary>
internal sealed record CultureNumber : FluentNumber, IFluentType
{
    public CultureNumber(double value, object original, CultureInfo culture, NumberOptions options)
        : base((FluentNumber)value)
    {
        Original = original;
        Culture = culture;
        Options = options;
    }

    /// <summary>Gets the number as it was given, so a whole or decimal number keeps its digits.</summary>
    public object Original { get; }

    public CultureInfo Culture { get; }

    public NumberOptions Options { get; }

    // Variant keys are plain Fluent numbers, and [0] must match a 0 given as an argument.
    protected override Type EqualityContract => typeof(FluentNumber);

    string IFluentType.AsString()
    {
        var minimum = Options.MinimumFractionDigits ?? 0;
        var maximum = Math.Max(minimum, Options.MaximumFractionDigits ?? (Options.Style == "percent" ? 0 : Options.Style == "currency" ? 2 : 3));
        if (Options.Style == "percent")
        {
            var format = (NumberFormatInfo)Culture.NumberFormat.Clone();
            format.PercentDecimalDigits = maximum;
            if (!Options.UseGrouping)
                format.PercentGroupSizes = [0];
            return Value.ToString("P", format);
        }

        var text = Original switch
        {
            long whole => whole.ToString(Pattern(minimum, maximum), Culture),
            decimal exact => exact.ToString(Pattern(minimum, maximum), Culture),
            _ => Value.ToString(Pattern(minimum, maximum), Culture),
        };
        return Options.Style == "currency" && Options.Currency is { Length: > 0 } currency ? $"{text} {currency}" : text;
    }

    IFluentType IFluentType.Copy() => this;

    private string Pattern(int minimum, int maximum)
    {
        var integer = new string('0', Math.Max(1, Options.MinimumIntegerDigits ?? 1));
        var whole = Options.UseGrouping ? "#," + integer.PadLeft(Math.Max(integer.Length, 3), '#') : integer;
        return maximum == 0 ? whole : whole + "." + new string('0', minimum) + new string('#', maximum - minimum);
    }
}

/// <summary>A date and time that the culture formats, in the local time zone.</summary>
internal sealed record CultureDate(DateTimeOffset Value, CultureInfo Culture, DateOptions Options) : IFluentType
{
    public string AsString()
    {
        var local = Value.ToLocalTime().DateTime;
        var format = Culture.DateTimeFormat;
        var date = Options.DateStyle switch
        {
            "full" => local.ToString("D", Culture),
            "long" => local.ToString(WithoutWeekday(format.LongDatePattern), Culture),
            "medium" => local.ToString(WithoutWeekday(format.LongDatePattern).Replace("MMMM", "MMM", StringComparison.Ordinal), Culture),
            "short" => local.ToString("d", Culture),
            _ => null,
        };
        var time = Options.TimeStyle switch
        {
            "short" => local.ToString("t", Culture),
            "medium" or "long" or "full" => local.ToString("T", Culture),
            _ => null,
        };
        return (date, time) switch
        {
            ({ } d, { } t) => d + " " + t,
            ({ } d, null) => d,
            (null, { } t) => t,
            _ => local.ToString("d", Culture),
        };
    }

    public bool IsError() => false;

    public bool Matches(IFluentType other, IScope scope) => other is CultureDate date && date.Value == Value;

    public IFluentType Copy() => this;

    private static string WithoutWeekday(string pattern)
    {
        var start = pattern.IndexOf("dddd", StringComparison.Ordinal);
        if (start < 0)
            return pattern;

        var end = start + 4;
        while (end < pattern.Length && pattern[end] is ',' or ' ' or '.' or '،')
            end++;
        return (pattern[..start] + pattern[end..]).Trim(' ', ',');
    }
}

/// <summary>The functions every bundle has.</summary>
internal static class FluentFunctions
{
    public static IDictionary<string, ExternalFunction> For(FluentValues values) => new Dictionary<string, ExternalFunction>(StringComparer.Ordinal)
    {
        ["NUMBER"] = values.Number,
        ["DATETIME"] = values.DateTime,
    };
}
