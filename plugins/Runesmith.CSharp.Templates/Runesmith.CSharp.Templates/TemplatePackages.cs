using System.Text.Json;

namespace Runesmith.CSharp.Templates;

/// <summary>A template package to read, and where it comes from.</summary>
/// <param name="Path">The <c>.nupkg</c> file.</param>
/// <param name="SdkTemplatesVersion">The version of the SDK templates folder it is in, or null for a package the user installed.</param>
internal sealed record TemplatePackage(string Path, Version? SdkTemplatesVersion);

/// <summary>Finds .NET roots and the template packages in them and in the template engine's folder.</summary>
internal static class TemplatePackages
{
    /// <summary>Gets the .NET roots from the environment: <c>DOTNET_ROOT</c> and the folder of the <c>dotnet</c> on the <c>PATH</c>.</summary>
    public static IReadOnlyList<string> FindRootsFromEnvironment()
    {
        var roots = new List<string>();
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && IsRoot(root))
            roots.Add(Path.GetFullPath(root));
        if (FindDotnetOnPath() is { } dotnet && System.IO.Path.GetDirectoryName(ResolveLinks(dotnet)) is { } folder && IsRoot(folder))
            roots.Add(Path.GetFullPath(folder));
        return [.. roots.Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gets the <c>dotnet</c> executable on the <c>PATH</c>, which may be a link or a wrapper script.</summary>
    public static string? FindDotnetOnPath()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(folder, name);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Gets the <c>dotnet</c> executable in a .NET root.</summary>
    public static string DotnetIn(string root) => Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    /// <summary>Gets the SDK's template packages in a root: every <c>templates/&lt;version&gt;/*.nupkg</c>.</summary>
    public static IEnumerable<TemplatePackage> FindSdkPackages(string root)
    {
        var templates = Path.Combine(root, "templates");
        if (!Directory.Exists(templates))
            yield break;

        foreach (var folder in Directory.EnumerateDirectories(templates))
        {
            var version = ParseVersion(Path.GetFileName(folder));
            foreach (var package in Directory.EnumerateFiles(folder, "*.nupkg"))
                yield return new TemplatePackage(package, version);
        }
    }

    /// <summary>Gets the packages installed with <c>dotnet new install</c>: those <c>packages.json</c> lists, or every package in the
    /// packages folder when there is no list.</summary>
    public static IEnumerable<TemplatePackage> FindInstalledPackages(string templateEngineFolder)
    {
        var list = Path.Combine(templateEngineFolder, "packages.json");
        if (File.Exists(list))
        {
            List<string> paths;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(list), new JsonDocumentOptions { AllowTrailingCommas = true });
                paths = document.RootElement.TryGetProperty("Packages", out var packages) && packages.ValueKind == JsonValueKind.Array
                    ? [.. packages.EnumerateArray()
                        .Select(p => p.TryGetProperty("MountPointUri", out var uri) ? uri.GetString() : null)
                        .OfType<string>()
                        .Select(ToPath)
                        .Where(p => p.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && File.Exists(p))]
                    : [];
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                paths = [];
            }

            foreach (var path in paths)
                yield return new TemplatePackage(path, null);
            yield break;
        }

        var folder = Path.Combine(templateEngineFolder, "packages");
        if (Directory.Exists(folder))
        {
            foreach (var package in Directory.EnumerateFiles(folder, "*.nupkg"))
                yield return new TemplatePackage(package, null);
        }
    }

    /// <summary>Gets the template engine's folder: <c>.templateengine</c> in <c>DOTNET_CLI_HOME</c>, or else in the user's profile.</summary>
    public static string TemplateEngineFolder()
    {
        var home = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME") is { Length: > 0 } cliHome
            ? cliHome
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".templateengine");
    }

    /// <summary>Parses a templates folder's name, such as <c>10.0.12</c> or <c>10.0.0-preview.7</c>, ignoring any suffix.</summary>
    public static Version? ParseVersion(string text)
    {
        var end = text.IndexOfAny(['-', '+']);
        return Version.TryParse(end < 0 ? text : text[..end], out var version) ? version : null;
    }

    private static bool IsRoot(string folder) => Directory.Exists(Path.Combine(folder, "sdk")) || Directory.Exists(Path.Combine(folder, "templates"));

    private static string ResolveLinks(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    private static string ToPath(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : uri;

    /// <summary>Formats a version for display.</summary>
    public static string Format(Version version) => version.ToString(version.Build >= 0 ? 3 : 2);
}
