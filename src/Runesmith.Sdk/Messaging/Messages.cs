namespace Runesmith.Sdk.Messaging;

/// <summary>Published after a folder opens, or the open folder closes, when <see cref="RootPath"/> is null.</summary>
public sealed record WorkspaceOpenedMessage(string? RootPath);

/// <summary>Published after the theme changes.</summary>
public sealed record ThemeChangedMessage(bool IsDark);

/// <summary>Published when files of the open folder change on disk, are created, deleted or renamed.</summary>
public sealed record FilesChangedMessage(IReadOnlyList<string> Paths);
