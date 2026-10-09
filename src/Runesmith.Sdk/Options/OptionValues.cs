using System.Globalization;

namespace Runesmith.Sdk.Options;

/// <summary>The values of a form's options, by option id, kept as text so they save and pass on as they are: toggles as <c>true</c> or
/// <c>false</c>, numbers in the invariant culture, and lists one item per line.</summary>
public sealed class OptionValues
{
    private readonly Dictionary<string, string> values;

    public OptionValues() => values = new(StringComparer.Ordinal);

    public OptionValues(IReadOnlyDictionary<string, string> values) => this.values = new(values, StringComparer.Ordinal);

    /// <summary>Gets every value, by option id.</summary>
    public IReadOnlyDictionary<string, string> All => values;

    /// <summary>Gets an option's value, or null when it has none.</summary>
    public string? Get(string id) => values.GetValueOrDefault(id);

    /// <summary>Sets an option's value; null removes it.</summary>
    public void Set(string id, string? value)
    {
        if (value is null)
            values.Remove(id);
        else
            values[id] = value;
    }

    /// <summary>Gets a toggle's value.</summary>
    public bool GetBool(string id) => bool.TryParse(Get(id), out var value) && value;

    /// <summary>Gets a number's value, or null when it has none or it is not a number.</summary>
    public double? GetNumber(string id) =>
        double.TryParse(Get(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Gets a list's items.</summary>
    public IReadOnlyList<string> GetList(string id) =>
        Get(id) is { Length: > 0 } text ? text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    /// <summary>Gets a key and value list's pairs; an item without <c>=</c> has an empty value.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetPairs(string id) =>
    [
        .. GetList(id).Select(item => item.IndexOf('=', StringComparison.Ordinal) is var at and >= 0
            ? new KeyValuePair<string, string>(item[..at], item[(at + 1)..])
            : new KeyValuePair<string, string>(item, "")),
    ];

    /// <summary>Gets a copy that changes independently.</summary>
    public OptionValues Copy() => new(values);
}
