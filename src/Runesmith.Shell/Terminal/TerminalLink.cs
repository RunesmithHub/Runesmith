using System.Globalization;
using System.Text.RegularExpressions;

namespace Runesmith.Shell.Terminal;

/// <summary>A link a program wrote around text with OSC 8, <c>ESC ] 8 ; params ; URI ST</c> before the text and <c>ESC ] 8 ; ; ST</c> after
/// it; each cell of the text refers to it.</summary>
/// <param name="uri">Where the link leads, as the program wrote it.</param>
/// <param name="id">The <c>id</c> parameter, which makes parts of a link written apart one link, or null.</param>
internal sealed partial class TerminalLink(string uri, string? id)
{
    private CellExtras? extras;

    public string Uri { get; } = uri;

    public string? Id { get; } = id;

    /// <summary>Gets the http or https address the link opens in the browser, or null when it leads elsewhere.</summary>
    public string? WebAddress => System.Uri.TryCreate(Uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https" ? parsed.AbsoluteUri : null;

    /// <summary>Gets the file on this computer the link opens in the editor, with the line and column its fragment gives, such as <c>#12</c>,
    /// <c>#L12</c> or <c>#L12:5</c>, counted from 1 and 0 when it gives none; or null when the link leads elsewhere.</summary>
    public (string Path, int Line, int Column)? File
    {
        get
        {
            if (!Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || !System.Uri.TryCreate(Uri, UriKind.Absolute, out var parsed) || !parsed.IsFile
                || !IsThisComputer(parsed.Host))
                return null;

            var place = Place().Match(parsed.Fragment);
            return (LocalPath(parsed), place.Success ? Number(place, "line") : 0, place.Success ? Number(place, "column") : 0);
        }
    }

    /// <summary>Gets what a cell that is part of the link and has no combining marks holds besides its character, shared by all of them.</summary>
    internal CellExtras Extras => extras ??= new CellExtras(null, this);

    /// <summary>Gets whether another link is this one: the same link, or one with the same id and address.</summary>
    public bool IsSameAs(TerminalLink? other) =>
        ReferenceEquals(this, other) || (other is not null && Id is not null && Id == other.Id && Uri == other.Uri);

    /// <summary>Gets the path a file URI names, such as <c>/home/me</c> for <c>file://host/home/me</c>, or <c>C:\Users\me</c> for
    /// <c>file:///C:/Users/me</c> on Windows.</summary>
    public static string LocalPath(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var path = System.Uri.UnescapeDataString(uri.AbsolutePath);
        var drive = path.StartsWith('/') ? path[1..] : path;
        return OperatingSystem.IsWindows() && drive.Length >= 2 && char.IsAsciiLetter(drive[0]) && drive[1] == ':' ? drive.Replace('/', '\\') : path;
    }

    // A file URI names a file of another computer when a program on it wrote the link, such as over SSH.
    private static bool IsThisComputer(string host) =>
        host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.Split('.')[0].Equals(Environment.MachineName.Split('.')[0], StringComparison.OrdinalIgnoreCase);

    private static int Number(Match match, string group) =>
        match.Groups[group].Success && int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    [GeneratedRegex(@"^#L?(?<line>\d+)(?:[:C](?<column>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Place();
}
