namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class VersionTests
{
    public static TheoryData<JavaFeature, string> Snippets => new()
    {
        { JavaFeature.Lambdas, "class C { Runnable r = () -> {}; }" },
        { JavaFeature.MethodReferences, "class C { Runnable r = C::new; }" },
        { JavaFeature.DefaultMethods, "interface I { default void m() {} }" },
        { JavaFeature.Modules, "module m { requires java.base; }" },
        { JavaFeature.LocalVariableTypeInference, "class C { void m() { var x = 1; } }" },
        { JavaFeature.VarInLambdaParameters, "class C { java.util.function.IntUnaryOperator f = (var x) -> x; }" },
        { JavaFeature.SwitchExpressions, "class C { int m(int x) { return switch (x) { case 1 -> 2; default -> 3; }; } }" },
        { JavaFeature.TextBlocks, "class C { String s = \"\"\"\n    text\n    \"\"\"; }" },
        { JavaFeature.Records, "record R(int x) {}" },
        { JavaFeature.InstanceofPatterns, "class C { boolean m(Object o) { return o instanceof String s; } }" },
        { JavaFeature.SealedClasses, "sealed interface S permits A {} final class A implements S {}" },
        { JavaFeature.SwitchPatterns, "class C { void m(Object o) { switch (o) { case String s: break; default: break; } } }" },
        { JavaFeature.RecordPatterns, "class C { boolean m(Object o) { return o instanceof java.util.Map.Entry(Object k, Object v); } }" },
        { JavaFeature.UnnamedVariables, "class C { void m(int[] a) { for (int _ : a) { } } }" },
        { JavaFeature.ModuleImports, "import module java.base; class C {}" },
        { JavaFeature.CompactSourceFiles, "void main() { }" },
        { JavaFeature.FlexibleConstructorBodies, "class C { C(int x) { if (x < 0) throw new Error(); super(); } }" },
        { JavaFeature.PrimitiveTypesInPatterns, "class C { boolean m(Object o) { return o instanceof int i; } }" },
    };

    private static string[] Messages(JavaSyntaxTree tree, string code) => [.. tree.Diagnostics.Where(d => d.Code == code).Select(d => d.Message)];

    [Theory]
    [MemberData(nameof(Snippets))]
    public void EveryFeatureParsesCleanlyInTheLatestReleaseWithPreviews(JavaFeature feature, string snippet)
    {
        _ = feature;
        Java.ParseClean(snippet, JavaVersion.LatestRelease, preview: true);
    }

    [Theory]
    [MemberData(nameof(Snippets))]
    public void AReleaseBeforeAFeatureReportsIt(JavaFeature feature, string snippet)
    {
        var first = JavaFeatures.PreviewReleases(feature)?.First ?? JavaFeatures.FinalRelease(feature)!.Value;
        var supported = JavaFeatures.FinalRelease(feature) ?? JavaVersion.LatestRelease;
        // Java 8 still allows _ as a name.
        var from = feature == JavaFeature.UnnamedVariables ? 9 : JavaVersion.MinimumRelease;
        for (var release = from; release <= JavaVersion.LatestRelease; release++)
        {
            var version = new JavaVersion(release);
            var tree = Java.Parse(snippet, release);
            var message = JavaFeatures.GetMessage(feature, version);
            Assert.Empty(Messages(tree, SyntaxDiagnostic.SyntaxError));
            if (release < first)
                Assert.Contains(message, Messages(tree, SyntaxDiagnostic.ReleaseError));
            else if (JavaFeatures.GetSupport(feature, version) == JavaFeatureSupport.NeedsPreview)
                Assert.Contains(message, Messages(tree, SyntaxDiagnostic.PreviewError));
            else
                Assert.True(release >= supported && message is null, $"Java {release}");
        }
    }

    [Theory]
    [MemberData(nameof(Snippets))]
    public void EnablingPreviewsAllowsAFeatureInItsPreviewReleases(JavaFeature feature, string snippet)
    {
        if (JavaFeatures.PreviewReleases(feature) is not var (first, last))
            return;

        for (var release = first; release <= last; release++)
        {
            var tree = Java.Parse(snippet, release, preview: true);
            Assert.DoesNotContain(tree.Diagnostics, d => d.Message.StartsWith(JavaFeatures.DisplayName(feature), StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MessagesNameTheReleaseNeeded()
    {
        var records = Java.Parse("record R() {}", 15);
        Assert.Equal("Records are a preview feature of Java 15; enable preview features to use them", Messages(records, SyntaxDiagnostic.PreviewError).Single());
        var old = Java.Parse("record R() {}", 11);
        Assert.Equal("Records need Java 16 or later", Messages(old, SyntaxDiagnostic.ReleaseError).Single());
        var primitive = Java.Parse("class C { boolean m(Object o) { return o instanceof int i; } }", 21);
        Assert.Contains("need Java 23 or later, with preview features enabled", Messages(primitive, SyntaxDiagnostic.ReleaseError).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagnosticCoversTheConstruct()
    {
        var text = "class C { void m() { var x = 1; } }";
        var tree = Java.Parse(text, 9);
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("var", text.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
    }

    [Fact]
    public void Java8AllowsTheUnderscoreAsAName()
    {
        Assert.Empty(Java.Parse("class C { void m() { int _ = 1; } }", 8).Diagnostics);
        Assert.NotEmpty(Java.Parse("class C { void m() { int _ = 1; } }", 9).Diagnostics);
    }

    [Fact]
    public void RecordComponentsOfPrimitiveTypesAreNotPrimitivePatterns() =>
        Java.ParseClean("record P(int x) {} class C { boolean m(Object o) { return o instanceof P(int x); } }", 21, preview: false);

    [Fact]
    public void MarkdownCommentsAreNeverReported() => Java.ParseClean("/// Doc\nclass C {}", 8, preview: false);

    [Fact]
    public void VersionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JavaVersion(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JavaVersion(26));
        Assert.Equal(8, JavaVersion.FromRelease(1).Release);
        Assert.Equal(25, JavaVersion.FromRelease(99).Release);
        Assert.Equal("Java 25 with preview features", new JavaVersion(25, isPreviewEnabled: true).ToString());
        Assert.Equal(JavaVersion.LatestRelease, JavaParseOptions.Default.Version.Release);
    }

    [Fact]
    public void EveryFeatureHasAnEntry()
    {
        foreach (var feature in Enum.GetValues<JavaFeature>())
        {
            Assert.False(string.IsNullOrEmpty(JavaFeatures.DisplayName(feature)));
            Assert.True(JavaFeatures.FinalRelease(feature) is not null || JavaFeatures.PreviewReleases(feature) is not null);
            Assert.True(JavaFeatures.IsAvailable(feature, new JavaVersion(JavaVersion.LatestRelease, isPreviewEnabled: true)));
        }
    }
}
