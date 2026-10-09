using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Tests.Jdk;

namespace Runesmith.Languages.Java.Tests.ClassFiles;

public sealed class ClassSymbolTests
{
    [Fact]
    public void ReadsAGenericInterfaceWithItsMembers()
    {
        var map = TestJdk.Api(25).FindClass("java.util.Map")!;

        Assert.Equal(ClassKind.Interface, map.Kind);
        Assert.Equal(["K", "V"], map.TypeParameters.Select(p => p.Name));
        Assert.True((map.Modifiers & JavaModifiers.Abstract) != 0);
        var entrySet = Assert.Single(map.Methods, m => m.Name == "entrySet");
        Assert.Equal("java.util.Set<java.util.Map.Entry<K, V>>", entrySet.ReturnType.ToJavaString());
        Assert.True(map.Methods.Single(m => m.Name == "getOrDefault").IsDefault);
        Assert.True(map.Methods.First(m => m.Name == "of").IsStatic);
        Assert.Contains("java.util.Map$Entry", map.NestedClasses);
    }

    [Fact]
    public void ReadsNestedTypesWithTheirRealModifiers()
    {
        var entry = TestJdk.Api(25).FindClass("java.util.Map$Entry")!;

        Assert.Equal("Entry", entry.SimpleName);
        Assert.Equal("java.util.Map", entry.OuterBinaryName);
        Assert.True(entry.IsStatic);
        Assert.True((entry.Modifiers & JavaModifiers.Public) != 0);
    }

    [Fact]
    public void ReadsClassesConstantsVarargsAndDeprecation()
    {
        var api = TestJdk.Api(25);
        var integer = api.FindClass("java.lang.Integer")!;
        var maxValue = integer.Fields.Single(f => f.Name == "MAX_VALUE");
        Assert.Equal(int.MaxValue, maxValue.ConstantValue);
        Assert.Equal("int", maxValue.Type.ToJavaString());

        var text = api.FindClass("java.lang.String")!;
        Assert.True((text.Modifiers & JavaModifiers.Final) != 0);
        Assert.Contains(text.Interfaces, i => i.ToJavaString() == "java.lang.Comparable<java.lang.String>");
        Assert.Contains(text.Methods, m => m.Name == "format" && m.IsVarargs && m.IsStatic);
        Assert.Equal("java.lang.Object", text.SuperClass!.BinaryName);

        var thread = api.FindClass("java.lang.Thread")!;
        Assert.Contains(thread.Methods, m => m.Name == "getId" && m.IsDeprecated);
        Assert.Contains(thread.Methods, m => m.Name == "threadId" && !m.IsDeprecated);
    }

    [Fact]
    public void ReadsEnumsRecordsAndSealedTypes()
    {
        var api = TestJdk.Api(25);
        var state = api.FindClass("java.lang.Thread$State")!;
        Assert.Equal(ClassKind.Enum, state.Kind);
        Assert.Contains(state.Fields, f => f.Name == "RUNNABLE" && f.IsEnumConstant);

        var constantDesc = api.FindClass("java.lang.constant.ConstantDesc")!;
        Assert.True((constantDesc.Modifiers & JavaModifiers.Sealed) != 0);
        Assert.Contains("java.lang.String", constantDesc.PermittedSubclasses);

        var principal = api.FindClass("jdk.net.UnixDomainPrincipal");
        if (principal is not null)
        {
            Assert.Equal(ClassKind.Record, principal.Kind);
            Assert.Equal(["user", "group"], principal.RecordComponents.Select(c => c.Name));
        }

        Assert.Null(api.FindClass("java.lang.Object")!.SuperClass);
    }

