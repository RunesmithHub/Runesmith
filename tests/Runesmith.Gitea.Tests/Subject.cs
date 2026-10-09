using Runesmith.Gitea;

namespace Runesmith.Plugins.Gitea.Tests;

/// <summary>The plugin the shared tests run against.</summary>
internal static class Subject
{
    public static GiteaFlavor Flavor => GiteaService.Flavor;

    /// <summary>Gets the kind of server the other plugin takes.</summary>
    public static ServerKind OtherKind => ServerKind.Forgejo;
}
