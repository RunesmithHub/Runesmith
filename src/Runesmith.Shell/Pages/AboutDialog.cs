using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Composition;
using Runesmith.Sdk;
using Runesmith.Sdk.Plugins;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Pages;

/// <summary>The About dialog: the version, what it runs on, the plugins and where Runesmith keeps its files.</summary>
internal static class AboutDialog
{
    public static Dialog Create(RuntimeInfo runtime)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0";
        var plugins = runtime.Plugins.Count(p => p.State == PluginState.Loaded);
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*"), Margin = new Thickness(0, 12, 0, 0) };
        (string, string)[] rows =
        [
            ("Version", version),
            ("Plugin API", RunesmithApi.Version.ToString()),
            (".NET", RuntimeInformation.FrameworkDescription),
            ("System", $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})"),
            ("Plugins", plugins == 1 ? "1 loaded" : $"{plugins} loaded"),
            ("Settings", RunesmithPaths.Config),
            ("Logs", RunesmithPaths.Logs),
        ];
        for (var i = 0; i < rows.Length; i++)
        {
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = rows[i].Item1, Classes = { "muted" }, Margin = new Thickness(0, 3) };
            var value = new SelectableTextBlock { Text = rows[i].Item2, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3) };
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            facts.Children.AddRange([name, value]);
        }

        var close = new Button { Content = "Close", Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 84 };
        var dialog = new Dialog
        {
            Width = 520,
            ShowCloseButton = false,
            Content = new StackPanel
            {
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 14,
                        Children =
                        {
                            new Logo(44),
                            new StackPanel
                            {
                                VerticalAlignment = VerticalAlignment.Center,
                                Children =
                                {
                                    new TextBlock { Text = "Runesmith", FontSize = 20, FontWeight = FontWeight.SemiBold },
                                    new TextBlock { Text = "A fast, extensible code editor", Classes = { "muted" } },
                                },
                            },
                        },
                    },
                    facts,
                },
            },
            Footer = close,
        };
        close.Click += (_, _) => dialog.Close();
        return dialog;
    }
}
