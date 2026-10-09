using System.Composition;
using Runesmith.Plugins.Views;

namespace Runesmith.Git.Views;

/// <summary>The Git plugin's avatars: pull request authors and repository owners.</summary>
[Export]
[Shared]
internal sealed class GitAvatarCache : AvatarCache;
