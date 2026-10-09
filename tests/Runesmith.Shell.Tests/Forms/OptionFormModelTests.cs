using Runesmith.Sdk.Options;
using Runesmith.Shell.Forms;

namespace Runesmith.Shell.Tests.Forms;

public sealed class OptionFormModelTests
{
    private static readonly OptionSet Build = new(
    [
        new Option("system", "Build system", OptionKind.Choice)
        {
            Default = "maven",
            Choices = [new("maven", "Maven"), new("gradle", "Gradle"), new("none", "None")],
        },
        new Option("dsl", "Gradle DSL", OptionKind.Choice)
        {
            Default = "kotlin",
            Choices = [new("kotlin", "Kotlin"), new("groovy", "Groovy")],
            VisibleWhen = new OptionCondition("system", ["gradle"]),
        },
        new Option("group", "Group id", OptionKind.Text) { IsRequired = true, Pattern = "^[a-z][a-z0-9.]*$", PatternMessage = "Use lower-case words." },
        new Option("tests", "JUnit tests", OptionKind.Toggle) { Default = "true", EnabledWhen = new OptionCondition("system", ["maven", "gradle"]) },
        new Option("port", "Port", OptionKind.Number) { Minimum = 1, Maximum = 65535, EnabledWhen = new OptionCondition("tests", ["true"]) },
    ]);

    [Fact]
    public void ConditionsFollowTheValuesTheyDependOn()
    {
        var model = new OptionFormModel(Build, Build.Defaults());
        var dsl = Build.Options[1];
        var tests = Build.Options[3];

        Assert.False(model.IsVisible(dsl));
        Assert.True(model.IsEnabled(tests));

        model.Set("system", "gradle");
        Assert.True(model.IsVisible(dsl));

        model.Set("system", "none");
        Assert.False(model.IsVisible(dsl));
        Assert.False(model.IsEnabled(tests));
    }

    [Fact]
    public void AProblemOfAVisibleOptionStaysWhenAHiddenOptionSharesItsId()
    {
        var options = new OptionSet(
        [
            new Option("project", "Project", OptionKind.Choice) { Default = "a", Choices = [new("a", "A"), new("b", "B")] },
            new Option("framework", "Target framework", OptionKind.Choice)
            {
                Choices = [new("net10.0", "net10.0")],
                VisibleWhen = new OptionCondition("project", ["a"]),
            },
            new Option("framework", "Target framework", OptionKind.Choice)
            {
                Choices = [new("net8.0", "net8.0")],
                VisibleWhen = new OptionCondition("project", ["b"]),
            },
        ]);
        var values = options.Defaults();
        values.Set("framework", "net8.0");
        var model = new OptionFormModel(options, values);

        Assert.False(model.IsValid);
        Assert.Equal("Target framework must be one of the listed values.", model.GetShownProblem("framework"));

        model.Set("project", "b");
        Assert.True(model.IsValid);
    }

    [Fact]
    public void ARequiredEmptyOptionMakesTheFormInvalidButShowsOnlyOnceTouchedOrRevealed()
    {
        var model = new OptionFormModel(Build, Build.Defaults());

        Assert.False(model.IsValid);
        Assert.Equal("Group id is required.", model.Problems["group"]);
        Assert.Null(model.GetShownProblem("group"));

        model.RevealProblems();
        Assert.Equal("Group id is required.", model.GetShownProblem("group"));
    }

    [Fact]
    public void AValueThatBreaksThePatternShowsItsMessageAtOnce()
    {
        var model = new OptionFormModel(Build, Build.Defaults());
        model.Set("group", "Com.Example", byUser: false);

        Assert.Equal("Use lower-case words.", model.GetShownProblem("group"));

        model.Set("group", "com.example");
        Assert.True(model.IsValid);
        Assert.Null(model.GetShownProblem("group"));
    }

    [Fact]
    public void ValidityChangesRaiseAnEventAndValuesRaiseTheirId()
    {
        var model = new OptionFormModel(Build, Build.Defaults());
        var validity = 0;
        var changed = new List<string>();
        model.ValidityChanged += (_, _) => validity++;
        model.ValueChanged += (_, id) => changed.Add(id);

        model.Set("group", "com.example");
        model.Set("group", "com.example");
        model.Set("group", "");

        Assert.Equal(2, validity);
        Assert.Equal(["group", "group"], changed);
    }

    [Fact]
    public void ProblemsOfHiddenAndDisabledOptionsDoNotCount()
    {
        var values = Build.Defaults();
        values.Set("group", "com.example");
        values.Set("port", "0");
        var model = new OptionFormModel(Build, values);
        Assert.Contains("port", model.Problems.Keys);

        model.Set("tests", "false");
        Assert.True(model.IsValid);
    }

    [Fact]
    public void ExtraValidationAddsProblemsTheOptionSetCannotExpress()
    {
        var values = Build.Defaults();
        values.Set("group", "com.example");
        var model = new OptionFormModel(Build, values, v => v.Get("group") == "com.example"
            ? new Dictionary<string, string> { ["group"] = "Taken." }
            : new Dictionary<string, string>());

        Assert.Equal("Taken.", model.GetShownProblem("group"));
        model.Set("group", "org.example");
        Assert.True(model.IsValid);
    }
}
