using System.IO.Compression;
using BenchmarkDotNet.Attributes;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Jdk;

namespace Runesmith.Benchmarks.JavaLibrary;

/// <summary>Loading a Java release's API from a JDK, cold and from the cache, and reading classes from it.</summary>
[MemoryDiagnoser]
public class JdkApiBenchmarks
{
    private JdkInstallation jdk = null!;
    private string cache = null!;
    private JdkApi warm = null!;
    private byte[] hashMap = [];

    [GlobalSetup]
    public void Setup()
    {
        jdk = JdkLocator.FindFor(21) ?? throw new InvalidOperationException("The benchmarks need a JDK 21 or newer; set JAVA_HOME to one.");
        cache = Directory.CreateTempSubdirectory("runesmith-bench-jdk-").FullName;
        JdkApi.Open(jdk, 21, cache).Dispose();
        warm = JdkApi.Open(jdk, 21, cache);
        warm.FindClass("java.util.HashMap");

        using var archive = ZipFile.OpenRead(jdk.CtSymPath);
        var entry = archive.Entries.First(e => e.FullName.EndsWith("/java.base/java/util/HashMap.sig", StringComparison.Ordinal)
            && e.FullName.AsSpan(0, e.FullName.IndexOf('/')).Contains(JdkApi.ReleaseLetter(21)));
        hashMap = new byte[entry.Length];
        using var stream = entry.Open();
        stream.ReadExactly(hashMap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        warm.Dispose();
        Directory.Delete(cache, recursive: true);
    }

    /// <summary>The first load of a release: reading <c>ct.sym</c>'s entries and the modules' exports.</summary>
    [Benchmark]
    public int OpenCold()
    {
        using var api = JdkApi.Open(jdk, 21);
        return api.Packages.Count;
    }

    /// <summary>Every later load: reading the cached index.</summary>
    [Benchmark]
    public int OpenCached()
    {
        using var api = JdkApi.Open(jdk, 21, cache);
        return api.Packages.Count;
    }

    /// <summary>Reading a large class and all of its members.</summary>
    [Benchmark]
    public int DecodeClassWithMembers() => ClassSymbol.Read(hashMap).Methods.Count;

    /// <summary>Finding the types with a simple name, as completing a type that is not imported does.</summary>
    [Benchmark]
    public int FindBySimpleName() => warm.FindBySimpleName("List").Count + warm.FindBySimpleName("Map").Count;

    /// <summary>Looking up a class that was read before.</summary>
    [Benchmark]
    public int FindDecodedClass() => warm.FindClass("java.util.HashMap")!.Methods.Count;
}
