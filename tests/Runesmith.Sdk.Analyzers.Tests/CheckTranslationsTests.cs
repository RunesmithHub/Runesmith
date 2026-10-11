using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Runesmith.Sdk.Tasks;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class CheckTranslationsTests : IDisposable
{
    private const string English = """
        hello = Hello
        files = { $count ->
            [one] One file
           *[other] { $count } files
        }
        save = Save
            .tooltip = Save { $name }
        bye = Bye
        """;

    private readonly string root = Directory.CreateTempSubdirectory("runesmith-check-translations-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AGoodTranslationPasses()
    {
        Write("locales/en/acme.todo.ftl", English);
        Write("locales/nl/acme.todo.ftl", "hello = Hallo\nfiles = { $count ->\n    [one] Eén bestand\n   *[other] { $count } bestanden\n}\nsave = Bewaar\n    .tooltip = Bewaar { $name }\nbye = Dag");

        var (succeeded, engine) = Run();

        Assert.True(succeeded);
        Assert.Empty(engine.Errors);
        Assert.Empty(engine.Warnings);
    }

    [Fact]
    public void GapsInATranslationAreWarnings()
    {
        Write("locales/en/acme.todo.ftl", English);
        var dutch = Write("locales/nl/acme.todo.ftl", "hello = Hallo { $who }\nsave = Bewaar\nfiles = Bestanden\nold = Oud");

        var (succeeded, engine) = Run();

        Assert.True(succeeded);
        Assert.Empty(engine.Errors);
        var warnings = engine.Warnings.Select(w => (w.Code, w.File, w.LineNumber)).ToList();
        Assert.Contains(("RSL1005", dutch, 1), warnings);
        Assert.Contains(("RSL1005", dutch, 3), warnings);
        Assert.Contains(("RSL1004", dutch, 4), warnings);
        var missing = Assert.Single(engine.Warnings, w => w.Code == "RSL1003");
        Assert.Equal(dutch, missing.File);
        Assert.Contains("3 of 4 messages", missing.Message!, StringComparison.Ordinal);
        Assert.Contains("save.tooltip, bye", missing.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void ASyntaxErrorIsAnErrorInEveryLanguage()
    {
        var english = Write("locales/en/acme.todo.ftl", "hello = Hello\nbroken = { $x");
        var dutch = Write("locales/nl/acme.todo.ftl", "hello = Hallo }");

        var (succeeded, engine) = Run();

        Assert.False(succeeded);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1001" && e.File == english && e.LineNumber == 2);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1001" && e.File == dutch && e.LineNumber == 1 && e.ColumnNumber == 15);
    }

    [Fact]
    public void ProblemsInTheEnglishSourceAreErrors()
    {
        var english = Write("locales/en/acme.todo.ftl", "hello = Hello { missing }\nhello = Again");

        var (succeeded, engine) = Run();

        Assert.False(succeeded);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1002" && e.File == english && e.LineNumber == 2);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1006" && e.File == english && e.LineNumber == 1);
    }

    [Fact]
    public void TranslationsNeedAnEnglishSource()
    {
        Write("locales/nl/acme.todo.ftl", "hello = Hallo");

        var (succeeded, engine) = Run();

        Assert.False(succeeded);
        Assert.Equal("RSL1007", Assert.Single(engine.Errors).Code);
    }

    [Fact]
    public void LanguageFoldersAreLanguageTags()
    {
        Write("locales/en/acme.todo.ftl", "hello = Hello");
        Write("locales/dutch_language/acme.todo.ftl", "hello = Hallo");
        Write("locales/PT-br/acme.todo.ftl", "hello = Olá");
        Write("locales/stray.ftl", "hello = Stray");

        var (succeeded, engine) = Run();

        Assert.False(succeeded);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1008" && e.Message!.Contains("dutch_language", StringComparison.Ordinal));
        Assert.Contains(engine.Errors, e => e.Code == "RSL1009" && e.File.EndsWith("stray.ftl", StringComparison.Ordinal));
        Assert.Contains(engine.Warnings, w => w.Code == "RSL1008" && w.Message!.Contains("pt-BR", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePluginsManifestListsItsLanguages()
    {
        Write("plugin.json", """{ "id": "acme.todo", "languages": ["en", "fr"], "projects": { "contracts": "Acme.Todo.Contracts/Acme.Todo.Contracts.csproj", "implementation": "Acme.Todo/Acme.Todo.csproj" } }""");
        Write("Acme.Todo/locales/en/acme.todo.ftl", "hello = Hello");
        Write("Acme.Todo/locales/de/acme.todo.ftl", "hello = Hallo");

        var (succeeded, engine) = Run("Acme.Todo/locales", "plugin.json", "Acme.Todo/Acme.Todo.csproj");
        var (contractsSucceeded, contracts) = Run("Acme.Todo.Contracts/locales", "plugin.json", "Acme.Todo.Contracts/Acme.Todo.Contracts.csproj");

        Assert.False(succeeded);
        Assert.Contains(engine.Errors, e => e.Code == "RSL1010" && e.Message!.Contains("fr", StringComparison.Ordinal));
        Assert.Contains(engine.Warnings, w => w.Code == "RSL1010" && w.Message!.Contains("\"de\"", StringComparison.Ordinal));
        Assert.True(contractsSucceeded);
        Assert.Empty(contracts.Errors);
    }

    [Fact]
    public void APluginWithoutLanguagesIsAskedToListThem()
    {
        Write("plugin.json", """{ "id": "acme.todo" }""");
        Write("locales/en/acme.todo.ftl", "hello = Hello");
        Write("locales/nl/acme.todo.ftl", "hello = Hallo");

        var (succeeded, engine) = Run(manifest: "plugin.json");

        Assert.True(succeeded);
        Assert.Contains("\"languages\": [\"en\", \"nl\"]", Assert.Single(engine.Warnings).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALanguagePackIsCheckedAgainstTheEnglishOfWhatItTranslates()
    {
        Write("plugin.json", """{ "id": "acme.dutch", "languagePack": { "language": "nl", "name": "Nederlands", "englishName": "Dutch" }, "capabilities": [] }""");
        var runesmith = Write("english/runesmith/shell.ftl", "settings = Settings\nplugins = Plugins");
        var pack = Write("locales/nl/runesmith/shell.ftl", "settings = Instellingen\nextra = Extra");
        Write("locales/nl/acme.todo/todo.ftl", "hello = Hallo");

        var (succeeded, engine) = Run(manifest: "plugin.json", sources: [(runesmith, "runesmith")]);

        Assert.True(succeeded);
        Assert.Contains(engine.Warnings, w => w.Code == "RSL1003" && w.File == pack && w.Message!.Contains("of runesmith has 1 of 2", StringComparison.Ordinal));
        Assert.Contains(engine.Warnings, w => w.Code == "RSL1004" && w.File == pack && w.LineNumber == 2);
        Assert.Contains(engine.Messages, m => m.Code == "RSL1012" && m.Message!.Contains("acme.todo", StringComparison.Ordinal));
    }

    [Fact]
    public void ALanguagePackCarriesOneLanguageInTargetFoldersAndDeclaresNothingElse()
    {
        Write("plugin.json", """{ "id": "acme.dutch", "languagePack": { "language": "nl", "name": "Nederlands" }, "capabilities": [{ "id": "network", "reason": "x" }], "languages": ["nl"], "dependencies": [{ "id": "acme.todo", "range": "^1.0.0" }] }""");
        Write("locales/de/runesmith/shell.ftl", "settings = Einstellungen");
        Write("locales/nl/shell.ftl", "settings = Instellingen");
        Write("locales/nl/Not_A_Plugin/shell.ftl", "settings = Instellingen");

        var (succeeded, engine) = Run(manifest: "plugin.json");

        Assert.False(succeeded);
        var errors = engine.Errors.Select(e => e.Code).ToList();
        Assert.Equal(6, errors.Count(code => code is "RSL1011" or "RSL1009"));
        Assert.Contains(engine.Errors, e => e.Message!.Contains("declares no capabilities", StringComparison.Ordinal));
        Assert.Contains(engine.Errors, e => e.Message!.Contains("declares no languages", StringComparison.Ordinal));
        Assert.Contains(engine.Errors, e => e.Message!.Contains("declares no dependencies", StringComparison.Ordinal));
        Assert.Contains(engine.Errors, e => e.Message!.Contains("carries nl only", StringComparison.Ordinal));
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private (bool Succeeded, BuildEngine Engine) Run(string locales = "locales", string? manifest = null, string? project = null, IEnumerable<(string Path, string Target)>? sources = null)
    {
        var directory = Path.Combine(root, locales);
        var engine = new BuildEngine();
        var task = new CheckTranslations
        {
            BuildEngine = engine,
            LocalesDirectory = directory,
            Files = Directory.Exists(directory)
                ? [.. Directory.EnumerateFiles(directory, "*.ftl", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(f => new TaskItem(f))]
                : [],
            EnglishSources = [.. (sources ?? []).Select(s => new TaskItem(s.Path, new Dictionary<string, string> { ["TranslationTarget"] = s.Target }))],
            Manifest = manifest is null ? null : Path.Combine(root, manifest),
            Project = project is null ? null : Path.Combine(root, project),
        };
        return (task.Execute(), engine);
    }

    internal sealed class BuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public List<BuildWarningEventArgs> Warnings { get; } = [];

        public List<BuildMessageEventArgs> Messages { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "";

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
    }
}
