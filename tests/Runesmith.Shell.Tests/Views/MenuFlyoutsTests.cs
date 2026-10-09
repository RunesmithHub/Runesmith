using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using HammerUI.Theming;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Tests.Views;

public sealed class MenuFlyoutsTests
{
    [Fact]
    public Task ADynamicMenuShowsTheItemsItsFillAddsEachTimeItOpens() => HeadlessSession.Value.Dispatch(() =>
    {
        var button = new Button { Content = "Menu" };
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        var opened = 0;
        var menu = MenuFlyouts.Dynamic(flyout =>
        {
            opened++;
            for (var i = 0; i < opened; i++)
                flyout.Items.Add(new MenuItem { Header = $"Item {i}" });
        });

        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, Presenter(menu).ItemCount);

        menu.Hide();
        menu.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, Presenter(menu).ItemCount);
    }, CancellationToken.None);

    private static MenuFlyoutPresenter Presenter(MenuFlyout menu)
    {
        var popup = (Popup)typeof(PopupFlyoutBase).GetProperty("Popup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
        return (MenuFlyoutPresenter)popup.Child!;
    }
}
