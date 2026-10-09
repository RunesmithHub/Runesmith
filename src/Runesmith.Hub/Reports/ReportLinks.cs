using System.Text;

namespace Runesmith.Hub.Reports;

/// <summary>What a report is about, and what Runesmith fills in for the user to read and edit before sending.</summary>
/// <param name="Repository">The plugin's source repository, such as <c>https://github.com/lumen-labs/lumen-todo</c>.</param>
public sealed record ReportSubject(string PluginId, string Version, string Repository, string RunesmithVersion, string OperatingSystem);

/// <summary>The addresses the Report menu opens in the browser. Runesmith never sends a report itself.</summary>
public static class ReportLinks
{
    /// <summary>The registry, where policy reports and private security reports go.</summary>
    public const string Registry = "https://github.com/RunesmithHub/registry";

    /// <summary>The address for private reports by people without a GitHub account.</summary>
    public const string SecurityEmail = "security@runesmith.dev";

    /// <summary>Gets the registry's private vulnerability reporting form. GitHub does not fill it in from the address, so the details go to
    /// the clipboard for the user to paste.</summary>
    public static Uri Security() => new($"{Registry}/security/advisories/new");

    /// <summary>Gets an email to the security address, for people without a GitHub account.</summary>
    public static Uri SecurityByEmail(ReportSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new($"mailto:{SecurityEmail}?subject={Uri.EscapeDataString($"Security report: {subject.PluginId} {subject.Version}")}&body={Uri.EscapeDataString(Details(subject))}");
    }

    /// <summary>Gets a new issue in the plugin's own repository, with the details and, when the user chose to, log lines they reviewed.</summary>
    /// <returns>Null when the repository is not on GitHub.</returns>
    public static Uri? Broken(ReportSubject subject, string? logLines = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!Uri.TryCreate(subject.Repository.TrimEnd('/'), UriKind.Absolute, out var repository) || repository.Scheme != Uri.UriSchemeHttps || repository.Host != "github.com")
            return null;

        var body = Details(subject);
        if (!string.IsNullOrWhiteSpace(logLines))
            body += $"\nRecent log lines:\n```\n{Trim(logLines, 4000)}\n```\n";
        return new($"{repository.AbsoluteUri.TrimEnd('/')}/issues/new?title={Uri.EscapeDataString($"Broken: {subject.PluginId} {subject.Version}")}&body={Uri.EscapeDataString(body)}");
    }

    /// <summary>Gets a new policy issue in the registry, for impersonation, spam or license problems.</summary>
    public static Uri Policy(ReportSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var body = $"Plugin: {subject.PluginId}\nVersion: {subject.Version}\n\nWhat breaks the rules:\n";
        return new($"{Registry}/issues/new?template=policy.yml&title={Uri.EscapeDataString($"Policy: {subject.PluginId}")}&body={Uri.EscapeDataString(body)}");
    }

    /// <summary>Gets the details every report starts with.</summary>
    public static string Details(ReportSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new StringBuilder()
            .Append("Plugin: ").AppendLine(subject.PluginId)
            .Append("Version: ").AppendLine(subject.Version)
            .Append("Runesmith version: ").AppendLine(subject.RunesmithVersion)
            .Append("Operating system: ").AppendLine(subject.OperatingSystem)
            .AppendLine()
            .AppendLine("What happened:")
            .ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Trim(string text, int length) => text.Length <= length ? text : text[^length..];
}
