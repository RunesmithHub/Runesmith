namespace Runesmith.Sdk.Sdks;

/// <summary>Where Runesmith found an SDK.</summary>
public enum SdkSource
{
    /// <summary>Installed on the system or by the user, outside Runesmith; Runesmith never removes it.</summary>
    System,

    /// <summary>Downloaded and installed by Runesmith, in its SDKs folder.</summary>
    Managed,
}

/// <summary>An SDK on this machine.</summary>
/// <param name="Kind">The kind of SDK, such as <c>dotnet</c> or <c>jdk</c>.</param>
/// <param name="Version">The version, such as <c>10.0.401</c> or <c>25.0.1</c>.</param>
/// <param name="Path">The SDK's home: the folder <c>DOTNET_ROOT</c> or <c>JAVA_HOME</c> would point at.</param>
public sealed record InstalledSdk(string Kind, string Version, string Path)
{
    /// <summary>Gets who built it, such as Microsoft or Eclipse Temurin, or null when unknown.</summary>
    public string? Vendor { get; init; }

    /// <summary>Gets where Runesmith found it.</summary>
    public SdkSource Source { get; init; }

    /// <summary>Gets the processor architecture it runs on, such as <c>x64</c>.</summary>
    public string? Architecture { get; init; }

    /// <summary>Gets the name shown in lists, such as "Temurin 25.0.1".</summary>
    public string DisplayName => Vendor is null ? Version : $"{Vendor} {Version}";
}

/// <summary>An SDK that can be downloaded for this machine.</summary>
/// <param name="Kind">The kind of SDK.</param>
/// <param name="Version">The version it installs.</param>
/// <param name="Vendor">Who builds it.</param>
/// <param name="Download">The archive to download.</param>
public sealed record SdkRelease(string Kind, string Version, string Vendor, Uri Download)
{
    /// <summary>Gets the release line it belongs to, such as <c>10.0</c> or <c>25</c>; updates stay within one.</summary>
    public string? Line { get; init; }

    /// <summary>Gets a variant of the package, such as "JDK with JavaFX", or null for the plain one.</summary>
    public string? Package { get; init; }

    /// <summary>Gets the processor architecture.</summary>
    public string? Architecture { get; init; }

    /// <summary>Gets whether the release line has long-term support.</summary>
    public bool IsLongTermSupport { get; init; }

    /// <summary>Gets the line's support phase, such as "active", "maintenance" or "preview", or null when unknown.</summary>
    public string? SupportPhase { get; init; }

    /// <summary>Gets the release date, when known.</summary>
    public DateOnly? Released { get; init; }

    /// <summary>Gets the archive's checksum as hexadecimal, or null when the vendor publishes none.</summary>
    public string? Checksum { get; init; }

    /// <summary>Gets the checksum's algorithm, such as <c>SHA-256</c> or <c>SHA-512</c>.</summary>
    public string? ChecksumAlgorithm { get; init; }

    /// <summary>Gets the archive's size in bytes, when known.</summary>
    public long? Size { get; init; }
}

/// <summary>How far an install got.</summary>
/// <param name="Stage">What happens now, such as "Downloading" or "Unpacking".</param>
/// <param name="Fraction">How much of the stage is done, from 0 to 1, or null when unknown.</param>
public sealed record SdkInstallProgress(string Stage, double? Fraction);

/// <summary>Finds, lists and installs the SDKs of one kind, such as the .NET SDK or JDKs. Export it with
/// <c>[Export(typeof(ISdkProvider))]</c>.</summary>
public interface ISdkProvider
{
    /// <summary>Gets the kind of SDK, such as <c>dotnet</c> or <c>jdk</c>; options and settings refer to it.</summary>
    string Kind { get; }

    /// <summary>Gets the name shown for the kind, such as ".NET SDK" or "JDK".</summary>
    string Name { get; }

    /// <summary>Finds the SDKs on this machine, both the system's and Runesmith's.</summary>
    Task<IReadOnlyList<InstalledSdk>> FindInstalledAsync(CancellationToken cancellationToken);

    /// <summary>Lists the releases that can be downloaded for this machine; this reaches the vendors' services.</summary>
    Task<IReadOnlyList<SdkRelease>> GetReleasesAsync(CancellationToken cancellationToken);

    /// <summary>Downloads a release, checks its checksum and unpacks it into Runesmith's SDKs folder.</summary>
    Task<InstalledSdk> InstallAsync(SdkRelease release, IProgress<SdkInstallProgress> progress, CancellationToken cancellationToken);

    /// <summary>Removes an SDK Runesmith installed.</summary>
    Task UninstallAsync(InstalledSdk sdk, CancellationToken cancellationToken);

    /// <summary>Gets the newest release in the same line as an installed SDK, when it is newer than it; null otherwise.</summary>
    SdkRelease? FindUpdate(InstalledSdk sdk, IReadOnlyList<SdkRelease> releases);
}

/// <summary>The SDKs of every kind, for forms and features that need one.</summary>
public interface ISdkService
{
    /// <summary>Gets the providers, one per kind.</summary>
    IReadOnlyList<ISdkProvider> Providers { get; }

    /// <summary>Gets the installed SDKs of a kind, newest first; found once and kept until <see cref="RefreshAsync"/>.</summary>
    Task<IReadOnlyList<InstalledSdk>> GetInstalledAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>Finds the installed SDKs again, such as after an install.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the SDK of a kind the user chose as the default, or the newest installed one, or null.</summary>
    Task<InstalledSdk?> GetDefaultAsync(string kind, CancellationToken cancellationToken = default);

    /// <summary>Raised when SDKs are installed or removed, or the default changes.</summary>
    event EventHandler? Changed;
}
