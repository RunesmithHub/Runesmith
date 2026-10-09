using System.Composition;
using Avalonia.Input;
using Runesmith.Sdk.Documents;

namespace Runesmith.Editor.Input;

/// <summary>The keyboard hooks plugins export, in the order they see keys: highest priority first, then the order they were loaded in.</summary>
/// <remarks>With no hooks, a key costs one length check and allocates nothing.</remarks>
[Export]
[Shared]
public sealed class EditorKeyHooks
{
    private readonly Lazy<IEditorKeyHook>[] exports;
    private IEditorKeyHook[]? all;
    private IEditorKeyHook[] active = [];
    private Func<IEditorKeyHook, bool>? isEnabled;

    [ImportingConstructor]
    public EditorKeyHooks([ImportMany] IEnumerable<Lazy<IEditorKeyHook>> hooks) => exports = [.. hooks];

    /// <summary>Creates the hooks with these instances, for tests.</summary>
    public EditorKeyHooks(IEnumerable<IEditorKeyHook> hooks)
        : this(hooks.Select(hook => new Lazy<IEditorKeyHook>(() => hook)))
    {
    }

    /// <summary>Gets the hooks that see keys now, in order.</summary>
    public IReadOnlyList<IEditorKeyHook> Active
    {
        get
        {
            EnsureCreated();
            return active;
        }
    }

    /// <summary>Gets or sets what decides whether a hook sees keys, such as the user turning a plugin's hooks off; null lets every hook.</summary>
    public Func<IEditorKeyHook, bool>? IsEnabled
    {
        get => isEnabled;
        set
        {
            isEnabled = value;
            if (all is not null)
                active = Filter(all);
        }
    }

    /// <summary>Raised when a hook throws; the hook is skipped for that key.</summary>
    public event EventHandler<KeyHookFailedEventArgs>? HookFailed;

    /// <summary>Lets the hooks see a key press; returns whether one handled it.</summary>
    public bool KeyDown(IEditorView editor, Key key, KeyModifiers modifiers)
    {
        var hooks = all is null ? Created() : active;
        if (hooks.Length == 0)
            return false;

        var press = new EditorKeyPress(key, modifiers);
        foreach (var hook in hooks)
        {
            try
            {
                if (hook.OnKeyDown(editor, press))
                    return true;
            }
            catch (Exception exception)
            {
                HookFailed?.Invoke(this, new KeyHookFailedEventArgs(hook, exception));
            }
        }

        return false;
    }

    /// <summary>Lets the hooks see typed text; returns whether one handled it.</summary>
    public bool TextInput(IEditorView editor, string text)
    {
        var hooks = all is null ? Created() : active;
        if (hooks.Length == 0)
            return false;

        foreach (var hook in hooks)
        {
            try
            {
                if (hook.OnTextInput(editor, text))
                    return true;
            }
            catch (Exception exception)
            {
                HookFailed?.Invoke(this, new KeyHookFailedEventArgs(hook, exception));
            }
        }

        return false;
    }

    private IEditorKeyHook[] Created()
    {
        EnsureCreated();
        return active;
    }

    private void EnsureCreated()
    {
        if (all is not null)
            return;

        var created = new List<IEditorKeyHook>(exports.Length);
        foreach (var export in exports)
        {
            try
            {
                created.Add(export.Value);
            }
            catch (Exception exception)
            {
                HookFailed?.Invoke(this, new KeyHookFailedEventArgs(null, exception));
            }
        }

        all = [.. created.Select((hook, index) => (hook, index)).OrderByDescending(e => Priority(e.hook)).ThenBy(e => e.index).Select(e => e.hook)];
        active = Filter(all);
    }

    private IEditorKeyHook[] Filter(IEditorKeyHook[] hooks) => isEnabled is { } enabled ? [.. hooks.Where(enabled)] : hooks;

    private int Priority(IEditorKeyHook hook)
    {
        try
        {
            return hook.Priority;
        }
        catch (Exception exception)
        {
            HookFailed?.Invoke(this, new KeyHookFailedEventArgs(hook, exception));
            return 0;
        }
    }
}

/// <summary>A keyboard hook that threw, or null when one could not be created.</summary>
public sealed class KeyHookFailedEventArgs(IEditorKeyHook? hook, Exception exception) : EventArgs
{
    public IEditorKeyHook? Hook { get; } = hook;

    public Exception Exception { get; } = exception;
}
