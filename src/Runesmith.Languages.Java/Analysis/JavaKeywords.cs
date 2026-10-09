using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Where a keyword can be written.</summary>
internal enum KeywordPlace
{
    /// <summary>At the top of a file, before or between type declarations.</summary>
    TopLevel,

    /// <summary>At the start of a member of a class body.</summary>
    Member,

    /// <summary>At the start of a statement.</summary>
    Statement,

    /// <summary>Inside an expression.</summary>
    Expression,

    /// <summary>Where only a type can be written.</summary>
    Type,

    /// <summary>After a type declaration's name, before its body.</summary>
    TypeHeader,
}

/// <summary>The keywords completion offers at a place, for a Java version: contextual keywords only where and since when they mean something.</summary>
internal static class JavaKeywords
{
    private static readonly string[] Primitives = ["boolean", "byte", "char", "short", "int", "long", "float", "double"];
    private static readonly string[] MemberModifiers = ["public", "protected", "private", "static", "final", "abstract", "synchronized", "native", "transient",
        "volatile", "strictfp"];
    private static readonly string[] Literals = ["true", "false", "null", "this", "super", "new"];
    private static readonly string[] Statements = ["if", "else", "for", "while", "do", "switch", "return", "break", "continue", "throw", "try", "catch",
        "finally", "synchronized", "assert", "final", "class"];

    /// <summary>Gets the keywords valid at a place.</summary>
    /// <param name="inInterface">Whether the place is in an interface's body, where <c>default</c> methods are.</param>
    /// <param name="inSwitchExpression">Whether the place is in a switch expression's case, where <c>yield</c> is.</param>
    /// <param name="isCompactFile">Whether the file declares methods outside a class, so top-level places are also member places.</param>
    public static IEnumerable<string> At(KeywordPlace place, JavaVersion version, bool inInterface = false, bool inSwitchExpression = false,
        bool isCompactFile = false)
    {
        var records = JavaFeatures.IsAvailable(JavaFeature.Records, version);
        var sealedClasses = JavaFeatures.IsAvailable(JavaFeature.SealedClasses, version);
        switch (place)
        {
            case KeywordPlace.TopLevel:
                foreach (var keyword in TypeDeclarationKeywords(records, sealedClasses))
                    yield return keyword;
                yield return "package";
                yield return "import";
                yield return "public";
                if (isCompactFile || JavaFeatures.IsAvailable(JavaFeature.CompactSourceFiles, version))
                {
                    yield return "void";
                    foreach (var primitive in Primitives)
                        yield return primitive;
                }

                break;
            case KeywordPlace.Member:
                foreach (var keyword in MemberModifiers)
                    yield return keyword;
                foreach (var keyword in TypeDeclarationKeywords(records, sealedClasses))
                    yield return keyword;
                yield return "void";
                foreach (var primitive in Primitives)
                    yield return primitive;
                if (inInterface)
                    yield return "default";
                break;
            case KeywordPlace.Statement:
                foreach (var keyword in Statements.Concat(Literals).Concat(Primitives))
                    yield return keyword;
                if (JavaFeatures.IsAvailable(JavaFeature.LocalVariableTypeInference, version))
                    yield return "var";
                if (records)
                {
                    yield return "record";
                    yield return "interface";
                    yield return "enum";
                }

                if (inSwitchExpression && JavaFeatures.IsAvailable(JavaFeature.SwitchExpressions, version))
                    yield return "yield";
                break;
            case KeywordPlace.Expression:
                foreach (var keyword in Literals)
                    yield return keyword;
                yield return "switch";
                break;
            case KeywordPlace.Type:
                foreach (var primitive in Primitives)
                    yield return primitive;
                break;
            case KeywordPlace.TypeHeader:
                yield return "extends";
                yield return "implements";
                if (sealedClasses)
                    yield return "permits";
                break;
        }
    }

    private static IEnumerable<string> TypeDeclarationKeywords(bool records, bool sealedClasses)
    {
        yield return "class";
        yield return "interface";
        yield return "enum";
        yield return "abstract";
        yield return "final";
        if (records)
            yield return "record";
        if (sealedClasses)
        {
            yield return "sealed";
            yield return "non-sealed";
        }
    }
}
