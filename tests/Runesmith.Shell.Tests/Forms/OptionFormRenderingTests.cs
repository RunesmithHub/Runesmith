using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using HammerUI.Controls;
using HammerUI.Theming;
using Runesmith.Sdk.Options;
using Runesmith.Shell.Forms;

namespace Runesmith.Shell.Tests.Forms;

public sealed class OptionFormRenderingTests
{
    private static readonly OptionSet Options = new(
    [
        new Option("name", "Name", OptionKind.Text) { IsRequired = true, Placeholder = "MyProject", Description = "The project's name." },
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
        new Option("release", "Java release", OptionKind.Choice)
        {
            Default = "25",
            Choices = [new("8", "8"), new("11", "11"), new("17", "17"), new("21", "21"), new("25", "25")],
        },
        new Option("tests", "JUnit tests", OptionKind.Toggle) { Default = "true" },
        new Option("env", "Environment", OptionKind.List) { IsKeyValueList = true, Group = OptionGroup.Advanced, Default = "A=1\nB=2" },
        new Option("jdk", "JDK", OptionKind.Sdk) { SdkKind = "jdk", Group = OptionGroup.Advanced },
    ]);

    [Fact]
    public Task EachKindGetsItsControlAndConditionsUpdateLive() => HeadlessSession.Value.Dispatch(() =>
    {
        var values = Options.Defaults();
        var form = new OptionForm(Options, values);
        var window = new Window { Width = 700, Height = 600, Content = form };
        window.Show();

        var segmented = form.GetLogicalDescendants().OfType<SegmentedControl>().ToList();
        Assert.Equal(2, segmented.Count);
        var release = Assert.Single(form.GetLogicalDescendants().OfType<ComboBox>());
        Assert.Equal("25", ((OptionChoice)release.SelectedItem!).Value);
        Assert.True(Assert.Single(form.GetLogicalDescendants().OfType<CheckBox>()).IsChecked);
        Assert.Equal(4, form.GetLogicalDescendants().OfType<TextBox>().Count(box => box.Text is "A" or "1" or "B" or "2"));

        var dsl = segmented[1];
        Assert.False(IsShown(dsl, form));
        segmented[0].SelectedIndex = 1;
        Assert.Equal("gradle", values.Get("system"));
        Assert.True(IsShown(dsl, form));

        var name = form.GetLogicalDescendants().OfType<TextBox>().First(box => box.PlaceholderText == "MyProject");
        Assert.False(form.IsValid);
        name.Text = "Hello";
        Assert.Equal("Hello", values.Get("name"));
        Assert.True(form.IsValid);

        form.SetValue("name", "Other");
        Assert.Equal("Other", name.Text);
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task AnSdkOptionWithoutAnSdkServiceIsAFolderField() => HeadlessSession.Value.Dispatch(() =>
    {
        var values = new OptionValues();
        var form = new OptionForm(new OptionSet([new Option("jdk", "JDK", OptionKind.Sdk) { SdkKind = "jdk" }]), values);
        var window = new Window { Width = 600, Height = 200, Content = form };
        window.Show();

        var box = Assert.Single(form.GetLogicalDescendants().OfType<TextBox>());
        box.Text = "/usr/lib/jvm/25";
        Assert.Equal("/usr/lib/jvm/25", values.Get("jdk"));
        window.Close();
    }, CancellationToken.None);

    [Fact]
    public Task AProblemReplacesTheHintOnceRevealed() => HeadlessSession.Value.Dispatch(() =>
    {
        var form = new OptionForm(Options, Options.Defaults());
        var window = new Window { Width = 700, Height = 600, Content = form };
        window.Show();

        Assert.Contains(form.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "The project's name." && t.IsVisible);
        form.RevealProblems();
        Assert.Contains(form.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Name is required." && t.IsVisible);
        Assert.DoesNotContain(form.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "The project's name." && t.IsVisible);
        window.Close();
    }, CancellationToken.None);

    private static bool IsShown(Control control, Control root)
    {
        for (var current = control; current is not null && current != root; current = current.Parent as Control)
        {
            if (!current.IsVisible)
                return false;
        }

        return true;
    }
}
