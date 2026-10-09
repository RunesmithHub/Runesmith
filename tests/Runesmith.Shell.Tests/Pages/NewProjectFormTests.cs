using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Session;

namespace Runesmith.Shell.Tests.Pages;

public sealed class NewProjectFormTests : IDisposable
{
    private static readonly ProjectTemplate Console = new("test.console", "Console App", "C#", TemplateCategories.Console)
    {
        Options = new OptionSet(
        [
            new Option("Framework", "Target framework", OptionKind.Choice) { Default = "net10.0", Choices = [new("net10.0", ".NET 10.0"), new("net9.0", ".NET 9.0")] },
            new Option("aot", "Native AOT", OptionKind.Toggle) { Default = "false" },
        ]),
    };

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-new-project-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void TheDialogFieldsComeFirstAndTheTemplateValuesLeaveThemOut()
    {
        var options = NewProjectForm.Options(Console, offerGit: true);
        Assert.Equal([NewProjectForm.Name, NewProjectForm.Location, NewProjectForm.Git, "Framework", "aot"], options.Options.Select(o => o.Id));

        var values = options.Defaults();
        values.Set(NewProjectForm.Name, "Hello");
        Assert.Equal(["Framework", "aot"], NewProjectForm.TemplateValues(values).All.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(NewProjectForm.Git, NewProjectForm.Options(Console, offerGit: false).Options.Select(o => o.Id));
    }

    [Fact]
    public void RememberedValuesApplyOverTheDefaultsAndOnlyChangesAreRemembered()
    {
        var values = NewProjectForm.InitialValues(Console, new Dictionary<string, string> { ["aot"] = "true", ["gone"] = "x" });
        Assert.Equal("true", values.Get("aot"));
        Assert.Equal("net10.0", values.Get("Framework"));
        Assert.Null(values.Get("gone"));

        values.Set(NewProjectForm.Name, "Hello");
        Assert.Equal(new Dictionary<string, string> { ["aot"] = "true" }, NewProjectForm.ChangedValues(Console, values));
    }

    [Theory]
    [InlineData("Hello", null)]
    [InlineData("hello-world.api", null)]
    [InlineData(" Hello", "A name cannot start or end with a space.")]
    [InlineData("a/b", "A name cannot contain / \\ : * ? \" < > |.")]
    [InlineData("name.", "A name cannot end with a dot.")]
    public void NamesMustBeAbleToNameAFolder(string name, string? problem) => Assert.Equal(problem, NewProjectForm.NameProblem(name));

    [Fact]
    public void AFolderWithFilesInTheWayIsAProblemButAnEmptyOneIsNot()
    {
        var values = new OptionValues();
        values.Set(NewProjectForm.Name, "Taken");
        values.Set(NewProjectForm.Location, folder);
        Assert.Equal(Path.Combine(folder, "Taken"), NewProjectForm.TargetFolder(values));
        Assert.Empty(NewProjectForm.Validate(values));

        Directory.CreateDirectory(Path.Combine(folder, "Taken"));
        Assert.Empty(NewProjectForm.Validate(values));

        File.WriteAllText(Path.Combine(folder, "Taken", "file.txt"), "");
        Assert.Equal("Taken exists in this location and is not empty.", NewProjectForm.Validate(values)[NewProjectForm.Name]);

        values.Set(NewProjectForm.Location, "relative/path");
        Assert.Equal("Type a full path, or choose a folder.", NewProjectForm.Validate(values)[NewProjectForm.Location]);
    }

    [Fact]
    public void ASuggestedNameComesFromTheTemplateAndSkipsFoldersThatExist()
    {
        Assert.Equal("ConsoleApp1", NewProjectForm.SuggestName(Console, folder));
        Directory.CreateDirectory(Path.Combine(folder, "ConsoleApp1"));
        Assert.Equal("ConsoleApp2", NewProjectForm.SuggestName(Console, folder));
    }

    [Fact]
    public void ACreatedProjectPutsItsTemplateFirstInRecentAndKeepsItsChanges()
    {
        var session = NewProjectSession.Empty
            .WithCreated("a", "/x", new Dictionary<string, string> { ["aot"] = "true" }, createGitRepository: true)
            .WithCreated("b", "/y", new Dictionary<string, string>(), createGitRepository: false)
            .WithCreated("a", "/z", new Dictionary<string, string>(), createGitRepository: false);

        Assert.Equal(["a", "b"], session.Recent);
        Assert.Equal("/z", session.Location);
        Assert.False(session.Values.ContainsKey("a"));
        Assert.False(session.CreateGitRepository);
    }
}
