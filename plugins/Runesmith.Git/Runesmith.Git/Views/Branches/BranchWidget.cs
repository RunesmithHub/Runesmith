using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Views.Branches;

/// <summary>Puts the branch widget in the main toolbar's leading slot, after the project widget.</summary>
[Export(typeof(IToolbarWidgetProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class BranchWidgetProvider(GitOperations operations, ICommandService commands) : IToolbarWidgetProvider
{
    /// <summary>Gets the widget once it is created, so the Branches command can open its popup.</summary>
    public BranchWidget? Widget { get; private set; }

    public ToolbarSlot Slot => ToolbarSlot.Leading;

    public int Order => 0;

    public Control CreateWidget()
    {
        GitIcons.Register();
        return Widget = new BranchWidget(operations, commands);
    }
}

/// <summary>The toolbar's branch widget: the current branch with arrows for the commits to pull and push, and the branch popup; hidden outside
/// a repository.</summary>
internal sealed class BranchWidget : Button
{
    private readonly RepositoryService repositories;
    private readonly TextBlock name = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock incoming = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock outgoing = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly Border badge;
    private readonly Flyout flyout;
    private readonly BranchPopup popup;

    public BranchWidget(GitOperations operations, ICommandService commands)
    {
        repositories = operations.Repositories;
        Classes.Add("toolbar");
        Padding = new Thickness(6, 0, 8, 0);
        VerticalAlignment = VerticalAlignment.Center;
        incoming[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentBrush");
        outgoing[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SuccessBrush");
        badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0),
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { incoming, outgoing } },
        };
        badge[!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceHoverBrush");

        var icon = new SymbolIcon { Data = Icons.Find(GitIcons.Branch), Size = 16, VerticalAlignment = VerticalAlignment.Center };
        var chevron = new SymbolIcon { Data = Icons.ChevronDown, Size = 12, VerticalAlignment = VerticalAlignment.Center };
        chevron[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
        name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimaryBrush");
        // A transparent background makes the whole widget take clicks, not only its text and icons.
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Background = Brushes.Transparent, Children = { icon, name, badge, chevron } };

        flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        popup = new BranchPopup(operations, commands, () => flyout.Hide());
        flyout.Content = popup;
        flyout.Opening += (_, _) => _ = popup.LoadAsync();
        Flyout = flyout;

        repositories.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>Opens the branch popup, if the widget shows.</summary>
    public bool OpenPopup()
    {
        if (!IsVisible || !IsEffectivelyVisible)
            return false;
        flyout.ShowAt(this);
        return true;
    }

    private void Update()
    {
        var info = repositories.Current;
        IsVisible = info is not null;
        if (info is null)
            return;

        name.Text = info.Branch ?? (info.Head is { } head ? head[..Math.Min(8, head.Length)] : "HEAD");
        incoming.Text = info.Behind > 0 ? $"↓{info.Behind}" : "";
        outgoing.Text = info.Ahead > 0 ? $"↑{info.Ahead}" : "";
        incoming.IsVisible = info.Behind > 0;
        outgoing.IsVisible = info.Ahead > 0;
        badge.IsVisible = info.Ahead > 0 || info.Behind > 0;
        ToolTip.SetTip(this, Describe(info));
    }

    /// <summary>Describes a repository's branch and sync state for the widget's and the status bar's tooltips.</summary>
    internal static string Describe(RepositoryInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Branch is null)
            return info.Head is { } head ? $"HEAD is detached at {head[..Math.Min(8, head.Length)]}" : "HEAD is detached";

        var lines = new List<string> { $"Branch {info.Branch}" };
        if (info.Upstream is null)
            lines.Add("Not tracking a remote branch; pushing sets one");
        else if (info.Ahead == 0 && info.Behind == 0)
            lines.Add($"Up to date with {info.Upstream}");
        else
        {
            lines.Add($"Tracking {info.Upstream}");
            if (info.Behind > 0)
                lines.Add($"{GitOperations.Commits(info.Behind)} to pull");
            if (info.Ahead > 0)
                lines.Add($"{GitOperations.Commits(info.Ahead)} to push");
        }

        return string.Join('\n', lines);
    }
}
