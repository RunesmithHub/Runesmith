using Runesmith.Sdk.Build;
using Runesmith.Text;

namespace Runesmith.Java.Tests;

public sealed class JavaBuildOutputParserTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "shop");

    [Fact]
    public void ReadsJavacErrors()
    {
        var path = Path.Combine(Root, "src", "main", "java", "Shop.java");

        var diagnostic = JavaBuildOutputParser.Parse($"{path}:12: error: cannot find symbol", Root);

        Assert.NotNull(diagnostic);
        Assert.Equal(path, diagnostic.FilePath);
        Assert.Equal(new TextPosition(11, 0), diagnostic.Start);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("cannot find symbol", diagnostic.Message);
        Assert.Equal(JavaBuildOutputParser.Source, diagnostic.Source);
    }

    [Fact]
    public void ResolvesRelativeJavacPathsAgainstTheRoot()
    {
        var diagnostic = JavaBuildOutputParser.Parse("src/Shop.java:3: warning: [deprecation] old() has been deprecated", Root);

        Assert.NotNull(diagnostic);
        Assert.Equal(Path.Combine(Root, "src", "Shop.java"), diagnostic.FilePath);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void ReadsMavenErrorsWithTheirColumns()
    {
        var path = Path.Combine(Root, "src", "main", "java", "Shop.java");

        var diagnostic = JavaBuildOutputParser.Parse($"[ERROR] {path}:[12,5] ';' expected", Root);

        Assert.NotNull(diagnostic);
        Assert.Equal(path, diagnostic.FilePath);
        Assert.Equal(new TextPosition(11, 4), diagnostic.Start);
        Assert.Equal("';' expected", diagnostic.Message);
    }

    [Theory]
    [InlineData("[ERROR] COMPILATION ERROR : ")]
    [InlineData("[INFO] BUILD FAILURE")]
    [InlineData("> Task :compileJava FAILED")]
    [InlineData("1 error")]
    public void IgnoresOtherLines(string line) => Assert.Null(JavaBuildOutputParser.Parse(line, Root));
}
