namespace Runesmith.Languages.Java.Syntax;

/// <summary>A language feature that appeared in a Java release.</summary>
public enum JavaFeature
{
    Lambdas,
    MethodReferences,
    DefaultMethods,
    Modules,
    LocalVariableTypeInference,
    VarInLambdaParameters,
    SwitchExpressions,
    TextBlocks,
    Records,
    InstanceofPatterns,
    SealedClasses,
    SwitchPatterns,
    RecordPatterns,
    UnnamedVariables,
    MarkdownDocComments,
    ModuleImports,
    CompactSourceFiles,
    FlexibleConstructorBodies,
    PrimitiveTypesInPatterns,
}

/// <summary>Whether a feature can be used with a version.</summary>
public enum JavaFeatureSupport
{
    Supported,

    /// <summary>The feature is a preview feature of the release, and preview features are not enabled.</summary>
    NeedsPreview,

    /// <summary>The release is older than the feature.</summary>
    NeedsNewerRelease,
}

/// <summary>When each feature arrived: the releases it was a preview feature in, and the release it became final in.</summary>
public static class JavaFeatures
{
    private static readonly Dictionary<JavaFeature, (string Name, int? Final, int PreviewFrom, int PreviewTo)> Table = new()
    {
        [JavaFeature.Lambdas] = ("Lambda expressions", 8, 0, 0),
        [JavaFeature.MethodReferences] = ("Method references", 8, 0, 0),
        [JavaFeature.DefaultMethods] = ("Default methods", 8, 0, 0),
        [JavaFeature.Modules] = ("Module declarations", 9, 0, 0),
        [JavaFeature.LocalVariableTypeInference] = ("Local variables declared with var", 10, 0, 0),
        [JavaFeature.VarInLambdaParameters] = ("Lambda parameters declared with var", 11, 0, 0),
        [JavaFeature.SwitchExpressions] = ("Switch expressions, arrow cases and cases with several labels", 14, 12, 13),
        [JavaFeature.TextBlocks] = ("Text blocks", 15, 13, 14),
        [JavaFeature.Records] = ("Records", 16, 14, 15),
        [JavaFeature.InstanceofPatterns] = ("Patterns in instanceof", 16, 14, 15),
        [JavaFeature.SealedClasses] = ("Sealed classes", 17, 15, 16),
        [JavaFeature.SwitchPatterns] = ("Patterns and null in switch", 21, 17, 20),
        [JavaFeature.RecordPatterns] = ("Record patterns", 21, 19, 20),
        [JavaFeature.UnnamedVariables] = ("Unnamed variables and patterns", 22, 21, 21),
        [JavaFeature.MarkdownDocComments] = ("Markdown documentation comments", 23, 0, 0),
        [JavaFeature.ModuleImports] = ("Module import declarations", 25, 23, 24),
        [JavaFeature.CompactSourceFiles] = ("Compact source files", 25, 21, 24),
        [JavaFeature.FlexibleConstructorBodies] = ("Statements before this() or super()", 25, 22, 24),
        [JavaFeature.PrimitiveTypesInPatterns] = ("Primitive types in patterns", null, 23, 25),
    };

    /// <summary>Gets the feature's name as messages use it, such as "Records".</summary>
    public static string DisplayName(JavaFeature feature) => Table[feature].Name;

    /// <summary>Gets the release the feature became final in, or null for a feature that is still a preview feature.</summary>
    public static int? FinalRelease(JavaFeature feature) => Table[feature].Final;

    /// <summary>Gets the first and last release the feature was a preview feature in, or null when it never was.</summary>
    public static (int First, int Last)? PreviewReleases(JavaFeature feature) =>
        Table[feature] is { PreviewFrom: > 0 } entry ? (entry.PreviewFrom, entry.PreviewTo) : null;

    /// <summary>Gets whether a version can use a feature.</summary>
    public static JavaFeatureSupport GetSupport(JavaFeature feature, JavaVersion version)
    {
        var entry = Table[feature];
        if (entry.Final is { } final && version.Release >= final)
            return JavaFeatureSupport.Supported;

        if (entry.PreviewFrom > 0 && version.Release >= entry.PreviewFrom && version.Release <= entry.PreviewTo)
            return version.IsPreviewEnabled ? JavaFeatureSupport.Supported : JavaFeatureSupport.NeedsPreview;

        return JavaFeatureSupport.NeedsNewerRelease;
    }

    public static bool IsAvailable(JavaFeature feature, JavaVersion version) => GetSupport(feature, version) == JavaFeatureSupport.Supported;

    /// <summary>Gets the message for using a feature a version does not support, or null when it supports it.</summary>
    public static string? GetMessage(JavaFeature feature, JavaVersion version)
    {
        var entry = Table[feature];
        return GetSupport(feature, version) switch
        {
            JavaFeatureSupport.NeedsPreview => $"{entry.Name} are a preview feature of Java {version.Release}; enable preview features to use them",
            JavaFeatureSupport.NeedsNewerRelease when entry.Final is { } final => $"{entry.Name} need Java {final} or later",
            JavaFeatureSupport.NeedsNewerRelease => $"{entry.Name} need Java {entry.PreviewFrom} or later, with preview features enabled",
            _ => null,
        };
    }
}
