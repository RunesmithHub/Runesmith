using System.IO.Compression;
using Runesmith.Languages.Java.Libraries;
using Runesmith.Languages.Java.Tests.Jdk;

namespace Runesmith.Languages.Java.Tests.Libraries;

public sealed class JarLibraryTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-jars-").FullName;

    [Fact]
    public void ReadsTheClassesOfAJarCompiledWithJavac()
    {
        var classes = TestJdk.Compile(new Dictionary<string, string>
        {
            ["lib/Greeter.java"] = "package lib; public class Greeter { public String greet(String name) { return name; } class Helper {} Runnable r = new Runnable() { public void run() {} }; }",
            ["lib/util/Strings.java"] = "package lib.util; public final class Strings { public static boolean empty(String s) { return s.isEmpty(); } }",
        }, "-parameters");
        var jar = Path.Combine(folder, "lib.jar");
        ZipFile.CreateFromDirectory(classes, jar);

        using var library = JarLibrary.Open(jar, 21);

        Assert.Equal(["lib", "lib.util"], library.Packages.Order(StringComparer.Ordinal));
        Assert.Equal(["lib.Greeter", "lib.Greeter$Helper"], library.GetTypes("lib"));
        Assert.Equal("greet", library.FindClass("lib.Greeter")!.Methods.Single(m => m.Name == "greet").Name);
        Assert.Equal(["name"], library.FindClass("lib.Greeter")!.Methods.Single(m => m.Name == "greet").ParameterNames);
        Assert.Equal(["lib.util.Strings"], library.FindBySimpleName("Strings"));
    }

    [Fact]
    public void PicksTheNewestClassAMultiReleaseJarHasForTheRelease()
    {
        var jdk = TestJdk.Require();
        var string11 = ReadCtSym(jdk.CtSymPath, 'B', "java.base/java/lang/String.sig");
        var string21 = ReadCtSym(jdk.CtSymPath, 'L', "java.base/java/lang/String.sig");
        var jar = Path.Combine(folder, "multi.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
        {
            Write(archive, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nMulti-Release: true\r\n"u8.ToArray());
            Write(archive, "java/lang/String.class", string11);
            Write(archive, "META-INF/versions/21/java/lang/String.class", string21);
        }

        static bool HasNewIndexOf(JarLibrary library) =>
            library.FindClass("java.lang.String")!.Methods.Any(m => m.Name == "indexOf" && m.ParameterTypes.Count == 3 && m.ParameterTypes.All(t => t.ToJavaString() == "int"));

        using var forRelease17 = JarLibrary.Open(jar, 17);
        using var forRelease21 = JarLibrary.Open(jar, 21);
        Assert.False(HasNewIndexOf(forRelease17));
        Assert.True(HasNewIndexOf(forRelease21));
    }

    [Fact]
    public void IgnoresVersionFoldersOfAJarThatIsNotMultiRelease()
    {
        var jdk = TestJdk.Require();
        var jar = Path.Combine(folder, "plain.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
            Write(archive, "META-INF/versions/21/java/lang/String.class", ReadCtSym(jdk.CtSymPath, 'L', "java.base/java/lang/String.sig"));

        using var library = JarLibrary.Open(jar, 25);
        Assert.Empty(library.TypeNames);
    }

    [Fact]
    public void ComposesLibrariesWithTheFirstOneWinning()
    {
        var jdk = TestJdk.Require();
        var jar = Path.Combine(folder, "shadow.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
            Write(archive, "java/lang/String.class", ReadCtSym(jdk.CtSymPath, 'B', "java.base/java/lang/String.sig"));

        using var library = JarLibrary.Open(jar, 25);
        var classPath = new ClassPath([TestJdk.Api(25), library]);

        Assert.Same(TestJdk.Api(25), classPath.LibraryOf("java.lang.String"));
        Assert.Contains(classPath.FindClass("java.lang.String")!.Methods, m => m.Name == "indexOf" && m.ParameterTypes.Count == 3);
        Assert.Contains("java.lang", classPath.Packages);
    }

    [Fact]
    public void ReportsAFileThatIsNotAJar()
    {
        var path = Path.Combine(folder, "broken.jar");
        File.WriteAllText(path, "not a zip");

        Assert.Throws<IOException>(() => JarLibrary.Open(path, 21));
    }

    [Fact]
    public void CachesItsIndex()
    {
        var jdk = TestJdk.Require();
        var jar = Path.Combine(folder, "cached.jar");
        using (var archive = ZipFile.Open(jar, ZipArchiveMode.Create))
            Write(archive, "java/lang/String.class", ReadCtSym(jdk.CtSymPath, 'B', "java.base/java/lang/String.sig"));
        var cache = Path.Combine(folder, "cache");

        using (JarLibrary.Open(jar, 21, cache))
        {
        }

        Assert.Single(Directory.GetFiles(cache, "*.idx"));
        using var reopened = JarLibrary.Open(jar, 21, cache);
        Assert.True(reopened.Contains("java.lang.String"));
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static byte[] ReadCtSym(string ctSym, char releaseLetter, string path)
    {
        using var archive = ZipFile.OpenRead(ctSym);
        var entry = archive.Entries.First(e => e.FullName.EndsWith("/" + path, StringComparison.Ordinal) && e.FullName.AsSpan(0, e.FullName.IndexOf('/')).Contains(releaseLetter));
        var bytes = new byte[entry.Length];
        using var stream = entry.Open();
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static void Write(ZipArchive archive, string name, byte[] bytes)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(bytes);
    }
}
