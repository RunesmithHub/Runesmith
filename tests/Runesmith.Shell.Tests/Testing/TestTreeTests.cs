using Runesmith.Sdk.Testing;
using Runesmith.Shell.Testing;
using TestResult = Runesmith.Shell.Testing.TestResult;

namespace Runesmith.Shell.Tests.Testing;

public sealed class TestTreeTests
{
    private readonly FakeTestProvider provider = new();
    private readonly TestTree tree = new();

    [Fact]
    public void ADiscoveryBuildsTheTreeInOrder()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));

        var project = Assert.Single(tree.RootsOf(provider));
        var type = project.Children[0].Children[0];
        Assert.Equal("CalculatorTests", type.Item.Label);
        Assert.Equal(["Add", "Subtract"], type.Children.Select(c => c.Item.Label));
        Assert.Equal(type, tree.Find(provider, Items.Id("Add"))!.Parent);
        Assert.Equal("Calculator.Tests › Calculator.Tests › CalculatorTests › Add", tree.Find(provider, Items.Id("Add"))!.Path);
        Assert.Equal(2, project.Tests().Count());
        Assert.Empty(tree.RootsOf(new FakeTestProvider()));
    }

    [Fact]
    public void DiscoveringAgainKeepsTheResultsOfTestsThatAreStillThere()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));
        var add = tree.Find(provider, Items.Id("Add"))!;
        tree.SetResults([(add, new TestResult(TestState.Passed))]);

        tree.ReplaceAll(provider, Items.Calculator("Add", "Multiply"));

        Assert.Same(add, tree.Find(provider, Items.Id("Add")));
        Assert.Equal(TestState.Passed, add.State);
        Assert.Null(tree.Find(provider, Items.Id("Subtract")));
        Assert.NotNull(tree.Find(provider, Items.Id("Multiply")));
        Assert.Equal(["Add", "Multiply"], add.Parent!.Children.Select(c => c.Item.Label));
    }

    [Fact]
    public void ANewOrderFromAFullDiscoveryIsKept()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract"));

        tree.ReplaceAll(provider, Items.Calculator("Subtract", "Add"));

        Assert.Equal(["Subtract", "Add"], tree.Find(provider, Items.Id("Add"))!.Parent!.Children.Select(c => c.Item.Label));
    }

    [Fact]
    public void AFileDiscoveryAddsAndRemovesOnlyThatFilesTests()
    {
        tree.ReplaceAll(provider, [Items.Project(Items.Namespace(
            Items.Class("CalculatorTests", Items.File, 2, Items.Test("CalculatorTests", "Add", Items.File, 4)),
            Items.Class("ParserTests", Items.OtherFile, 2, Items.Test("ParserTests", "Parse", Items.OtherFile, 4))))]);
        var changed = new List<IReadOnlyCollection<string>?>();
        tree.Changed += (_, files) => changed.Add(files);

        tree.ReplaceFile(provider, Items.File, Items.Calculator("Divide"));

        Assert.Null(tree.Find(provider, Items.Id("Add")));
        Assert.NotNull(tree.Find(provider, Items.Id("Divide")));
        Assert.NotNull(tree.Find(provider, "Calculator.Tests.ParserTests.Parse"));
        Assert.Equal([Items.File], Assert.Single(changed));
    }

    [Fact]
    public void AFileDiscoveryMovesAnEditedTestsLocationAndKeepsItsResult()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add"));
        var add = tree.Find(provider, Items.Id("Add"))!;
        tree.SetResults([(add, new TestResult(TestState.Failed))]);

        tree.ReplaceFile(provider, Items.File, [Items.Project(Items.Namespace(Items.Class("CalculatorTests", Items.File, 2, Items.Test("CalculatorTests", "Add", Items.File, 20))))]);

        Assert.Same(add, tree.Find(provider, Items.Id("Add")));
        Assert.Equal(20, add.Item.Start!.Value.Line);
        Assert.Equal(TestState.Failed, add.State);
    }

    [Fact]
    public void AFileWithoutTestsAnyMoreLeavesNoEmptyGroups()
    {
        tree.ReplaceAll(provider, [Items.Project(Items.Namespace(
            Items.Class("CalculatorTests", Items.File, 2, Items.Test("CalculatorTests", "Add", Items.File, 4)),
            Items.Class("ParserTests", Items.OtherFile, 2, Items.Test("ParserTests", "Parse", Items.OtherFile, 4))))]);

        tree.ReplaceFile(provider, Items.File, []);

        Assert.Null(tree.Find(provider, "Calculator.Tests.CalculatorTests"));
        Assert.NotNull(tree.Find(provider, "Calculator.Tests"));
        Assert.Single(tree.RootsOf(provider)[0].Children[0].Children);

        tree.ReplaceFile(provider, Items.OtherFile, []);
        Assert.Empty(tree.RootsOf(provider));
    }

    [Fact]
    public void AFileDiscoveryAddsANewClassUnderTheExistingNamespace()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add"));

        tree.ReplaceFile(provider, Items.OtherFile, [Items.Project(Items.Namespace(Items.Class("ParserTests", Items.OtherFile, 2, Items.Test("ParserTests", "Parse", Items.OtherFile, 4))))]);

        var space = tree.Find(provider, "Calculator.Tests")!;
        Assert.Equal(["CalculatorTests", "ParserTests"], space.Children.Select(c => c.Item.Label));
        Assert.Equal(2, tree.InFile(Items.OtherFile).Count);
    }

    [Fact]
    public void ACaseFoundWhileRunningGoesUnderItsTest()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add"));
        var add = tree.Find(provider, Items.Id("Add"))!;
        Assert.True(add.IsTest);

        var node = tree.AddChild(provider, Items.Id("Add"), new TestItem(Items.Id("Add") + "(1, 2)", "Add(1, 2)", TestItemKind.Case));

        Assert.NotNull(node);
        Assert.False(add.IsTest);
        Assert.Same(node, Assert.Single(add.Tests()));
        Assert.Null(tree.AddChild(provider, "missing", new TestItem("x", "x", TestItemKind.Case)));
    }

    [Fact]
    public void GroupsShowTheMostTellingStateOfTheirTests()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add", "Subtract", "Multiply"));
        var add = tree.Find(provider, Items.Id("Add"))!;
        var subtract = tree.Find(provider, Items.Id("Subtract"))!;
        var group = add.Parent!;
        Assert.Equal(TestState.None, group.State);

        tree.SetResults([(add, new TestResult(TestState.Passed) { Duration = TimeSpan.FromMilliseconds(5) })]);
        Assert.Equal(TestState.Passed, group.State);

        tree.SetResults([(subtract, new TestResult(TestState.Running))]);
        Assert.Equal(TestState.Running, group.State);

        tree.SetResults([(subtract, new TestResult(TestState.Failed) { Duration = TimeSpan.FromMilliseconds(7) })]);
        Assert.Equal(TestState.Failed, group.State);
        Assert.Equal(TimeSpan.FromMilliseconds(12), group.Duration);
        Assert.Equal(TestState.Failed, tree.RootsOf(provider)[0].State);
    }

    [Fact]
    public void AggregationRanksErrorsFailuresRunningPassesAndSkips()
    {
        Assert.Equal(TestState.None, TestNode.Aggregate([]));
        Assert.Equal(TestState.Skipped, TestNode.Aggregate([TestState.Skipped, TestState.Skipped]));
        Assert.Equal(TestState.Passed, TestNode.Aggregate([TestState.Skipped, TestState.Passed, TestState.None]));
        Assert.Equal(TestState.Queued, TestNode.Aggregate([TestState.Passed, TestState.Queued]));
        Assert.Equal(TestState.Failed, TestNode.Aggregate([TestState.Running, TestState.Failed]));
        Assert.Equal(TestState.Errored, TestNode.Aggregate([TestState.Failed, TestState.Errored]));
    }

    [Fact]
    public void ResultChangesNameTheFilesOfTheTestAndOfWhereItFailed()
    {
        tree.ReplaceAll(provider, Items.Calculator("Add"));
        var files = new List<string>();
        tree.ResultsChanged += (_, changed) => files.AddRange(changed);
        var failure = new TestFailure("boom") { FilePath = "/src/Calculator.cs", Position = new Runesmith.Text.TextPosition(9, 0) };

        tree.SetResults([(tree.Find(provider, Items.Id("Add"))!, new TestResult(TestState.Failed) { Failure = failure })]);

        Assert.Contains(Items.File, files);
        Assert.Contains("/src/Calculator.cs", files);
    }

    [Fact]
    public void ProvidersKeepTheirTestsApart()
    {
        var other = new FakeTestProvider("Other");
        tree.ReplaceAll(provider, Items.Calculator("Add"));
        tree.ReplaceAll(other, Items.Calculator("Add"));

        Assert.NotSame(tree.Find(provider, Items.Id("Add")), tree.Find(other, Items.Id("Add")));
        Assert.Equal([provider, other], tree.Providers);

        tree.Clear(provider);
        Assert.Null(tree.Find(provider, Items.Id("Add")));
        Assert.NotNull(tree.Find(other, Items.Id("Add")));
    }
}
