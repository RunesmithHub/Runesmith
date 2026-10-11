using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Runesmith.Sdk.Analyzers.Tests;

/// <summary>Builds small projects with <c>dotnet build</c> and the SDK's localization targets, as a plugin's build runs them.</summary>
public sealed class TargetsTests : IDisposable
{
    private static readonly string Tools = AppContext.BaseDirectory;
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-targets-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public async Task APluginEmbedsItsFluentFilesGeneratesStringsAndReportsGapsInTranslations()
    {
        Write("plugin.json", """{ "id": "acme.todo", "languages": ["en", "nl"], "projects": { "contracts": "Acme.Todo.Contracts/Acme.Todo.Contracts.csproj", "implementation": "Acme.Todo/Acme.Todo.csproj" } }""");
        Write("Acme.Todo/Acme.Todo.csproj", Project());
        Write("Acme.Todo/locales/en/acme.todo.ftl", "# The panel's title.\npanel-title = To-dos\ntodo-count = { $count ->\n    [one] One to-do\n   *[other] { $count } to-dos\n}\n");
        Write("Acme.Todo/locales/nl/acme.todo.ftl", "panel-title = Taken\n");
        Write("Acme.Todo/Use.cs", "namespace Acme.Todo;\n\npublic static class Use\n{\n    public static string Title() => Strings.PanelTitle + Strings.TodoCount(2);\n}\n");

        var (exitCode, output) = await BuildAsync("Acme.Todo/Acme.Todo.csproj");

        Assert.True(exitCode == 0, output);
        Assert.Contains("warning RSL1003", output, StringComparison.Ordinal);
        Assert.Contains("todo-count", output, StringComparison.Ordinal);
        var (resources, types) = Read(Path.Combine(root, "Acme.Todo", "bin", "Debug", "net10.0", "Acme.Todo.dll"));
        Assert.Equal(["locales/en/acme.todo.ftl", "locales/nl/acme.todo.ftl"], resources.Order(StringComparer.Ordinal));
        Assert.Contains("Acme.Todo.Strings", types);
    }

    [Fact]
    public async Task ASyntaxErrorInTheEnglishSourceFailsTheBuildBeforeTheCodeCompiles()
    {
        Write("Acme.Todo/Acme.Todo.csproj", Project());
        Write("Acme.Todo/locales/en/acme.todo.ftl", "fine = Fine\nbroken = { $x\n");

        var (exitCode, output) = await BuildAsync("Acme.Todo/Acme.Todo.csproj");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("acme.todo.ftl(2,", output, StringComparison.Ordinal);
        Assert.Contains("error RSL1001", output, StringComparison.Ordinal);
        Assert.DoesNotContain("error CS", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALanguagePackEmbedsItsFilesUnderTheirTargets()
    {
        Write("plugin.json", """{ "id": "acme.dutch", "languagePack": { "language": "nl", "name": "Nederlands", "englishName": "Dutch" }, "capabilities": [], "projects": { "contracts": "Acme.Dutch.Contracts/Acme.Dutch.Contracts.csproj", "implementation": "Acme.Dutch/Acme.Dutch.csproj" } }""");
        Write("Acme.Dutch/Acme.Dutch.csproj", Project());
        Write("Acme.Dutch/locales/nl/runesmith/Runesmith.Shell.ftl", "settings = Instellingen\n");
        Write("Acme.Dutch/locales/nl/acme.todo/acme.todo.ftl", "panel-title = Taken\n");

        var (exitCode, output) = await BuildAsync("Acme.Dutch/Acme.Dutch.csproj");

        Assert.True(exitCode == 0, output);
        var (resources, types) = Read(Path.Combine(root, "Acme.Dutch", "bin", "Debug", "net10.0", "Acme.Dutch.dll"));
        Assert.Equal(["locales/nl/acme.todo/acme.todo.ftl", "locales/nl/runesmith/Runesmith.Shell.ftl"], resources.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(types, t => t.EndsWith(".Strings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AProjectWithoutFluentFilesBuildsAsBefore()
    {
        Write("Acme.Todo.Contracts/Acme.Todo.Contracts.csproj", Project());
        Write("Acme.Todo.Contracts/ITodos.cs", "namespace Acme.Todo;\n\npublic interface ITodos;\n");

        var (exitCode, output) = await BuildAsync("Acme.Todo.Contracts/Acme.Todo.Contracts.csproj");

        Assert.True(exitCode == 0, output);
        var (resources, types) = Read(Path.Combine(root, "Acme.Todo.Contracts", "bin", "Debug", "net10.0", "Acme.Todo.Contracts.dll"));
        Assert.Empty(resources);
        Assert.Contains("Acme.Todo.ITodos", types);
    }

    private static string Project() => $"""
        <Project>
          <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RunesmithPluginRoot>$([MSBuild]::NormalizeDirectory('$(MSBuildProjectDirectory)', '..'))</RunesmithPluginRoot>
            <RunesmithSdkTasksAssembly>{Path.Combine(Tools, "Runesmith.Sdk.Tasks.dll")}</RunesmithSdkTasksAssembly>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="{Path.Combine(Tools, "Runesmith.Sdk.dll")}" />
            <Analyzer Include="{Path.Combine(Tools, "Runesmith.Sdk.Analyzers.dll")}" />
          </ItemGroup>
          <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
          <Import Project="{Path.Combine(Repository.Root, "src", "Runesmith.Sdk", "build", "Runesmith.Sdk.Localization.targets")}" />
        </Project>
        """;

    private static (List<string> Resources, List<string> Types) Read(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        var resources = metadata.ManifestResources.Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name)).ToList();
        var types = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Select(type => $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}").ToList();
        return (resources, types);
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task<(int ExitCode, string Output)> BuildAsync(string project)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in (string[])["build", project, "-nologo", "-nodeReuse:false", "-p:UseSharedCompilation=false", "-clp:NoSummary"])
            start.ArgumentList.Add(argument);
        foreach (var variable in (string[])["MSBuildSDKsPath", "MSBuildExtensionsPath", "MSBUILD_EXE_PATH", "MSBuildLoadMicrosoftTargetsReadOnly"])
            start.Environment.Remove(variable);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }
}
