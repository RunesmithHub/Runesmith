using Runesmith.Sdk.Shell;
using Runesmith.Shell.QuickInput;

namespace Runesmith.Shell.Tests.QuickInput;

public sealed class PickModelTests
{
    private static readonly QuickPickOptions Options = new();

    private static PickRow Row(string label, string? description = null, string? detail = null, bool selected = false) =>
        PickRow.From(new PickItem<string>(label, label) { Description = description, Detail = detail, IsSelected = selected });

    private static PickRow Separator(string label) => PickRow.From(PickItem.Separator<string>(label));

    private static string[] Labels(IEnumerable<PickRow> rows) => [.. rows.Select(r => r.IsSeparator ? $"--{r.Label}" : r.Label)];

    [Fact]
    public void FilteringMatchesLabelsAndPutsTheBestFirstWhenThereAreNoGroups()
    {
        var rows = new[] { Row("Reset Layout"), Row("Show Output"), Row("Settings") };

        var shown = PickModel.Filter(rows, "sett", Options);

        Assert.Equal(["Settings", "Reset Layout"], Labels(shown));
        Assert.Equal([0, 1, 2, 3], shown[0].Matches);
        Assert.Same(rows, PickModel.Filter(rows, "  ", Options));
    }

    [Fact]
    public void FilteringKeepsGroupsInOrderWithTheirSeparators()
    {
        var rows = new[] { Separator("Local"), Row("main"), Row("feature/login"), Separator("Remote"), Row("origin/main"), Row("origin/fix") };

        Assert.Equal(["--Local", "main", "--Remote", "origin/main"], Labels(PickModel.Filter(rows, "main", Options)));
        Assert.Equal(["--Remote", "origin/fix"], Labels(PickModel.Filter(rows, "fix", Options)));
    }

    [Fact]
    public void DescriptionsAndDetailsMatchOnlyWhenAskedTo()
    {
        var rows = new[] { Row("Program.cs", description: "src/App"), Row("README.md", detail: "How to build the app") };

        Assert.Empty(PickModel.Filter(rows, "app", Options));
        Assert.Equal(["Program.cs"], Labels(PickModel.Filter(rows, "app", Options with { MatchOnDescription = true })));
        Assert.Equal(["Program.cs", "README.md"], Labels(PickModel.Filter(rows, "app", Options with { MatchOnDescription = true, MatchOnDetail = true })));
        Assert.Empty(PickModel.Filter(rows, "app", Options with { MatchOnDescription = true })[0].Matches);
    }

    [Fact]
    public async Task MovingSkipsSeparatorsAndWrapsAround()
    {
        using var model = new PickModel([Separator("A"), Row("one"), Row("two"), Separator("B"), Row("three")], Options, canPickMany: false);
        await model.SetQueryAsync("");

        Assert.Equal("one", model.Active?.Label);
        model.Move(1);
        model.Move(1);
        Assert.Equal("three", model.Active?.Label);
        model.Move(1);
        Assert.Equal("one", model.Active?.Label);
        model.Move(-1);
        Assert.Equal("three", model.Active?.Label);
        model.Move(-10);
        Assert.Equal("one", model.Active?.Label);
    }

    [Fact]
    public async Task APickOfOneStartsOnTheSelectedItem()
    {
        using var model = new PickModel([Row("one"), Row("two", selected: true)], Options, canPickMany: false);
        await model.SetQueryAsync("");

        Assert.Equal("two", model.Active?.Label);
        Assert.Equal(["two"], Labels(model.Accept()!));
    }

    [Fact]
    public async Task APickOfManyChecksTheSelectedItemsAndGivesTheCheckedOnes()
    {
        using var model = new PickModel([Row("one", selected: true), Row("two"), Row("three")], Options, canPickMany: true);
        await model.SetQueryAsync("");

        Assert.Equal(["one"], Labels(model.Picked));
        model.Move(1);
        model.Toggle();
        model.Toggle(model.Rows[0]);
        Assert.Equal(["two"], Labels(model.Accept()!));

        model.SetAllChecked(true);
        Assert.Equal(3, model.Picked.Count);
        model.SetAllChecked(false);
        Assert.Equal(["two"], Labels(model.Accept()!));
    }

