using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Runesmith.Sdk.Localization;

/// <summary>The values of a message's variables, by name without the <c>$</c>: <c>new LocalizationArgs { { "count", 3 }, { "name", file } }</c>.</summary>
/// <remarks>Added in plugin API 0.1.2. Numbers and dates keep their type, so each language formats them its own way, and a number picks the
/// plural form of the language, such as <c>[one]</c> or <c>[other]</c>. The values are strings, <see cref="long"/>, <see cref="double"/>,
/// <see cref="decimal"/> and <see cref="DateTimeOffset"/>.</remarks>
public sealed class LocalizationArgs : IEnumerable<KeyValuePair<string, object>>
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    /// <summary>Gets how many variables have a value.</summary>
    public int Count => values.Count;

    /// <summary>Sets a variable to text, which shows as it is.</summary>
    public LocalizationArgs Add(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Set(name, value);
    }

    /// <summary>Sets a variable to a whole number.</summary>
    public LocalizationArgs Add(string name, long value) => Set(name, value);

    /// <summary>Sets a variable to a number.</summary>
    public LocalizationArgs Add(string name, double value) => Set(name, value);

    /// <summary>Sets a variable to a decimal number.</summary>
    public LocalizationArgs Add(string name, decimal value) => Set(name, value);

    /// <summary>Sets a variable to a date and time; a <see cref="DateTime"/> converts to one.</summary>
    public LocalizationArgs Add(string name, DateTimeOffset value) => Set(name, value);

    /// <summary>Gets a variable's value: a string, <see cref="long"/>, <see cref="double"/>, <see cref="decimal"/> or
    /// <see cref="DateTimeOffset"/>.</summary>
    public bool TryGetValue(string name, [NotNullWhen(true)] out object? value) => values.TryGetValue(name, out value);

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private LocalizationArgs Set(string name, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        values[name.StartsWith('$') ? name[1..] : name] = value;
        return this;
    }
}
