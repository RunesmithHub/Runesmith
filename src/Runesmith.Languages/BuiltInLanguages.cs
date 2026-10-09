using Runesmith.Sdk.Languages;
using TextMateSharp.Grammars;

namespace Runesmith.Languages;

/// <summary>The languages Runesmith knows without plugins: every language TextMateSharp ships a grammar for, plus plain text.</summary>
internal static class BuiltInLanguages
{
    // Languages whose grammar is only included by other grammars, or duplicates another language's grammar.
    private static readonly HashSet<string> Hidden = ["dockercompose", "typst-code"];

    private static readonly Dictionary<string, Extra> Extras = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = new("C#", "file-code"),
        ["fsharp"] = new("F#", "file-code"),
        ["vb"] = new("Visual Basic", "file-code"),
        ["xml"] = new("XML", "code", Extensions: [".slnx", ".resx", ".config", ".manifest", ".runsettings", ".ruleset", ".editorconfig.xml"]),
        ["json"] = new("JSON", "file-json"),
        ["jsonc"] = new("JSON with Comments", "file-json", FileNames: ["tsconfig.json", "jsconfig.json", "global.json", "settings.json", ".markdownlint.json"]),
        ["yaml"] = new("YAML", "settings"),
        ["ini"] = new("INI", "settings"),
        ["properties"] = new("Properties", "settings", FileNames: [".editorconfig", ".gitattributes", ".gitconfig", ".gitmodules"]),
        ["ignore"] = new("Ignore", "settings", FileNames: [".gitignore", ".dockerignore", ".npmignore"]),
        ["markdown"] = new("Markdown", "file-text"),
        ["shellscript"] = new("Shell Script", "terminal"),
        ["powershell"] = new("PowerShell", "terminal"),
        ["bat"] = new("Batch", "terminal"),
        ["dockerfile"] = new("Dockerfile", "package", FileNames: ["Dockerfile", "Containerfile"]),
        ["makefile"] = new("Makefile", "settings", FileNames: ["Makefile", "makefile", "GNUmakefile"]),
        ["log"] = new("Log", "file-text"),
        ["diff"] = new("Diff", "git-compare"),
        ["html"] = new("HTML", "code"),
        ["css"] = new("CSS", "braces"),
        ["scss"] = new("SCSS", "braces"),
        ["less"] = new("Less", "braces"),
        ["razor"] = new("Razor", "code"),
        ["hlsl"] = new("HLSL", "file-code"),
        ["shaderlab"] = new("ShaderLab", "file-code"),
        ["sql"] = new("SQL", "database"),
    };

    /// <summary>Creates the built-in definitions from the grammars TextMateSharp ships.</summary>
    public static IReadOnlyList<LanguageDefinition> Create(RegistryOptions options)
    {
        var languages = new List<LanguageDefinition> { new(ILanguageRegistry.PlainText, "Plain Text") { Extensions = [".txt", ".text"], Icon = "file-text" } };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ILanguageRegistry.PlainText };
        foreach (var language in options.GetAvailableLanguages())
        {
            if (Hidden.Contains(language.Id) || !seen.Add(language.Id) || options.GetScopeByLanguageId(language.Id) is not { Length: > 0 } scope)
                continue;

            Extras.TryGetValue(language.Id, out var extra);
            var configuration = language.Configuration;
            var block = configuration?.Comments?.BlockComment;
            languages.Add(new LanguageDefinition(language.Id, extra?.Name ?? language.Aliases?.FirstOrDefault() ?? language.Id)
            {
                Extensions = [.. (language.Extensions ?? []).Where(e => e.StartsWith('.')).Concat(extra?.Extensions ?? [])],
                FileNames = extra?.FileNames ?? [],
                ScopeName = scope,
                LineComment = configuration?.Comments?.LineComment is { Length: > 0 } line ? line : null,
                BlockComment = block is [{ Length: > 0 } open, { Length: > 0 } close] ? (open, close) : null,
                Brackets = Brackets(configuration),
                Icon = extra?.Icon ?? "file-code",
            });
        }

        return languages;
    }

    private static IReadOnlyList<(char Open, char Close)> Brackets(LanguageConfiguration? configuration)
    {
        if (configuration?.Brackets is not { Length: > 0 } brackets)
            return [('(', ')'), ('[', ']'), ('{', '}')];

        return [.. brackets.Where(pair => pair is [{ Length: 1 }, { Length: 1 }]).Select(pair => (pair[0][0], pair[1][0])).Distinct()];
    }

    private sealed record Extra(string Name, string Icon, IReadOnlyList<string>? Extensions = null, IReadOnlyList<string>? FileNames = null);
}
