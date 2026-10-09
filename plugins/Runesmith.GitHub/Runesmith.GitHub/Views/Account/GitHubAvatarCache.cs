using System.Composition;
using Runesmith.Plugins.Views;

namespace Runesmith.GitHub.Views.Account;

/// <summary>The GitHub plugin's avatars: the signed-in account's.</summary>
[Export]
[Shared]
internal sealed class GitHubAvatarCache : AvatarCache;
