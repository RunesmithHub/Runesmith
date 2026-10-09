using System.Windows.Input;

namespace Runesmith.Git.Commands;

/// <summary>A command that always runs an action, for controls such as <c>EmptyState</c> that take an <see cref="ICommand"/>.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
