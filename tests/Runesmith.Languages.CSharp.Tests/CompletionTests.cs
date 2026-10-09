using System.Diagnostics;
using Runesmith.LanguageServices;

namespace Runesmith.Languages.CSharp.Tests;

public sealed class CompletionTests
{
    private static readonly Dictionary<string, string> Console = new()
    {
        ["App.csproj"] = TestProject.ProjectFile(),
        ["Program.cs"] = "Console.WriteLine();\n",
    };

    [Fact]
    public async Task MemberAccessOffersTheMembersOfTheType()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.$$\n");

        var result = await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');

        Assert.Contains(result.Items, item => item.Label == "WriteLine");
        Assert.Equal(offset, result.ReplaceSpan.Start);
    }

    [Fact]
    public async Task TheBestMatchForTheTypedWordComesFirst()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.WriteL$$\n");

        var result = await project.CompleteAsync("Program.cs", offset);

        Assert.Equal("WriteLine", result.Items[0].Label);
        Assert.Equal("Console.".Length, result.ReplaceSpan.Start);
    }

    [Fact]
    public async Task LocalsComeBeforeTypesThatMatchAsWell()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "var Console1 = 1;\nvar total = Consol$$\n");

        var result = await project.CompleteAsync("Program.cs", offset);
        var local = result.Items.ToList().FindIndex(item => item.Label == "Console1");
        var type = result.Items.ToList().FindIndex(item => item.Label == "Console");

        Assert.True(local >= 0 && type >= 0, string.Join(", ", result.Items.Take(10).Select(item => item.Label)));
        Assert.Equal(CompletionKind.Variable, result.Items[local].Kind);
        Assert.Equal(0, result.Items[local].SortGroup);
        Assert.Equal(2, result.Items[type].SortGroup);
    }

    [Fact]
    public async Task TypingMoreOfTheSameWordFiltersTheKeptListWithoutTheCompiler()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.$$\n");
        await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');
        var computed = project.Analyzer.ComputedCompletionLists;

        var timings = new List<double>();
        foreach (var character in "WriteL")
        {
            offset = project.Insert("Program.cs", offset, character.ToString());
            var stopwatch = Stopwatch.StartNew();
            var result = await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Incomplete);
            timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            Assert.NotEmpty(result.Items);
        }

        Assert.Equal(computed, project.Analyzer.ComputedCompletionLists);
        Assert.Equal("WriteLine", (await project.CompleteAsync("Program.cs", offset)).Items[0].Label);
        TestContext.Current.TestOutputHelper?.WriteLine($"Filtering the kept list: {string.Join(", ", timings.Select(t => $"{t:0.00} ms"))}");
    }

    [Fact]
    public async Task AnEditBeforeTheWordAsksTheCompilerAgain()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.$$\n");
        await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');
        var computed = project.Analyzer.ComputedCompletionLists;

        project.Insert("Program.cs", 0, "var x = 1;\n");
        await project.CompleteAsync("Program.cs", offset + "var x = 1;\n".Length);

        Assert.Equal(computed + 1, project.Analyzer.ComputedCompletionLists);
    }

    [Fact]
    public async Task ResolvingASuggestionGivesItsSignatureAndDocumentation()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.WriteLine$$\n");
        var result = await project.CompleteAsync("Program.cs", offset);

        var details = await project.Host.ResolveAsync(result.Items.First(item => item.Label == "WriteLine"), TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Contains("WriteLine", details.Detail);
        Assert.Empty(details.AdditionalChanges);
    }

    [Fact]
    public async Task ChangesReachTheCompilerIncrementally()
    {
        await using var project = await TestProject.CreateAsync(Console);
        project.Open("Program.cs", "\n");
        var offset = project.Insert("Program.cs", 0, "var greeting = \"hi\";\n");
        offset = project.Insert("Program.cs", offset, "gree");

        var result = await project.CompleteAsync("Program.cs", offset);

        Assert.Equal("greeting", result.Items[0].Label);
    }

    [Fact]
    public async Task ManyEditsWithoutARequestDoNotOverflowTheStackLater()
    {
        await using var project = await TestProject.CreateAsync(Console);
        var offset = project.Open("Program.cs", "Console.$$\n");
        for (var i = 0; i < 50_000; i++)
        {
            project.Insert("Program.cs", 0, " ");
            project.Remove("Program.cs", 0, 1);
        }

        var result = await project.CompleteAsync("Program.cs", offset, CompletionTriggerKind.Character, '.');

        Assert.Contains(result.Items, item => item.Label == "WriteLine");
    }

    [Fact]
    public async Task AFileOutsideAnyProjectGetsCompletionFromTheNewestLanguage()
    {
        await using var project = await TestProject.CreateAsync();
        var offset = project.Open("Loose.cs", "var items = new List<int>();\nitems.$$\n");

        var result = await project.CompleteAsync("Loose.cs", offset, CompletionTriggerKind.Character, '.');

        Assert.Contains(result.Items, item => item.Label == "Add");
    }
}
