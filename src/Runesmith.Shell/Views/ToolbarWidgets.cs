using System.Composition;
using Avalonia.Controls;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Views;

/// <summary>Creates the widgets plugins put in the main toolbar, in their slots' order.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ToolbarWidgets([ImportMany] IEnumerable<Lazy<IToolbarWidgetProvider>> providers, IOutputService output)
{
    /// <summary>Creates every widget once, grouped by slot and ordered by <see cref="IToolbarWidgetProvider.Order"/>; a provider that fails is
    /// written to the Plugins output and left out.</summary>
    public ILookup<ToolbarSlot, Control> Create()
    {
        var widgets = new List<(ToolbarSlot Slot, int Order, Control Widget)>();
        foreach (var provider in providers)
        {
            try
            {
                var value = provider.Value;
                widgets.Add((value.Slot, value.Order, value.CreateWidget()));
            }
            catch (Exception exception)
            {
                output.GetChannel("Plugins").AppendLine($"A toolbar widget could not be created: {exception}");
            }
        }

        return widgets.OrderBy(w => w.Order).ToLookup(w => w.Slot, w => w.Widget);
    }
}
