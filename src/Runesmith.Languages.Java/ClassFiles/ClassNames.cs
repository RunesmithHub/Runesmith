namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>Converts between the forms of Java class names: internal (<c>java/util/Map$Entry</c>), binary (<c>java.util.Map$Entry</c>)
/// and source (<c>java.util.Map.Entry</c>).</summary>
/// <remarks>A <c>$</c> in a binary name is taken as a nesting separator, which holds for the class libraries this reads; class files that
/// use <c>$</c> in their own names are rare.</remarks>
public static class ClassNames
{
    /// <summary>Turns an internal name into a binary name.</summary>
    public static string FromInternal(string internalName) => internalName.Replace('/', '.');

    /// <summary>Turns a binary name into an internal name.</summary>
    public static string ToInternal(string binaryName) => binaryName.Replace('.', '/');

    /// <summary>Gets the package of a binary name, or an empty text for the unnamed package.</summary>
    public static string PackageName(string binaryName)
    {
        var dot = binaryName.LastIndexOf('.');
        return dot < 0 ? "" : binaryName[..dot];
    }

    /// <summary>Gets the name a type is declared with, such as <c>Entry</c> for <c>java.util.Map$Entry</c>.</summary>
    public static string SimpleName(string binaryName)
    {
        var start = Math.Max(binaryName.LastIndexOf('.'), binaryName.LastIndexOf('$')) + 1;
        return binaryName[start..];
    }

    /// <summary>Gets the name a type has inside its package, such as <c>Map.Entry</c>.</summary>
    public static string NestedName(string binaryName) => binaryName[(binaryName.LastIndexOf('.') + 1)..].Replace('$', '.');

    /// <summary>Gets the name source code uses, such as <c>java.util.Map.Entry</c>.</summary>
    public static string SourceName(string binaryName) => binaryName.Replace('$', '.');

    /// <summary>Gets the binary name of the class a nested class is declared in, or null for a top-level class.</summary>
    public static string? OuterName(string binaryName)
    {
        var dollar = binaryName.LastIndexOf('$');
        return dollar <= binaryName.LastIndexOf('.') ? null : binaryName[..dollar];
    }

    /// <summary>Whether a binary name is an anonymous or local class, such as <c>Outer$1</c> or <c>Outer$1Local</c>, which no code can name.</summary>
    public static bool IsAnonymousOrLocal(string binaryName)
    {
        var start = binaryName.LastIndexOf('.') + 1;
        for (var i = binaryName.IndexOf('$', start); i >= 0 && i + 1 < binaryName.Length; i = binaryName.IndexOf('$', i + 1))
        {
            if (char.IsAsciiDigit(binaryName[i + 1]))
                return true;
        }

        return binaryName.EndsWith('$');
    }
}