    [Fact]
    public async Task ASourceIsAskedAtOnceThenAfterAPauseAndEarlierSearchesAreCancelled()
    {
        var delays = new List<TaskCompletionSource>();
        var asked = new List<(string Query, CancellationToken Token)>();
        var answers = new Dictionary<string, TaskCompletionSource<IReadOnlyList<PickRow>>>();
        using var model = new PickModel((query, token) =>
        {
            asked.Add((query, token));
            answers[query] = new TaskCompletionSource<IReadOnlyList<PickRow>>();
            return answers[query].Task;
        }, Options, canPickMany: false, delay: (_, token) =>
        {
            var pause = new TaskCompletionSource();
            token.Register(() => pause.TrySetCanceled(token));
            delays.Add(pause);
            return pause.Task;
        });

        var first = model.SetQueryAsync("");
        Assert.Equal([""], asked.Select(a => a.Query));
        Assert.True(model.IsBusy);
        answers[""].SetResult([Row("alpha"), Row("beta")]);
        await first;
        Assert.False(model.IsBusy);
        Assert.Equal(["alpha", "beta"], Labels(model.Rows));

        var typedA = model.SetQueryAsync("a");
        var typedAl = model.SetQueryAsync("al");
        await typedA;
        Assert.Equal(2, delays.Count);
        Assert.True(delays[0].Task.IsCanceled);
        delays[1].SetResult();
        await Task.Yield();
        Assert.Equal(["", "al"], asked.Select(a => a.Query));

        var typedAlp = model.SetQueryAsync("alp");
        Assert.True(asked[1].Token.IsCancellationRequested);
        answers["al"].SetResult([Row("stale")]);
        delays[2].SetResult();
        await Task.Yield();
        answers["alp"].SetResult([Row("alpha")]);
        await Task.WhenAll(typedAl, typedAlp);

        Assert.Equal(["alpha"], Labels(model.Rows));
        Assert.Equal([0, 1, 2], model.Rows[0].Matches);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task ASourceThatFailsShowsWhyAndReportsIt()
    {
        var failures = new List<Exception>();
        using var model = new PickModel((_, _) => Task.FromException<IReadOnlyList<PickRow>>(new InvalidOperationException("offline")), Options, canPickMany: false);
        model.Failed += (_, e) => failures.Add(e);

        await model.SetQueryAsync("");

        Assert.Equal("offline", model.Error);
        Assert.Empty(model.Rows);
        Assert.Single(failures);
        Assert.Null(model.Accept());
    }

    [Fact]
    public async Task CheckedItemsStayCheckedWhenASourceFindsThemAgain()
    {
        using var model = new PickModel((query, _) => Task.FromResult<IReadOnlyList<PickRow>>([Row("one"), Row("two", selected: true)]), Options with { SearchDelay = TimeSpan.Zero }, canPickMany: true);
        await model.SetQueryAsync("");
        Assert.Equal(["two"], Labels(model.Picked));
        model.Toggle(model.Rows[0]);
        model.Toggle(model.Rows[1]);

        await model.SetQueryAsync("o");

        Assert.True(model.Rows[0].IsChecked);
        Assert.False(model.Rows[1].IsChecked);
        Assert.Equal(["one"], Labels(model.Picked));
    }

    [Fact]
    public async Task AnInputBoxChecksTheTextAfterAPauseAndRefusesWhatIsWrong()
    {
        var pauses = 0;
        using var model = new InputModel(new InputBoxOptions
        {
            Value = "x",
            Validate = (text, _) => Task.FromResult(text.Length < 3 ? "Type at least 3 characters." : null),
        }, (_, _) =>
        {
            pauses++;
            return Task.CompletedTask;
        });

        await model.SetTextAsync("ab");
        Assert.Equal(1, pauses);
        Assert.Equal("Type at least 3 characters.", model.Error);
        Assert.False(await model.TryAcceptAsync());

        await model.SetTextAsync("abc", immediate: true);
        Assert.Null(model.Error);
        Assert.True(await model.TryAcceptAsync());
        Assert.Equal(1, pauses);
    }

    [Fact]
    public async Task AnInputBoxWhoseCheckFailsRefusesTheTextAndReportsIt()
    {
        var failures = new List<Exception>();
        using var model = new InputModel(new InputBoxOptions { Validate = (_, _) => throw new InvalidOperationException("bug") });
        model.Failed += (_, e) => failures.Add(e);

        Assert.False(await model.TryAcceptAsync());
        Assert.NotNull(model.Error);
        Assert.Single(failures);
    }
}
