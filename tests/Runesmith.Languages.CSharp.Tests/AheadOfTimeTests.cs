using Runesmith.LanguageServices;

namespace Runesmith.Languages.CSharp.Tests;

/// <summary>Lists computed before the user asks: the next word's after a separator, and the members after a dot while an identifier is typed.</summary>
public sealed class AheadOfTimeTests
{
    private static readonly Dictionary<string, string> Files = new()
    {
        ["App.csproj"] = TestProject.ProjectFile(),
        ["Program.cs"] = "\n",
    };

    [Fact]
    public async Task TheListForTheNextWordIsReadyBeforeItsFirstLetter()
    {
        await using var project = await TestProject.CreateAsync(Files);
        var offset = project.Open("Program.cs", "var total = 1;$$\n");
        var before = project.Analyzer.ComputedCompletionLists;

        offset = project.Insert("Program.cs", offset, "\n");
        await WaitForAsync(() => project.Analyzer.ComputedCompletionLists > before);
        var computed = project.Analyzer.ComputedCompletionLists;
        offset = project.Insert("Program.cs", offset, "tot");
        var result = await project.CompleteAsync("Program.cs", offset);

        Assert.Equal(computed, project.Analyzer.ComputedCompletionLists);
        Assert.Equal("total", result.Items[0].Label);
    }

    [Fact]
    public async Task TheMembersAfterADotAreReadyWhenTheDotIsTyped()
    {
        await using var project = await TestProject.CreateAsync(Files);
        var offset = project.Open("Program.cs", "var numbers = new List<int>();\n$$\n");

        foreach (var character in "numbers")
            offset = project.Insert("Program.cs", offset, character.ToString());
        await WaitForAsync(() => project.Analyzer.ComputedCompletionLists > 0);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        var computed = project.Analyzer.ComputedCompletionLists;
        offset = project.Insert("Program.cs", offset, ".");
        var result = await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');

        Assert.Equal(computed, project.Analyzer.ComputedCompletionLists);
        Assert.Equal(1, project.Analyzer.SpeculationsUsed);
        Assert.Contains(result.Items, item => item.Label == "Add");
        Assert.Equal(offset, result.ReplaceSpan.Start);
    }

    [Fact]
    public async Task ASpeculationIsNotUsedWhenSomethingElseIsTyped()
    {
        await using var project = await TestProject.CreateAsync(Files);
        var offset = project.Open("Program.cs", "var numbers = new List<int>();\n$$\n");
        foreach (var character in "numbers")
            offset = project.Insert("Program.cs", offset, character.ToString());
        await WaitUntilQuietAsync(project);

        offset = project.Insert("Program.cs", offset, " ");
        offset = project.Insert("Program.cs", offset, ".");
        var result = await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');

        Assert.Equal(0, project.Analyzer.SpeculationsUsed);
        Assert.Contains(result.Items, item => item.Label == "Add");
    }

    // Work ahead of time runs in the background; this waits until no list was computed for a while, however slow the machine is.
    private static async Task WaitUntilQuietAsync(TestProject project)
    {
        var last = -1;
        for (var quiet = 0; quiet < 8;)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var now = project.Analyzer.ComputedCompletionLists;
            quiet = now == last ? quiet + 1 : 0;
            last = now;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition(), "The list was not computed ahead of time.");
    }
}
