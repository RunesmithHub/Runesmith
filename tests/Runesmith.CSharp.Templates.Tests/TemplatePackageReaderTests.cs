using System.Globalization;
using System.IO.Compression;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;

namespace Runesmith.CSharp.Templates.Tests;

public sealed class TemplatePackageReaderTests : IDisposable
{
    private const string TemplateJson = """
        {
          // Comments and trailing commas appear in real templates.
          "identity": "Acme.Widget.CSharp",
          "groupIdentity": "Acme.Widget",
          "name": "Widget App",
          "shortName": ["widget", "widgets"],
          "author": "Acme",
          "classifications": ["Common", "Console"],
          "precedence": "500",
          "tags": { "language": "C#", "type": "project" },
          "symbols": {
            "Framework": {
              "type": "parameter",
              "datatype": "choice",
              "choices": [
                { "choice": "net10.0", "displayName": ".NET 10.0", "description": "Target net10.0" },
                { "choice": "net9.0", "displayName": ".NET 9.0" },
              ],
              "defaultValue": "net10.0"
            },
            "UseShiny": { "type": "parameter", "dataType": "bool", "defaultValue": false, "displayName": "Use _shiny parts" },
            "Port": { "type": "parameter", "datatype": "integer", "defaultValue": "5000" },
            "Secret": { "type": "parameter", "datatype": "string" },
            "skipRestore": { "type": "parameter", "datatype": "bool", "defaultValue": "false" },
            "HostIdentifier": { "type": "bind", "binding": "host:HostIdentifier" },
            "Computed": { "type": "computed", "value": "true" },
          },
        }
        """;

    private const string CliHost = """
        { "symbolInfo": { "UseShiny": { "longName": "shiny" }, "Secret": { "isHidden": "true" }, "skipRestore": { "longName": "no-restore" } } }
        """;

    private const string IdeHost = """
        { "symbolInfo": [ { "id": "UseShiny", "isVisible": true } ] }
        """;

    private const string Strings = """
        { "name": "Widget Application", "symbols/Framework/choices/net9.0/displayName": ".NET 9 (localized)" }
        """;

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-templates-").FullName;

    [Fact]
    public void ReadsTheTemplatesOfAPackage()
    {
        var package = WritePackage("Acme.Templates.1.2.3.nupkg", new()
        {
            ["Acme.Templates.nuspec"] = """<package><metadata><id>Acme.Templates</id><version>1.2.3</version></metadata></package>""",
            ["content/Widget/.template.config/template.json"] = TemplateJson,
            ["content/Widget/.template.config/dotnetcli.host.json"] = CliHost,
            ["content/Widget/.template.config/ide.host.json"] = IdeHost,
            ["content/Widget/.template.config/localize/templatestrings.en.json"] = Strings,
            ["content/Broken/.template.config/template.json"] = "{ not json",
        });

        var template = Assert.Single(TemplatePackageReader.Read(package, null, null, CultureInfo.GetCultureInfo("en-US")));

        Assert.Equal("Acme.Widget.CSharp", template.Identity);
        Assert.Equal("Widget Application", template.Name);
        Assert.Equal("widget", template.ShortName);
        Assert.Equal("C#", template.Language);
        Assert.Equal("project", template.Type);
        Assert.Equal("Acme.Templates 1.2.3", template.Source);
        Assert.Equal(500, template.Precedence);
        Assert.Equal("no-restore", template.SkipRestoreCliName);
        Assert.Equal(["Framework", "UseShiny", "Port"], template.Parameters.Select(p => p.Name));

        var framework = template.Parameters[0];
        Assert.Equal("Target framework", framework.Label);
        Assert.Equal(OptionKind.Choice, framework.Kind);
        Assert.Equal("net10.0", framework.Default);
        Assert.Equal(["net10.0", "net9.0"], framework.Choices.Select(c => c.Value));
        Assert.Equal(".NET 9 (localized)", framework.Choices[1].Label);
        Assert.Equal("Target net10.0", framework.Choices[0].Description);
        Assert.True(framework.IsMain);

        var shiny = template.Parameters[1];
        Assert.Equal(OptionKind.Toggle, shiny.Kind);
        Assert.Equal("shiny", shiny.CliName);
        Assert.Equal("Use shiny parts", shiny.Label);
        Assert.Equal("false", shiny.Default);
        Assert.True(shiny.IsMain);

        var port = template.Parameters[2];
        Assert.Equal(OptionKind.Number, port.Kind);
        Assert.Equal("Port", port.CliName);
        Assert.False(port.IsMain);
    }

    [Fact]
    public void FallsBackToEnglishStrings()
    {
        var files = new Dictionary<string, string>
        {
            ["template.json"] = TemplateJson,
            ["localize/templatestrings.en.json"] = Strings,
        };

        var template = TemplatePackageReader.ReadTemplate(files.GetValueOrDefault, CultureInfo.GetCultureInfo("nl-NL"));

        Assert.NotNull(template);
        Assert.Equal("Widget Application", template.Name);
    }

