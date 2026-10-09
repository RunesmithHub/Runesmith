using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Tests.Running;

public sealed class RunConfigurationsDialogTests
{
    private static readonly FakeType Type = new()
    {
        Options = new([new Option("command", "Command", OptionKind.Text), new Option("sdk", "SDK", OptionKind.Sdk) { SdkKind = "dotnet" }]),
    };

    private static readonly Dictionary<string, string> Defaults = new() { ["dotnet"] = "/sdks/dotnet" };

    [Fact]
    public void ADetectedConfigurationStaysDetectedUntilTheUserChangesIt()
    {
        var detected = new RunConfiguration("fake", "App", Values(("command", "echo"))) { IsDetected = true };
        var entry = RunConfigurationsDialog.Entry.From(detected, Type, Type.Options);

        entry.Values.Set("sdk", "/sdks/dotnet");
        Assert.True(entry.ToConfiguration(Defaults).IsDetected);

        entry.Values.Set("command", "echo changed");
        var changed = entry.ToConfiguration(Defaults);
        Assert.False(changed.IsDetected);
        Assert.Equal("echo changed", changed.Values.Get("command"));
        Assert.Null(changed.Values.Get("sdk"));
        Assert.DoesNotContain(changed.Values.All.Keys, k => k.StartsWith('$'));
    }

    [Fact]
    public void NameStorageAndStepsComeFromTheFormAndAChosenSdkIsKept()
    {
        var saved = new RunConfiguration("fake", "App", Values(("sdk", "/sdks/dotnet")));
        var entry = RunConfigurationsDialog.Entry.From(saved, Type, Type.Options);

        entry.Values.Set(RunConfigurationsDialog.NameId, " Renamed ");
        entry.Values.Set(RunConfigurationsDialog.StoreId, "local");
        entry.Steps.Insert(0, new BeforeLaunchStep(BeforeLaunchKind.Command, "make"));
        var configuration = entry.ToConfiguration(Defaults);

        Assert.Equal("Renamed", configuration.Name);
        Assert.True(configuration.IsLocal);
        Assert.Equal([new BeforeLaunchStep(BeforeLaunchKind.Command, "make"), new BeforeLaunchStep(BeforeLaunchKind.Build)], configuration.BeforeLaunch);
        Assert.Equal("/sdks/dotnet", configuration.Values.Get("sdk"));
        Assert.Equal("App (copy)", entry.Copy("App (copy)").ToConfiguration(Defaults).Name);
    }

    private static OptionValues Values(params (string Id, string Value)[] pairs)
    {
        var values = new OptionValues();
        foreach (var (id, value) in pairs)
            values.Set(id, value);
        return values;
    }
}
