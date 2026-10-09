using Runesmith.Sdk.Options;

namespace Runesmith.Shell.Forms;

/// <summary>The state of a form apart from its controls: the values, which options show and can be changed, and the problems to show.</summary>
/// <remarks>A required option that is still empty counts against <see cref="IsValid"/> at once, but its message shows only after the user
/// changed the option or <see cref="RevealProblems"/> was called, so a new form does not open full of errors.</remarks>
public sealed class OptionFormModel
{
    private readonly HashSet<string> touched = new(StringComparer.Ordinal);
    private Dictionary<string, string> problems = new(StringComparer.Ordinal);
    private bool revealAll;

    /// <summary>Creates the state of a form over some values, which it changes in place.</summary>
    public OptionFormModel(OptionSet options, OptionValues values, Func<OptionValues, IReadOnlyDictionary<string, string>>? validate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(values);
        Options = options;
        Values = values;
        ExtraValidation = validate;
        Revalidate();
    }

    /// <summary>Gets the options, in the order they show.</summary>
    public OptionSet Options { get; }

    /// <summary>Gets the values the form edits.</summary>
    public OptionValues Values { get; }

    /// <summary>Gets checks the option set cannot express, such as whether a folder exists already; they return problems by option id.</summary>
    public Func<OptionValues, IReadOnlyDictionary<string, string>>? ExtraValidation { get; }

    /// <summary>Gets whether the values can be used: no visible, enabled option has a problem.</summary>
    public bool IsValid => problems.Count == 0;

    /// <summary>Gets every problem, by option id, whether it shows yet or not.</summary>
    public IReadOnlyDictionary<string, string> Problems => problems;

    /// <summary>Raised after a value changes, with the option's id.</summary>
    public event EventHandler<string>? ValueChanged;

    /// <summary>Raised when <see cref="IsValid"/> changes.</summary>
    public event EventHandler? ValidityChanged;

    /// <summary>Whether an option shows now.</summary>
    public bool IsVisible(Option option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.VisibleWhen?.IsMet(Values) ?? true;
    }

    /// <summary>Whether an option can be changed now.</summary>
    public bool IsEnabled(Option option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.EnabledWhen?.IsMet(Values) ?? true;
    }

    /// <summary>Gets the message to show under an option's field, or null.</summary>
    public string? GetShownProblem(string id) =>
        problems.TryGetValue(id, out var problem) && (revealAll || touched.Contains(id) || !string.IsNullOrWhiteSpace(Values.Get(id))) ? problem : null;

    /// <summary>Sets a value; <paramref name="byUser"/> says whether the user typed or picked it, which lets its problem show.</summary>
    public void Set(string id, string? value, bool byUser = true)
    {
        if (byUser)
            touched.Add(id);
        if (Values.Get(id) == value)
            return;

        Values.Set(id, value);
        Revalidate();
        ValueChanged?.Invoke(this, id);
    }

    /// <summary>Shows every problem, such as when the user tries to submit the form.</summary>
    public void RevealProblems()
    {
        revealAll = true;
        Revalidate();
    }

    /// <summary>Checks the values again, such as after something outside them changed that <see cref="ExtraValidation"/> reads.</summary>
    public void Revalidate()
    {
        var wasValid = IsValid;
        var found = new Dictionary<string, string>(Options.Validate(Values), StringComparer.Ordinal);
        if (ExtraValidation?.Invoke(Values) is { } extra)
        {
            foreach (var (id, problem) in extra)
                found.TryAdd(id, problem);
        }

        // Options can share an id under different conditions; a problem belongs to whichever of them shows.
        var shown = Options.Options.Where(o => IsVisible(o) && IsEnabled(o)).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var option in Options.Options)
        {
            if (!shown.Contains(option.Id))
                found.Remove(option.Id);
        }

        problems = found;
        if (wasValid != IsValid)
            ValidityChanged?.Invoke(this, EventArgs.Empty);
    }
}
