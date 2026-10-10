using System.Globalization;
using System.Text;

namespace Runesmith.Sdk.Documents;

/// <summary>Builds a snippet for <see cref="IEditorView.InsertSnippet(SnippetString)"/>: text with tab stops the user moves between with Tab
/// and Shift+Tab, placeholders, choices and variables.</summary>
/// <remarks>
/// <para>The syntax is the common snippet syntax: <c>$1</c> and <c>${1}</c> are tab stops, <c>${1:name}</c> a placeholder whose text is selected,
/// <c>${1|a,b|}</c> a choice, <c>$0</c> the final position, <c>$TM_FILENAME</c> or <c>${TM_FILENAME:default}</c> a variable. A tab stop used
/// more than once is linked: typing in one changes the others. The <c>Append</c> methods escape their text, so <c>$</c>, <c>}</c> and
/// <c>\</c> in it are inserted as they are.</para>
/// <para>Added in plugin API 0.1.2.</para>
/// </remarks>
public sealed class SnippetString
{
    private readonly StringBuilder value = new();

    /// <summary>Starts an empty snippet.</summary>
    public SnippetString()
    {
    }

    /// <summary>Starts with snippet syntax, which is not escaped.</summary>
    public SnippetString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        this.value.Append(value);
    }

    /// <summary>Gets the snippet in snippet syntax.</summary>
    public string Value => value.ToString();

    /// <summary>Gets or sets the number the next tab stop, placeholder or choice gets when it is not given one; it starts at 1.</summary>
    public int NextTabStop { get; set; } = 1;

    /// <summary>Adds text, inserted as it is.</summary>
    public SnippetString AppendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        value.Append(Escape(text));
        return this;
    }

    /// <summary>Adds a tab stop, such as <c>$1</c>.</summary>
    /// <param name="number">The tab stop's number, or null for <see cref="NextTabStop"/>; tab stops with the same number are linked.</param>
    public SnippetString AppendTabStop(int? number = null)
    {
        value.Append('$').Append(Number(number));
        return this;
    }

    /// <summary>Adds a placeholder, such as <c>${1:name}</c>: a tab stop whose text is selected when the caret comes to it.</summary>
    public SnippetString AppendPlaceholder(string text, int? number = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        value.Append("${").Append(Number(number)).Append(':').Append(Escape(text)).Append('}');
        return this;
    }

    /// <summary>Adds a placeholder whose content is built by <paramref name="nested"/>, such as one with a tab stop inside it.</summary>
    public SnippetString AppendPlaceholder(Action<SnippetString> nested, int? number = null)
    {
        ArgumentNullException.ThrowIfNull(nested);
        var index = Number(number);
        var inner = new SnippetString { NextTabStop = NextTabStop };
        nested(inner);
        NextTabStop = Math.Max(NextTabStop, inner.NextTabStop);
        value.Append("${").Append(index).Append(':').Append(inner.Value).Append('}');
        return this;
    }

    /// <summary>Adds a choice, such as <c>${1|public,private|}</c>: a tab stop that offers the choices in a list, the first inserted.</summary>
    /// <exception cref="ArgumentException">There are no choices.</exception>
    public SnippetString AppendChoice(IReadOnlyList<string> choices, int? number = null)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
            throw new ArgumentException("A choice needs at least one option.", nameof(choices));

        value.Append("${").Append(Number(number)).Append('|')
            .AppendJoin(',', choices.Select(c => c.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("|", "\\|", StringComparison.Ordinal)))
            .Append("|}");
        return this;
    }

    /// <summary>Adds a variable, such as <c>TM_FILENAME</c>, <c>SELECTED_TEXT</c> or <c>CLIPBOARD</c>, with the text used when it is empty.</summary>
    public SnippetString AppendVariable(string name, string? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (defaultValue is null)
            value.Append("${").Append(name).Append('}');
        else
            value.Append("${").Append(name).Append(':').Append(Escape(defaultValue)).Append('}');
        return this;
    }

    /// <summary>Adds the final position, <c>$0</c>, where the caret ends after the last tab stop.</summary>
    public SnippetString AppendFinalTabStop()
    {
        value.Append("$0");
        return this;
    }

    /// <summary>Gets the snippet in snippet syntax.</summary>
    public override string ToString() => Value;

    /// <summary>Escapes text so a snippet inserts it as it is.</summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("}", "\\}", StringComparison.Ordinal);
    }

    private string Number(int? number)
    {
        if (number is { } given)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(given, nameof(number));
            NextTabStop = Math.Max(NextTabStop, given + 1);
            return given.ToString(CultureInfo.InvariantCulture);
        }

        return (NextTabStop++).ToString(CultureInfo.InvariantCulture);
    }
}
