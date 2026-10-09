using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Tests.Running;

public sealed class RunConfigurationServiceTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-run-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-run-state-").FullName;

    [Fact]
    public void RunJsonRoundTripsAsPrettyJson()
    {
        var values = new OptionValues();
        values.Set("project", "src/App/App.csproj");
        values.Set("arguments", "--verbose\n--port=5000");
        RunConfiguration[] saved =
        [
            new("dotnet.project", "App", values) { BeforeLaunch = [new(BeforeLaunchKind.Command, "npm install"), new(BeforeLaunchKind.Build)] },
            new("java.maven", "Package", new OptionValues()) { BeforeLaunch = [] },
        ];

        RunConfigurationFile.WriteShared(root, saved);
        var text = File.ReadAllText(RunConfigurationFile.SharedPath(root));
        var read = RunConfigurationFile.ReadShared(root);

        Assert.Contains("\n  \"configurations\": [", text, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"dotnet.project\"", text, StringComparison.Ordinal);
        Assert.Contains("\"beforeLaunch\"", text, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"command\"", text, StringComparison.Ordinal);
        Assert.Equal(2, read.Count);
        Assert.Equal("App", read[0].Name);
        Assert.Equal("dotnet.project", read[0].TypeId);
        Assert.Equal(["--verbose", "--port=5000"], read[0].Values.GetList("arguments"));
        Assert.Equal([new BeforeLaunchStep(BeforeLaunchKind.Command, "npm install"), new BeforeLaunchStep(BeforeLaunchKind.Build)], read[0].BeforeLaunch);
        Assert.Empty(read[1].BeforeLaunch);
        Assert.All(read, c => Assert.False(c.IsLocal || c.IsDetected));
    }

    [Fact]
    public void MissingOrDamagedFilesReadAsEmptyAndNoConfigurationsRemoveTheFile()
    {
        Assert.Empty(RunConfigurationFile.ReadShared(root));
        Directory.CreateDirectory(Path.Combine(root, ".runesmith"));
        File.WriteAllText(RunConfigurationFile.SharedPath(root), "{ not json");
        Assert.Empty(RunConfigurationFile.ReadShared(root));

        RunConfigurationFile.WriteShared(root, []);

        Assert.False(File.Exists(RunConfigurationFile.SharedPath(root)));
    }

    [Fact]
    public void LocalStateKeepsLocalConfigurationsTheSelectionAndRecentRuns()
    {
        var path = RunConfigurationFile.LocalPath(state, root);
        RunConfigurationFile.WriteLocal(path, new RunLocalState([new RunConfiguration("fake", "Mine", new OptionValues()) { IsLocal = true }], "Mine", ["Mine", "App"]));

        var read = RunConfigurationFile.ReadLocal(path);

        Assert.Equal("Mine", Assert.Single(read.Configurations).Name);
        Assert.True(read.Configurations[0].IsLocal);
        Assert.Equal("Mine", read.Selected);
        Assert.Equal(["Mine", "App"], read.Recent);
    }

    [Fact]
    public void SavedConfigurationsReplaceDetectedOnesInPlaceAndTheRestFollow()
    {
        RunConfiguration[] detected = [Config("App"), Config("Worker"), Config("App")];
        var edited = Config("Worker", "echo edited");
        RunConfiguration[] saved = [Config("Mine"), edited];

        var merged = RunConfigurationMerger.Merge(detected, saved);

        Assert.Equal(["App", "Worker", "Mine"], merged.Select(c => c.Name));
        Assert.True(merged[0].IsDetected);
        Assert.Same(edited, merged[1]);
        Assert.False(merged[2].IsDetected);
    }

    [Fact]
    public async Task LoadsSavedAndDetectedConfigurationsAndRemembersTheSelection()
    {
        RunConfigurationFile.WriteShared(root, [Config("Shared")]);
        var type = new FakeType { Detected = [Config("App"), Config("Tool")] };
        var workspace = new FakeWorkspace(null);
        using var service = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], workspace, null, state);
        var changes = 0;
        service.Changed += (_, _) => changes++;

        await service.LoadAsync(root);

        Assert.Equal(["App", "Tool", "Shared"], service.Configurations.Select(c => c.Name));
        Assert.Equal("App", service.Selected?.Name);
        Assert.False(service.IsDetecting);
        Assert.True(changes >= 2);

        service.Select(service.Find("Tool")!);
        service.MarkRun(service.Find("Shared")!);
        using var reopened = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], workspace, null, state);
        await reopened.LoadAsync(root);

        Assert.Equal("Tool", reopened.Selected?.Name);
        Assert.Equal(["Shared"], reopened.Recent);
    }

    [Fact]
    public async Task SavingWritesSharedAndLocalOnesAndLeavesDetectedOnesOut()
    {
        var type = new FakeType { Detected = [Config("App"), Config("Tool")] };
        using var service = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], new FakeWorkspace(null), null, state);
        await service.LoadAsync(root);

        var edited = service.Find("Tool")! with { IsDetected = false };
        service.Save([service.Find("App")!, edited, Config("Mine") with { IsLocal = true }], selected: "Mine");

        Assert.Equal("Tool", Assert.Single(RunConfigurationFile.ReadShared(root)).Name);
        Assert.Equal("Mine", Assert.Single(RunConfigurationFile.ReadLocal(RunConfigurationFile.LocalPath(state, root)).Configurations).Name);
        Assert.Equal("Mine", service.Selected?.Name);

        using var reopened = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], new FakeWorkspace(null), null, state);
        await reopened.LoadAsync(root);
        Assert.Equal(["App", "Tool", "Mine"], reopened.Configurations.Select(c => c.Name));
        Assert.Equal([true, false, false], reopened.Configurations.Select(c => c.IsDetected));
        Assert.Equal("Mine", reopened.Selected?.Name);
    }

    [Fact]
    public async Task ATypeThatFailsToDetectLeavesTheOthers()
    {
        var good = new FakeType("good") { Detected = [Config("App") with { TypeId = "good" }] };
        Lazy<IRunConfigurationType>[] types = [new(() => throw new InvalidOperationException("broken plugin")), new(() => good)];
        using var service = new RunConfigurationService(types, new FakeWorkspace(null), null, state);

        await service.LoadAsync(root);

        Assert.Equal("App", Assert.Single(service.Configurations).Name);
        Assert.Same(good, Assert.Single(service.Types));
    }

    private static RunConfiguration Config(string name, string command = "echo hi")
    {
        var values = new OptionValues();
        values.Set("command", command);
        return new RunConfiguration("fake", name, values);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }
}
