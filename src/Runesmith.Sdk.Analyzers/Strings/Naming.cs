using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace Runesmith.Sdk.Analyzers.Strings;

/// <summary>Turns Fluent ids into C# names: <c>run-exited</c> into <c>RunExited</c>, <c>$file-name</c> into <c>fileName</c>.</summary>
internal static class Naming
{
    /// <summary>Gets the member name of a message or attribute id.</summary>
    public static string Member(string id)
    {
        var name = new StringBuilder(id.Length);
        var upper = true;
        foreach (var character in id)
        {
            if (character is '-' or '_' or '.')
            {
                upper = true;
                continue;
            }

            name.Append(upper ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        if (name.Length == 0 || !SyntaxFacts.IsIdentifierStartCharacter(name[0]))
            name.Insert(0, "Message");
        return name.ToString();
    }

    /// <summary>Gets the parameter name of a variable, escaped when it is a C# keyword.</summary>
    public static string Parameter(string variable)
    {
        var member = Member(variable);
        var name = char.ToLowerInvariant(member[0]) + member.Substring(1);
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
    }
}
