using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Tests.ClassFiles;

public sealed class SignatureParserTests
{
    [Theory]
    [InlineData("I", "int")]
    [InlineData("Z", "boolean")]
    [InlineData("[[J", "long[][]")]
    [InlineData("Ljava/lang/String;", "java.lang.String")]
    [InlineData("Ljava/util/Map$Entry;", "java.util.Map.Entry")]
    [InlineData("TT;", "T")]
    [InlineData("Ljava/util/List<+Ljava/lang/Number;>;", "java.util.List<? extends java.lang.Number>")]
    [InlineData("Ljava/util/Comparator<-TT;>;", "java.util.Comparator<? super T>")]
    [InlineData("Ljava/util/Map<*Ljava/lang/String;>;", "java.util.Map<?, java.lang.String>")]
    public void ReadsFieldTypes(string signature, string expected) =>
        Assert.Equal(expected, SignatureParser.ParseType(signature).ToJavaString());

    [Fact]
    public void ReadsAnInnerClassOfAParameterizedOuterClass()
    {
        var type = (ClassTypeReference)SignatureParser.ParseType("Ljava/util/Map<TK;TV;>.Entry<TK;TV;>;");

        Assert.Equal("java.util.Map$Entry", type.BinaryName);
        Assert.Equal("java.util.Map", type.Outer!.BinaryName);
        Assert.Equal("java.util.Map<K, V>.Entry<K, V>", type.ToJavaString());
    }

    [Fact]
    public void ReadsGenericMethodsWithBoundsAndThrows()
    {
        var method = SignatureParser.ParseMethod("<T:Ljava/lang/Object;:Ljava/lang/Comparable<-TT;>;>(Ljava/util/List<TT;>;[I)TT;^Ljava/io/IOException;^TX;");

        var parameter = Assert.Single(method.TypeParameters);
        Assert.Equal("T", parameter.Name);
        Assert.Equal(2, parameter.Bounds.Count);
        Assert.Equal("java.util.List<T>", method.ParameterTypes[0].ToJavaString());
        Assert.Equal("int[]", method.ParameterTypes[1].ToJavaString());
        Assert.Equal("T", method.ReturnType.ToJavaString());
        Assert.Equal(["java.io.IOException", "X"], method.Throws.Select(t => t.ToJavaString()));
    }

    [Fact]
    public void ReadsInterfaceOnlyBounds()
    {
        var method = SignatureParser.ParseMethod("<T::Ljava/lang/Runnable;>()V");

        Assert.Equal("T extends java.lang.Runnable", method.TypeParameters[0].ToJavaString());
    }

    [Fact]
    public void ReadsClassSignatures()
    {
        var signature = SignatureParser.ParseClass("<K:Ljava/lang/Object;V:Ljava/lang/Object;>Ljava/util/AbstractMap<TK;TV;>;Ljava/util/Map<TK;TV;>;Ljava/io/Serializable;");

        Assert.Equal(["K", "V"], signature.TypeParameters.Select(p => p.Name));
        Assert.Equal("java.util.AbstractMap<K, V>", signature.SuperClass!.ToJavaString());
        Assert.Equal(["java.util.Map<K, V>", "java.io.Serializable"], signature.Interfaces.Select(i => i.ToJavaString()));
    }

    [Theory]
    [InlineData("Ljava/lang/String")]
    [InlineData("Q")]
    [InlineData("I;")]
    public void RejectsBrokenSignatures(string signature) =>
        Assert.Throws<ClassFileFormatException>(() => SignatureParser.ParseType(signature));

    [Fact]
    public void ComparesTypesByValue() =>
        Assert.Equal(SignatureParser.ParseType("Ljava/util/List<Ljava/lang/String;>;"), SignatureParser.ParseType("Ljava/util/List<Ljava/lang/String;>;"));
}
