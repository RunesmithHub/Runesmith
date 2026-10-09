using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

/// <summary>Hover information. Every form servers send arrives as one <see cref="MarkupContent"/>; marked strings become markdown.</summary>
public sealed record Hover([property: JsonConverter(typeof(HoverContentsConverter))] MarkupContent Contents, Range? Range);

/// <summary>The label of a parameter: its <see cref="Text"/>, or the <see cref="Start"/> and <see cref="End"/> offsets into the signature's label.</summary>
public sealed record ParameterLabel(string? Text, int Start, int End);

public sealed record ParameterInformation(
    [property: JsonConverter(typeof(ParameterLabelConverter))] ParameterLabel Label,
    [property: JsonConverter(typeof(MarkupContentConverter))] MarkupContent? Documentation);

public sealed record SignatureInformation(
    string Label,
    [property: JsonConverter(typeof(MarkupContentConverter))] MarkupContent? Documentation,
    IReadOnlyList<ParameterInformation>? Parameters,
    int? ActiveParameter)
{
    /// <summary>Gets the text of a parameter's label, resolving offsets into <see cref="Label"/>.</summary>
    public string? GetParameterLabel(int index)
    {
        if (Parameters is null || index < 0 || index >= Parameters.Count)
            return null;
        var label = Parameters[index].Label;
        if (label.Text is not null)
            return label.Text;
        return label.Start >= 0 && label.End <= Label.Length && label.Start <= label.End ? Label[label.Start..label.End] : null;
    }
}

public sealed record SignatureHelp(IReadOnlyList<SignatureInformation> Signatures, int? ActiveSignature, int? ActiveParameter);
