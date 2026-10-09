using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Runesmith.Sdk.Options;

namespace Runesmith.CSharp.Templates;

/// <summary>Reads the templates in a template package, a <c>.nupkg</c> zip, without extracting it.</summary>
/// <remarks>A parameter is hidden when its symbol is not a <c>parameter</c>, when <c>dotnetcli.host.json</c> marks it hidden (such as the
/// port numbers), or when it skips restoring, which Runesmith decides.</remarks>
internal static class TemplatePackageReader
{
    private const string ConfigFolder = ".template.config/";
    private const string TemplateFile = ConfigFolder + "template.json";

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Reads every template in a package; a template whose files are damaged is left out.</summary>
    /// <param name="source">Where the package comes from, or null to take its id and version from its <c>.nuspec</c>.</param>
    public static IReadOnlyList<TemplateDefinition> Read(string packagePath, string? source, Version? sdkTemplatesVersion, CultureInfo culture)
    {
        using var zip = ZipFile.OpenRead(packagePath);
        source ??= ReadPackageName(zip) ?? Path.GetFileNameWithoutExtension(packagePath);
        var templates = new List<TemplateDefinition>();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith("/" + TemplateFile, StringComparison.Ordinal) && entry.FullName != TemplateFile)
                continue;

            var folder = entry.FullName[..^"template.json".Length];
            try
            {
                if (ReadTemplate(zip, folder, culture) is { } template)
                    templates.Add(template with { PackagePath = packagePath, Source = source, SdkTemplatesVersion = sdkTemplatesVersion });
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or InvalidOperationException
                or FormatException)
            {
            }
        }

        return templates;
    }

    /// <summary>Reads one template from the files of its <c>.template.config</c> folder.</summary>
    internal static TemplateDefinition? ReadTemplate(Func<string, string?> readFile, CultureInfo culture)
    {
        if (readFile("template.json") is not { } text)
            return null;

        using var document = JsonDocument.Parse(text, JsonOptions);
        var root = document.RootElement;
        var identity = String(root, "identity");
        var shortName = Property(root, "shortName") switch
        {
            { ValueKind: JsonValueKind.String } single => single.GetString(),
            { ValueKind: JsonValueKind.Array } many => many.EnumerateArray().Select(n => n.GetString()).FirstOrDefault(n => !string.IsNullOrEmpty(n)),
            _ => null,
        };
        if (identity is null || string.IsNullOrEmpty(shortName))
            return null;

        var strings = ReadLocalized(readFile, culture);
        string Localize(string key, string? fallback) => strings.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback ?? "";

        var tags = Property(root, "tags");
        using var cliHost = ReadJson(readFile("dotnetcli.host.json"));
        using var ideHost = ReadJson(readFile("ide.host.json"));
        var visibleInIde = ideHost is { } ide ? VisibleSymbols(ide.RootElement) : null;
        var cliSymbols = cliHost is { } cli ? Property(cli.RootElement, "symbolInfo") : null;

        var parameters = new List<TemplateParameter>();
        string? skipRestore = null;
        if (Property(root, "symbols") is { ValueKind: JsonValueKind.Object } symbols)
        {
            foreach (var symbol in symbols.EnumerateObject())
            {
                if (!string.Equals(String(symbol.Value, "type"), "parameter", StringComparison.OrdinalIgnoreCase))
                    continue;

                var host = cliSymbols is { ValueKind: JsonValueKind.Object } info ? Property(info, symbol.Name) : null;
                var cliName = host is { } h && String(h, "longName") is { Length: > 0 } longName ? longName : symbol.Name;
                if (cliName == "no-restore" || string.Equals(symbol.Name, "skipRestore", StringComparison.OrdinalIgnoreCase))
                {
                    skipRestore = cliName;
                    continue;
                }

                if (host is { } hidden && Bool(hidden, "isHidden"))
                    continue;

                parameters.Add(ReadParameter(symbol.Name, symbol.Value, cliName, visibleInIde, Localize));
            }
        }

        return new TemplateDefinition(identity, StripAccelerators(Localize("name", String(root, "name") ?? shortName)), shortName,
            tags is { ValueKind: JsonValueKind.Object } t ? String(t, "language") ?? "" : "")
        {
            GroupIdentity = String(root, "groupIdentity"),
            Type = tags is { ValueKind: JsonValueKind.Object } t2 ? String(t2, "type") ?? "" : "",
            Author = Localize("author", String(root, "author")) is { Length: > 0 } author ? author : null,
            Description = Localize("description", String(root, "description")) is { Length: > 0 } description ? description : null,
            Classifications = Property(root, "classifications") is { ValueKind: JsonValueKind.Array } classes
                ? [.. classes.EnumerateArray().Select(c => c.GetString()).OfType<string>()]
                : [],
            Parameters = parameters,
            SkipRestoreCliName = skipRestore,
            Precedence = int.TryParse(String(root, "precedence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var precedence) ? precedence : 0,
        };
    }

    private static TemplateDefinition? ReadTemplate(ZipArchive zip, string folder, CultureInfo culture) =>
        ReadTemplate(name => zip.GetEntry(folder + name) is { } entry ? ReadText(entry) : null, culture);

    private static TemplateParameter ReadParameter(string name, JsonElement symbol, string cliName, HashSet<string>? visibleInIde,
        Func<string, string?, string> localize)
    {
        var dataType = String(symbol, "datatype")?.ToLowerInvariant();
        var choices = dataType == "choice" && Property(symbol, "choices") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray()
                .Select(c => String(c, "choice") is { } value
                    ? new OptionChoice(value, StripAccelerators(localize($"symbols/{name}/choices/{value}/displayName", String(c, "displayName") ?? value)))
                    {
                        Description = localize($"symbols/{name}/choices/{value}/description", String(c, "description")) is { Length: > 0 } d ? d : null,
                    }
                    : null)
                .OfType<OptionChoice>()
                .ToList()
            : [];
        var kind = dataType switch
        {
            "choice" when choices.Count > 0 => OptionKind.Choice,
            "bool" or "boolean" => OptionKind.Toggle,
            "int" or "integer" or "float" or "double" or "number" => OptionKind.Number,
            _ => OptionKind.Text,
        };
        var label = name == "Framework"
            ? "Target framework"
            : StripAccelerators(localize($"symbols/{name}/displayName", String(symbol, "displayName") ?? name));
        var isMain = visibleInIde?.Contains(name) ?? kind is OptionKind.Choice or OptionKind.Toggle;
        return new TemplateParameter(name, cliName, kind, label)
        {
            Description = localize($"symbols/{name}/description", String(symbol, "description")) is { Length: > 0 } description ? description : null,
            Default = String(symbol, "defaultValue") ?? (kind == OptionKind.Toggle ? "false" : null),
            IsRequired = Bool(symbol, "isRequired"),
            Choices = choices,
            IsMain = isMain || name == "Framework",
        };
    }

    private static HashSet<string> VisibleSymbols(JsonElement ideHost)
    {
        var visible = new HashSet<string>(StringComparer.Ordinal);
        if (Property(ideHost, "symbolInfo") is { ValueKind: JsonValueKind.Array } symbols)
        {
            foreach (var symbol in symbols.EnumerateArray())
            {
                if (String(symbol, "id") is { } id && Bool(symbol, "isVisible"))
                    visible.Add(id);
            }
        }

        return visible;
    }

    private static Dictionary<string, string> ReadLocalized(Func<string, string?> readFile, CultureInfo culture)
    {
        for (var current = culture; ; current = current.Parent)
        {
            var name = current.Name.Length == 0 ? "en" : current.Name;
            if (readFile($"localize/templatestrings.{name}.json") is { } text)
            {
                using var document = JsonDocument.Parse(text, JsonOptions);
                return document.RootElement.EnumerateObject()
                    .Where(p => p.Value.ValueKind == JsonValueKind.String)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase);
            }

            if (current.Name.Length == 0)
                return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? ReadPackageName(ZipArchive zip)
    {
        if (zip.Entries.FirstOrDefault(e => !e.FullName.Contains('/', StringComparison.Ordinal) && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            is not { } entry)
            return null;

        try
        {
            var metadata = XDocument.Parse(ReadText(entry)).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
            var id = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
            var version = metadata?.Elements().FirstOrDefault(e => e.Name.LocalName == "version")?.Value;
            return id is null ? null : $"{id} {version}".TrimEnd();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static JsonDocument? ReadJson(string? text)
    {
        if (text is null)
            return null;

        try
        {
            return JsonDocument.Parse(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Removes the underscores that mark a label's access key, such as in "Do not use _top-level statements".</summary>
    internal static string StripAccelerators(string text) => text.Replace("__", "\0", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal)
        .Replace("\0", "_", StringComparison.Ordinal);

    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        return null;
    }

    private static string? String(JsonElement element, string name) => Property(element, name) switch
    {
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static bool Bool(JsonElement element, string name) => string.Equals(String(element, name), "true", StringComparison.OrdinalIgnoreCase);
}
