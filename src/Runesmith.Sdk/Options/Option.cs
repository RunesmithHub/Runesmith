namespace Runesmith.Sdk.Options;

/// <summary>The kind of value an option holds, which decides the control that edits it.</summary>
public enum OptionKind
{
    /// <summary>A line of text.</summary>
    Text,

    /// <summary>A file or folder path, with a browse button.</summary>
    Path,

    /// <summary>One of a fixed set of values; two to four short choices show as a segmented control, more as a drop-down.</summary>
    Choice,

    /// <summary>On or off.</summary>
    Toggle,

    /// <summary>A number, optionally within a range.</summary>
    Number,

    /// <summary>A list of values, or of <c>key=value</c> pairs when <see cref="Option.IsKeyValueList"/> is set.</summary>
    List,

    /// <summary>An installed SDK of the kind <see cref="Option.SdkKind"/> names, with a way to download one.</summary>
    Sdk,
}

/// <summary>Where an option shows in a form: with the main options, or in the collapsed Advanced section.</summary>
public enum OptionGroup
{
    Main,
    Advanced,
}

/// <summary>What a path option picks.</summary>
public enum PathKind
{
    File,
    Folder,
}

/// <summary>A value a choice option can take.</summary>
/// <param name="Value">The value saved and passed on.</param>
/// <param name="Label">The text the user sees.</param>
public sealed record OptionChoice(string Value, string Label)
{
    /// <summary>Gets a longer explanation, shown as the choice's tooltip.</summary>
    public string? Description { get; init; }
}

/// <summary>Shows or enables an option only while another option has one of some values.</summary>
/// <param name="OptionId">The other option.</param>
/// <param name="Values">The values that satisfy the condition.</param>
public sealed record OptionCondition(string OptionId, IReadOnlyList<string> Values)
{
    /// <summary>Whether the condition holds for a set of values.</summary>
    public bool IsMet(OptionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Values.Contains(values.Get(OptionId) ?? "", StringComparer.Ordinal);
    }
}

/// <summary>One setting in a form that a plugin describes: a project template's parameter, a run configuration's setting, a download filter.</summary>
/// <param name="Id">The key its value is saved under.</param>
/// <param name="Label">The label in front of the field.</param>
/// <param name="Kind">The kind of value, which decides the control.</param>
public sealed record Option(string Id, string Label, OptionKind Kind)
{
    /// <summary>Gets a hint shown under the field.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the value a new form starts with, in the text form <see cref="OptionValues"/> keeps.</summary>
    public string? Default { get; init; }

    /// <summary>Gets the section the option shows in.</summary>
    public OptionGroup Group { get; init; }

    /// <summary>Gets whether the form cannot be submitted while the option is empty.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets a regular expression a text value must match, or null.</summary>
    public string? Pattern { get; init; }

    /// <summary>Gets the message shown when the value does not match <see cref="Pattern"/>.</summary>
    public string? PatternMessage { get; init; }

    /// <summary>Gets the greyed text an empty field shows.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Gets the values of a choice option.</summary>
    public IReadOnlyList<OptionChoice> Choices { get; init; } = [];

    /// <summary>Gets what a path option picks.</summary>
    public PathKind PathKind { get; init; } = PathKind.Folder;

    /// <summary>Gets the lowest value of a number option.</summary>
    public double? Minimum { get; init; }

    /// <summary>Gets the highest value of a number option.</summary>
    public double? Maximum { get; init; }

    /// <summary>Gets whether a list option holds <c>key=value</c> pairs, such as environment variables.</summary>
    public bool IsKeyValueList { get; init; }

    /// <summary>Gets the kind of SDK an SDK option lists, such as <c>dotnet</c> or <c>jdk</c>.</summary>
    public string? SdkKind { get; init; }

    /// <summary>Gets the condition under which the option shows; null shows it always.</summary>
    public OptionCondition? VisibleWhen { get; init; }

    /// <summary>Gets the condition under which the option can be changed; null enables it always.</summary>
    public OptionCondition? EnabledWhen { get; init; }
}
