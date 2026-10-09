using System.Composition;
using Runesmith.Java.Sdks;
using Runesmith.Languages.Java.Analysis;
using Runesmith.Languages.Java.Jdk;
using Runesmith.LanguageServices;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java;

/// <summary>Runesmith's own Java analyzer: completion, hover, go to definition, signature help and problems, without a JVM.</summary>
[Shared]
public sealed class JavaAnalysis
{
    private readonly ISdkService? sdks;
    private Task<JdkSearchOptions>? search;

    /// <summary>Creates the analyzer, which reads the API of the user's default JDK and also finds the JDKs Runesmith installed.</summary>
    [ImportingConstructor]
    public JavaAnalysis([Import(AllowDefault = true)] ISdkService? sdks)
    {
        this.sdks = sdks;
        sdks?.Changed += (_, _) => Volatile.Write(ref search, null);
        Analyzer = new JavaAnalyzer { JdkSearch = Search };
    }

    [Export(typeof(ILanguageAnalyzer))]
    public ILanguageAnalyzer Analyzer { get; }

    // The analyzer asks from its own threads, where waiting for the SDK service, which only reads the disk, is safe.
    internal JdkSearchOptions Search()
    {
        var pending = Volatile.Read(ref search);
        if (pending is null)
        {
            pending = FindAsync();
            Volatile.Write(ref search, pending);
        }

        return pending.GetAwaiter().GetResult();
    }

    private async Task<JdkSearchOptions> FindAsync()
    {
        var options = new JdkSearchOptions { ExtraInstallFolders = [JdkSdkEnvironment.ManagedFolderPath] };
        var preferred = sdks is null ? null : await sdks.GetDefaultAsync(JdkSdkProvider.SdkKind).ConfigureAwait(false);
        return options with { PreferredHome = preferred?.Path };
    }
}
