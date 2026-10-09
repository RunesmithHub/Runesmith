using Runesmith.Sdk.Options;

namespace Runesmith.Sdk.Tests.Options;

public sealed class OptionSetTests
{
    private static readonly OptionSet Set = new(
    [
        new Option("name", "Name", OptionKind.Text) { IsRequired = true, Pattern = "^[A-Za-z][A-Za-z0-9.]*$", PatternMessage = "Use letters." },
        new Option("framework", "Target framework", OptionKind.Choice)
        {
            Default = "net10.0",
            Choices = [new OptionChoice("net10.0", ".NET 10"), new OptionChoice("net9.0", ".NET 9")],
        },
        new Option("port", "Port", OptionKind.Number) { Minimum = 1, Maximum = 65535, Default = "8080" },
        new Option("https", "Use HTTPS", OptionKind.Toggle) { Default = "true" },
        new Option("certificate", "Certificate", OptionKind.Path)
        {
            IsRequired = true,
            VisibleWhen = new OptionCondition("https", ["true"]),
        },
    ]);

    [Fact]
    public void DefaultsHoldTheDeclaredValues()
    {
        var values = Set.Defaults();

        Assert.Equal("net10.0", values.Get("framework"));
        Assert.Equal(8080, values.GetNumber("port"));
        Assert.True(values.GetBool("https"));
        Assert.Null(values.Get("name"));
    }

    [Fact]
    public void ValidationReportsRequiredPatternRangeAndChoiceProblems()
    {
        var values = Set.Defaults();
        values.Set("framework", "net5.0");
        values.Set("port", "70000");

        var problems = Set.Validate(values);

        Assert.Equal("Name is required.", problems["name"]);
        Assert.Equal("Target framework must be one of the listed values.", problems["framework"]);
        Assert.Equal("Port must be a number from 1 to 65535.", problems["port"]);
        Assert.Equal("Certificate is required.", problems["certificate"]);

        values.Set("name", "1st");
        Assert.Equal("Use letters.", Set.Validate(values)["name"]);
    }

    [Fact]
    public void HiddenOptionsAreNotValidated()
    {
        var values = Set.Defaults();
        values.Set("name", "App");
        values.Set("https", "false");

        Assert.Empty(Set.Validate(values));
    }

    [Fact]
    public void ListsAndPairsSplitByLine()
    {
        var values = new OptionValues();
        values.Set("environment", "A=1\n\n B = two \nFLAG");

        Assert.Equal(["A=1", "B = two", "FLAG"], values.GetList("environment"));
        Assert.Equal(
            [new KeyValuePair<string, string>("A", "1"), new KeyValuePair<string, string>("B ", " two"), new KeyValuePair<string, string>("FLAG", "")],
            values.GetPairs("environment"));
    }

    [Fact]
    public void ACopyChangesIndependently()
    {
        var values = Set.Defaults();
        var copy = values.Copy();
        copy.Set("framework", "net9.0");

        Assert.Equal("net10.0", values.Get("framework"));
    }
}
