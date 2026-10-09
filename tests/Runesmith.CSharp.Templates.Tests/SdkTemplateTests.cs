using System.Diagnostics;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;

namespace Runesmith.CSharp.Templates.Tests;

/// <summary>Reads the templates of the .NET SDK the tests run with and creates a project from one.</summary>
public sealed class SdkTemplateTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-sdk-templates-").FullName;

    [Fact]
    public async Task FindsTheTemplatesTheSdkShips()
    {
        var provider = Provider();
        var stopwatch = Stopwatch.StartNew();

        var templates = await provider.GetTemplatesAsync(TestContext.Current.CancellationToken);
        var cold = stopwatch.Elapsed;
        stopwatch.Restart();
        await provider.GetTemplatesAsync(TestContext.Current.CancellationToken);
        TestContext.Current.SendDiagnosticMessage($"{templates.Count} templates in {cold.TotalMilliseconds:0} ms, again in {stopwatch.Elapsed.TotalMilliseconds:0} ms");

        var console = Find(templates, "console");
        Assert.Equal(TemplateCategories.Console, console.Category);
        Assert.Equal(["C#", "F#", "VB"], console.Options.Options.Single(o => o.Id == TemplateCatalog.LanguageOption).Choices.Select(c => c.Value));
        Assert.Equal(TemplateCategories.Library, Find(templates, "classlib").Category);
        Assert.Equal(TemplateCategories.Web, Find(templates, "webapi").Category);
        Assert.Equal(TemplateCategories.Web, Find(templates, "blazor").Category);
        var tests = (ProjectTemplate[])[Find(templates, "xunit"), Find(templates, "mstest")];
        Assert.NotEmpty(tests);
        Assert.All(tests, t => Assert.Equal(TemplateCategories.Test, t.Category));

        foreach (var template in (ProjectTemplate[])[console, Find(templates, "classlib"), Find(templates, "webapi"), Find(templates, "blazor"), .. tests])
        {
            var framework = template.Options.Options.Single(o => o.Id == "Framework");
            Assert.Equal("Target framework", framework.Label);
            Assert.Contains(framework.Choices, c => c.Value == "net10.0");
            Assert.Empty(template.Options.Validate(template.Options.Defaults()));
            Assert.DoesNotContain(template.Options.Options, o => o.Id is "skipRestore" or "TargetFrameworkOverride");
        }
    }

    [Fact]
    public async Task CreatesAConsoleAppInASolution()
    {
        var provider = Provider();
        provider.SkipRestore = true;
        var templates = await provider.GetTemplatesAsync(TestContext.Current.CancellationToken);
        var console = Find(templates, "console");
        var values = console.Options.Defaults();
        values.Set(TemplateCatalog.SolutionNameOption, "Demos");
        var steps = new List<string>();

        var result = await console.CreateWith(provider, new ProjectCreationRequest("Demo", folder, values), new SynchronousProgress(steps.Add));

        var created = Path.Combine(folder, "Demo");
        Assert.Equal(created, result.Folder);
        Assert.True(File.Exists(Path.Combine(created, "Demo", "Demo.csproj")));
        Assert.Equal([Path.Combine(created, "Demo", "Program.cs")], result.FilesToOpen);
        var solution = Path.Combine(created, "Demos.slnx");
        Assert.True(File.Exists(solution));
        Assert.Contains("Demo.csproj", await File.ReadAllTextAsync(solution, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(["Creating the project with dotnet new console", "Creating the solution", "Adding the project to the solution"], steps);
    }

    [Fact]
    public async Task CreatesAProjectAloneInTheSameDirectory()
    {
        var provider = Provider();
        provider.SkipRestore = true;
        var console = Find(await provider.GetTemplatesAsync(TestContext.Current.CancellationToken), "console");
        var values = console.Options.Defaults();
        values.Set(TemplateCatalog.LanguageOption, "F#");
        values.Set(TemplateCatalog.SameDirectoryOption, "true");

        var result = await console.CreateWith(provider, new ProjectCreationRequest("Tool", folder, values), new SynchronousProgress(_ => { }));

        Assert.True(File.Exists(Path.Combine(folder, "Tool", "Tool.fsproj")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(folder, "Tool"), "*.sln*"));
        Assert.Equal([Path.Combine(folder, "Tool", "Program.fs")], result.FilesToOpen);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private DotnetTemplateProvider Provider()
    {
        var roots = TemplatePackages.FindRootsFromEnvironment();
        if (!roots.Any(root => TemplatePackages.FindSdkPackages(root).Any()))
            Assert.Skip("No .NET SDK with templates is on this machine; set DOTNET_ROOT.");

        return new DotnetTemplateProvider(_ => Task.FromResult(roots), Path.Combine(folder, ".templateengine"))
        {
            DefaultDotnet = TemplatePackages.FindDotnetOnPath() ?? TemplatePackages.DotnetIn(roots[0]),
        };
    }

    private static ProjectTemplate Find(IReadOnlyList<ProjectTemplate> templates, string shortName) =>
        Assert.Single(templates, t => t.Data is DotnetTemplateData { IsBlankSolution: false } data
            && data.Variants.Values.Any(v => v.ShortName == shortName));

    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}

internal static class TemplateExtensions
{
    public static Task<ProjectCreationResult> CreateWith(this ProjectTemplate template, IProjectTemplateProvider provider, ProjectCreationRequest request,
        IProgress<string> progress) => provider.CreateAsync(template, request, progress, TestContext.Current.CancellationToken);
}
