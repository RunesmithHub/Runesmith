namespace Runesmith.Sdk.Settings;

/// <summary>Where a setting's value comes from; later scopes override earlier ones.</summary>
public enum SettingScope
{
    /// <summary>The value a setting has until someone changes it.</summary>
    Default,

    /// <summary>The user's own settings, for every folder they open.</summary>
    User,

    /// <summary>The open folder's settings, in <c>.runesmith/settings.json</c>, shared with everyone who opens it.</summary>
    Workspace,
}

/// <summary>Describes a setting, so the settings page can show it and the settings service can check its values.</summary>
/// <param name="Key">A unique key, conventionally <c>area.name</c> such as <c>editor.fontSize</c>; the settings files use it.</param>
/// <param name="Title">The name shown on the settings page.</param>
/// <param name="Category">The settings page section, such as "Editor".</param>
/// <param name="DefaultValue">The value until someone changes it; its type is the setting's type: <see cref="bool"/>, <see cref="int"/>,
/// <see cref="double"/>, <see cref="string"/> or an enum.</param>
public sealed record SettingDefinition(string Key, string Title, string Category, object DefaultValue)
{
    /// <summary>Gets the explanation shown under the setting.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the smallest allowed number, for number settings.</summary>
    public double? Minimum { get; init; }

    /// <summary>Gets the largest allowed number, for number settings.</summary>
    public double? Maximum { get; init; }

    /// <summary>Gets the allowed values, for text settings with a fixed set of choices.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Gets the type of the setting's values.</summary>
    public Type ValueType => DefaultValue.GetType();
}

/// <summary>Adds settings. Export it with <c>[Export(typeof(ISettingContributor))]</c>.</summary>
public interface ISettingContributor
{
    IEnumerable<SettingDefinition> Settings { get; }
}