    [Fact]
    public void MergesTheLanguagesOfOneKindOfTemplate()
    {
        var parameter = new TemplateParameter("Framework", "framework", OptionKind.Choice, "Target framework") { IsMain = true, Default = "net10.0" };
        var csharpOnly = new TemplateParameter("UseProgramMain", "use-program-main", OptionKind.Toggle, "Use Program.Main") { IsMain = true };
        TemplateDefinition Variant(string language, params TemplateParameter[] parameters) =>
            new($"Acme.Console.{language}", "Console App", "console", language)
            {
                GroupIdentity = "Acme.Console",
                Type = "project",
                Classifications = ["Common", "Console"],
                Parameters = parameters,
            };

        var templates = TemplateCatalog.Build([Variant("VB", parameter), Variant("F#", parameter), Variant("C#", parameter, csharpOnly)]);

        var console = Assert.Single(templates, t => t.Id == "dotnet:Acme.Console");
        Assert.Equal("C#", console.Language);
        Assert.Equal(TemplateCategories.Console, console.Category);
        Assert.Equal("terminal", console.Icon);
        Assert.Contains("F#", console.Tags);
        Assert.Equal(
            [TemplateCatalog.LanguageOption, "Framework", TemplateCatalog.SdkOption, TemplateCatalog.SolutionNameOption, TemplateCatalog.SameDirectoryOption, "UseProgramMain"],
            console.Options.Options.Select(o => o.Id));
        var language = console.Options.Options[0];
        Assert.Equal(["C#", "F#", "VB"], language.Choices.Select(c => c.Value));
        Assert.Equal("C#", language.Default);
        Assert.Null(console.Options.Options[1].VisibleWhen);
        Assert.Equal(["C#"], console.Options.Options[^1].VisibleWhen?.Values);
        Assert.Contains(templates, t => t.Id == TemplateCatalog.BlankSolutionId);
    }

    [Theory]
    [InlineData("Test,MSTest,Desktop,Web", TemplateCategories.Test)]
    [InlineData("Common,Worker,Web", TemplateCategories.Web)]
    [InlineData("Common,WPF", TemplateCategories.Desktop)]
    [InlineData("Common,Library", TemplateCategories.Library)]
    [InlineData("Common,Console", TemplateCategories.Console)]
    [InlineData("Common,AI,MCP", TemplateCategories.Other)]
    public void CategorizesByClassification(string classifications, string category) =>
        Assert.Equal(category, TemplateCatalog.Categorize(classifications.Split(',')));

    [Fact]
    public void OffersProjectAndSolutionTemplatesButNotItemTemplates()
    {
        TemplateDefinition Of(string type) => new("Acme." + type, type, type.ToLowerInvariant(), "C#") { Type = type };

        var templates = TemplateCatalog.Build([Of("item"), Of("solution"), Of("project")]);

        Assert.Equal(["Blank Solution", "project", "solution"], templates.Select(t => t.Name));
        var solution = templates.Single(t => t.Name == "solution");
        Assert.True(((DotnetTemplateData)solution.Data!).IsSolution);
        Assert.DoesNotContain(solution.Options.Options, o => o.Id is TemplateCatalog.SolutionNameOption or TemplateCatalog.SameDirectoryOption);
    }

    [Fact]
    public void TheSdksOwnSolutionTemplateIsLeftToBlankSolution()
    {
        var sln = new TemplateDefinition("Microsoft.Standard.QuickStarts.Solution", "Solution File", "sln", "") { Type = "solution" };

        Assert.Equal(["Blank Solution"], TemplateCatalog.Build([sln]).Select(t => t.Name));
    }

    [Fact]
    public void ASolutionTemplateRunsWithItsOwnType()
    {
        var provider = new DotnetTemplateProvider(_ => Task.FromResult<IReadOnlyList<string>>([]), folder);
        var variant = new TemplateDefinition("Acme.Api", "Api solution", "acme-api", "C#") { Type = "solution" };

        var arguments = provider.NewProjectArguments(variant, "Demo", "/tmp/Demo", new OptionValues());

        Assert.Equal(["new", "acme-api", "--language", "C#", "--type", "solution", "--name", "Demo", "--output", "/tmp/Demo", "--no-update-check"], arguments);
    }

    [Fact]
    public void PassesOnlyChangedParameters()
    {
        var provider = new DotnetTemplateProvider(_ => Task.FromResult<IReadOnlyList<string>>([]), folder) { SkipRestore = true };
        var variant = new TemplateDefinition("Acme.Console.CSharp", "Console App", "console", "C#")
        {
            SkipRestoreCliName = "no-restore",
            Parameters =
            [
                new TemplateParameter("Framework", "framework", OptionKind.Choice, "Target framework") { Default = "net10.0" },
                new TemplateParameter("UseProgramMain", "use-program-main", OptionKind.Toggle, "Use Program.Main") { Default = "false" },
                new TemplateParameter("langVersion", "langVersion", OptionKind.Text, "Language version") { Default = "" },
            ],
        };
        var values = new OptionValues(new Dictionary<string, string> { ["Framework"] = "net10.0", ["UseProgramMain"] = "True", ["langVersion"] = "" });

        var arguments = provider.NewProjectArguments(variant, "Demo", "/tmp/Demo", values);

        Assert.Equal(
            ["new", "console", "--language", "C#", "--type", "project", "--name", "Demo", "--output", "/tmp/Demo", "--no-update-check",
                "--use-program-main", "True", "--no-restore", "true"],
            arguments);
    }

    [Fact]
    public void ReadsInstalledPackagesFromTheTemplateEngineList()
    {
        var listed = WritePackage("packages/Listed.1.0.0.nupkg", []);
        WritePackage("packages/Uninstalled.1.0.0.nupkg", []);
        File.WriteAllText(Path.Combine(folder, "packages.json"), $$"""{"Packages":[{"MountPointUri":"{{listed.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}]}""");

        var packages = TemplatePackages.FindInstalledPackages(folder).ToList();

        Assert.Equal([listed], packages.Select(p => p.Path));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string WritePackage(string name, Dictionary<string, string> files)
    {
        var path = Path.GetFullPath(Path.Combine(folder, name));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(text);
        }

        return path;
    }
}
