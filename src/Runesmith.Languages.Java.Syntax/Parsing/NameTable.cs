namespace Runesmith.Languages.Java.Syntax;

/// <summary>Gives every distinct identifier of a file one string, so repeated names cost no allocations.</summary>
internal sealed class NameTable
{
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> bySpan;

    public NameTable() => bySpan = names.GetAlternateLookup<ReadOnlySpan<char>>();

    public string Get(ReadOnlySpan<char> text)
    {
        if (bySpan.TryGetValue(text, out var name))
            return name;

        name = text.ToString();
        names[name] = name;
        return name;
    }
}
