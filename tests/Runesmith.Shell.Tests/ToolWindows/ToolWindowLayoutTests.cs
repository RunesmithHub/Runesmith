using System.Text.Json;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Session;
using Runesmith.Shell.ToolWindows;

namespace Runesmith.Shell.Tests.ToolWindows;

public sealed class ToolWindowLayoutTests
{
    private static readonly ToolWindowDefinition Explorer = new("explorer", "Explorer", "folder-open", DockSide.Left) { IsVisibleByDefault = true };
    private static readonly ToolWindowDefinition Search = new("search", "Search", "search", DockSide.Left) { Order = 1, IsVisibleByDefault = true };
    private static readonly ToolWindowDefinition Problems = new("problems", "Problems", "alert-triangle", DockSide.Bottom) { IsVisibleByDefault = true };
    private static readonly ToolWindowDefinition Output = new("output", "Output", "terminal", DockSide.Bottom) { Order = 1 };
    private static readonly ToolWindowDefinition Outline = new("outline", "Outline", "list-tree", DockSide.Right);

    private static readonly ToolWindowDefinition[] All = [Output, Problems, Search, Explorer, Outline];

    [Fact]
    public void StartsWithEachWindowOnItsSideInOrderAndTheFirstDefaultOneShown()
    {
        var layout = new ToolWindowLayout(All);

        Assert.Equal(["explorer", "search"], layout.WindowsIn(DockSide.Left));
        Assert.Equal(["problems", "output"], layout.WindowsIn(DockSide.Bottom));
        Assert.Equal(["outline"], layout.WindowsIn(DockSide.Right));
        Assert.Equal("explorer", layout.ShownIn(DockSide.Left));
        Assert.Equal("problems", layout.ShownIn(DockSide.Bottom));
        Assert.Null(layout.ShownIn(DockSide.Right));
    }

    [Fact]
    public void ShowsOneWindowPerAreaAndHidesTheShownOneOnToggle()
    {
        var layout = new ToolWindowLayout(All);

        layout.Toggle("search");
        Assert.Equal("search", layout.ShownIn(DockSide.Left));
        Assert.False(layout.IsShown("explorer"));

        layout.Toggle("search");
        Assert.Null(layout.ShownIn(DockSide.Left));
        Assert.Equal("problems", layout.ShownIn(DockSide.Bottom));
    }

    [Fact]
    public void MovesAWindowToAnotherAreaWhereItKeepsShowing()
    {
        var layout = new ToolWindowLayout(All);
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.Move("problems", DockSide.Right, 0);

        Assert.Equal(["problems", "outline"], layout.WindowsIn(DockSide.Right));
        Assert.Equal(["output"], layout.WindowsIn(DockSide.Bottom));
        Assert.Equal("problems", layout.ShownIn(DockSide.Right));
        Assert.Null(layout.ShownIn(DockSide.Bottom));
        Assert.Equal(DockSide.Right, layout.AreaOf("problems"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ReordersAWindowWithinItsArea()
    {
        var layout = new ToolWindowLayout(All);

        layout.Move("search", DockSide.Left, 0);

        Assert.Equal(["search", "explorer"], layout.WindowsIn(DockSide.Left));
    }

    [Fact]
    public void RestoresPlacesShownWindowsAndSizesFromTheSavedSession()
    {
        var layout = new ToolWindowLayout(All);
        layout.Move("output", DockSide.Left, 1);
        layout.Show("output");
        layout.Hide("problems");
        layout.Resize(DockSide.Left, 333);

        var json = JsonSerializer.Serialize(layout.Save(), SessionJson.Default.ToolWindowSession);
        var restored = new ToolWindowLayout(All, JsonSerializer.Deserialize(json, SessionJson.Default.ToolWindowSession));

        Assert.Equal(["explorer", "output", "search"], restored.WindowsIn(DockSide.Left));
        Assert.Equal("output", restored.ShownIn(DockSide.Left));
        Assert.Null(restored.ShownIn(DockSide.Bottom));
        Assert.Equal(333, restored.SizeOf(DockSide.Left));
        Assert.Equal(ToolWindowLayout.DefaultBottomSize, restored.SizeOf(DockSide.Bottom));
    }

    [Fact]
    public void PlacesNewWindowsOnTheirSideAndKeepsThePlaceOfWindowsThatAreGone()
    {
        var saved = new ToolWindowSession(["gone", "explorer"], [], ["problems"], "gone", null, "problems", 0, 0, 0);

        var layout = new ToolWindowLayout(All, saved);

        Assert.Equal(["explorer", "search"], layout.WindowsIn(DockSide.Left));
        Assert.Null(layout.ShownIn(DockSide.Left));
        Assert.Equal(["problems", "output"], layout.WindowsIn(DockSide.Bottom));
        Assert.Equal(["gone", "explorer", "search"], layout.Save().Left);
        Assert.Equal(ToolWindowLayout.DefaultSideSize, layout.SizeOf(DockSide.Left));
    }

    [Fact]
    public void AnUnavailableWindowLeavesItsStripeAndAreaAndComesBackWhereItWas()
    {
        var layout = new ToolWindowLayout(All);
        layout.Show("search");
        var changes = 0;
        layout.Changed += (_, _) => changes++;

        layout.SetAvailable("search", available: false);
        layout.SetAvailable("search", available: false);

        Assert.Equal(1, changes);
        Assert.Equal(["explorer"], layout.WindowsIn(DockSide.Left));
        Assert.Null(layout.ShownIn(DockSide.Left));
        Assert.False(layout.IsShown("search"));
        layout.Show("search");
        Assert.Null(layout.ShownIn(DockSide.Left));
        Assert.Equal("search", layout.Save().ShownLeft);

        layout.SetAvailable("search", available: true);

        Assert.Equal(["explorer", "search"], layout.WindowsIn(DockSide.Left));
        Assert.Equal("search", layout.ShownIn(DockSide.Left));
    }
}
