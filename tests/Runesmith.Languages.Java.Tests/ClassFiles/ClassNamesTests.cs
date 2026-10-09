using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Tests.ClassFiles;

public sealed class ClassNamesTests
{
    [Fact]
    public void SplitsBinaryNames()
    {
        Assert.Equal("java.util", ClassNames.PackageName("java.util.Map$Entry"));
        Assert.Equal("Entry", ClassNames.SimpleName("java.util.Map$Entry"));
        Assert.Equal("Map.Entry", ClassNames.NestedName("java.util.Map$Entry"));
        Assert.Equal("java.util.Map.Entry", ClassNames.SourceName("java.util.Map$Entry"));
        Assert.Equal("java.util.Map", ClassNames.OuterName("java.util.Map$Entry"));
        Assert.Null(ClassNames.OuterName("java.util.Map"));
        Assert.Equal("", ClassNames.PackageName("Main"));
    }

    [Theory]
    [InlineData("a.Outer$1", true)]
    [InlineData("a.Outer$1Local", true)]
    [InlineData("a.Outer$Inner$2", true)]
    [InlineData("a.Outer$Inner", false)]
    [InlineData("a.Outer", false)]
    public void RecognizesClassesNoCodeCanName(string name, bool expected) => Assert.Equal(expected, ClassNames.IsAnonymousOrLocal(name));
}