    [Fact]
    public void ReadsWhatJavacWritesForRecordsInnerClassesAndParameterNames()
    {
        var classes = TestJdk.Compile(
            new Dictionary<string, string>
            {
                ["sample/Shapes.java"] = """
                    package sample;
                    public sealed interface Shapes permits Shapes.Circle, Shapes.Square {
                        record Circle(double radius) implements Shapes {}
                        record Square(double side) implements Shapes {}
                    }
                    """,
                ["sample/Outer.java"] = """
                    package sample;
                    import java.util.List;
                    public class Outer<T> {
                        public class Inner { public Inner(String label, int count) {} }
                        public static <E extends Comparable<E>> E max(List<? extends E> items, E fallback) throws java.io.IOException { return fallback; }
                        @Deprecated public static final String NAME = "outer";
                        private int hidden;
                    }
                    """,
            },
            "-parameters", "--release", "21");

        var circle = ClassSymbol.Read(File.ReadAllBytes(Path.Combine(classes, "sample", "Shapes$Circle.class")));
        Assert.Equal(ClassKind.Record, circle.Kind);
        Assert.Equal("Circle", circle.SimpleName);
        Assert.Equal("radius", Assert.Single(circle.RecordComponents).Name);
        Assert.Equal(65, circle.MajorVersion);

        var shapes = ClassSymbol.Read(File.ReadAllBytes(Path.Combine(classes, "sample", "Shapes.class")));
        Assert.Equal(["sample.Shapes$Circle", "sample.Shapes$Square"], shapes.PermittedSubclasses);

        var inner = ClassSymbol.Read(File.ReadAllBytes(Path.Combine(classes, "sample", "Outer$Inner.class")));
        Assert.False(inner.IsStatic);
        var constructor = Assert.Single(inner.Constructors);
        Assert.Equal(["label", "count"], constructor.ParameterNames);
        Assert.True(constructor.HasParameterNames);
        Assert.Equal(["java.lang.String", "int"], constructor.ParameterTypes.Select(t => t.ToJavaString()));

        var outer = ClassSymbol.Read(File.ReadAllBytes(Path.Combine(classes, "sample", "Outer.class")));
        var max = outer.Methods.Single(m => m.Name == "max");
        Assert.Equal("E extends java.lang.Comparable<E>", max.TypeParameters[0].ToJavaString());
        Assert.Equal(["items", "fallback"], max.ParameterNames);
        Assert.Equal("java.util.List<? extends E>", max.ParameterTypes[0].ToJavaString());
        Assert.Equal("java.io.IOException", Assert.Single(max.Throws).ToJavaString());
        var name = outer.Fields.Single(f => f.Name == "NAME");
        Assert.True(name.IsDeprecated);
        Assert.Equal("outer", name.ConstantValue);
        Assert.DoesNotContain(outer.Fields, f => f.Name == "hidden");
    }

    [Fact]
    public void ReadsModuleExports()
    {
        var jdk = TestJdk.Require();
        using var archive = System.IO.Compression.ZipFile.OpenRead(jdk.CtSymPath);
        var entry = archive.Entries.First(e => e.FullName.EndsWith("/java.base/module-info.sig", StringComparison.Ordinal) && e.FullName.Contains('P', StringComparison.Ordinal));
        var bytes = new byte[entry.Length];
        using (var stream = entry.Open())
            stream.ReadExactly(bytes);

        var module = ClassSymbol.Read(bytes).Module!;
        Assert.Equal("java.base", module.Name);
        Assert.Contains(module.Exports, e => e.PackageName == "java.lang" && !e.IsQualified);
        Assert.Contains(module.Exports, e => e.PackageName == "java.util.concurrent");
        Assert.DoesNotContain(module.Exports, e => e.PackageName == "jdk.internal.misc" && !e.IsQualified);
    }

    [Fact]
    public void RejectsBytesThatAreNotAClassFile() =>
        Assert.Throws<ClassFileFormatException>(() => ClassSymbol.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }));

    [Fact]
    public void DecodesMembersOnceAndSafelyFromSeveralThreads()
    {
        var hashMap = JdkApiFresh().FindClass("java.util.HashMap")!;
        var counts = new int[16];
        Parallel.For(0, counts.Length, i => counts[i] = hashMap.Methods.Count);
        Assert.All(counts, c => Assert.Equal(counts[0], c));
        Assert.True(counts[0] > 10);
    }

    private static Runesmith.Languages.Java.Jdk.JdkApi JdkApiFresh() => Runesmith.Languages.Java.Jdk.JdkApi.Open(TestJdk.Require(), 25);
}
