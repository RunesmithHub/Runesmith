using Avalonia.Input;
using Runesmith.Editor.Input;
using Runesmith.Sdk.Documents;

namespace Runesmith.Editor.Tests;

public sealed class KeyHookTests
{
    [Fact]
    public void HigherPriorityRunsFirstAndTiesKeepTheirLoadOrder()
    {
        var seen = new List<string>();
        var hooks = new EditorKeyHooks(new IEditorKeyHook[]
        {
            new Hook("first", 0, seen),
            new Hook("urgent", 10, seen),
            new Hook("second", 0, seen),
        });

        Assert.False(hooks.KeyDown(null!, Key.A, KeyModifiers.None));

        Assert.Equal(["urgent", "first", "second"], seen);
    }

    [Fact]
    public void AHandledKeyGoesNoFurther()
    {
        var seen = new List<string>();
        var hooks = new EditorKeyHooks(new IEditorKeyHook[] { new Hook("modal", 5, seen) { Handles = Key.J }, new Hook("later", 0, seen) });

        Assert.True(hooks.KeyDown(null!, Key.J, KeyModifiers.None));
        Assert.Equal(["modal"], seen);

        Assert.False(hooks.KeyDown(null!, Key.K, KeyModifiers.None));
        Assert.Equal(["modal", "modal", "later"], seen);
    }

    [Fact]
    public void AHookThatThrowsIsSkippedAndReported()
    {
        var seen = new List<string>();
        var hooks = new EditorKeyHooks(new IEditorKeyHook[] { new Hook("broken", 1, seen) { Throws = true }, new Hook("fine", 0, seen) { Handles = Key.A } });
        var failures = new List<KeyHookFailedEventArgs>();
        hooks.HookFailed += (_, e) => failures.Add(e);

        Assert.True(hooks.KeyDown(null!, Key.A, KeyModifiers.None));

        Assert.Equal(["broken", "fine"], seen);
        Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(failures[0].Exception);
    }

    [Fact]
    public void TurnedOffHooksSeeNothing()
    {
        var seen = new List<string>();
        var off = new Hook("off", 1, seen) { Handles = Key.A, TakesText = true };
        var hooks = new EditorKeyHooks(new IEditorKeyHook[] { off, new Hook("on", 0, seen) });
        hooks.IsEnabled = hook => !ReferenceEquals(hook, off);

        Assert.False(hooks.KeyDown(null!, Key.A, KeyModifiers.None));
        Assert.False(hooks.TextInput(null!, "a"));

        Assert.Equal(["on", "on:a"], seen);
        hooks.IsEnabled = null;
        Assert.True(hooks.TextInput(null!, "b"));
    }

    [Fact]
    public void WithoutHooksNothingIsHandled()
    {
        var hooks = new EditorKeyHooks(Array.Empty<IEditorKeyHook>());

        Assert.False(hooks.KeyDown(null!, Key.A, KeyModifiers.Control));
        Assert.False(hooks.TextInput(null!, "a"));
        Assert.Empty(hooks.Active);
    }

    private sealed class Hook(string name, int priority, List<string> seen) : IEditorKeyHook
    {
        public Key? Handles { get; init; }

        public bool Throws { get; init; }

        public bool TakesText { get; init; }

        public int Priority => priority;

        public bool OnKeyDown(IEditorView editor, EditorKeyPress key)
        {
            seen.Add(name);
            if (Throws)
                throw new InvalidOperationException("broken hook");
            return key.Key == Handles;
        }

        public bool OnTextInput(IEditorView editor, string text)
        {
            seen.Add($"{name}:{text}");
            return TakesText;
        }
    }
}
