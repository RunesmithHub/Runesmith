using Runesmith.Git.Git;
using Runesmith.Git.Views.Branches;

namespace Runesmith.Git.Tests.Views;

public sealed class BranchRowsTests
{
    private static readonly IReadOnlyList<ActionRow> Actions = [new("Update Project...", "pull", "git.pull"), new("Fetch", "refresh", "git.fetch")];

    private static readonly IReadOnlyList<GitRef> Refs =
    [
        Local("main", head: true), Local("feature/login"), Local("feature/search"), Local("fix/crash"), Local("docs"), Local("release"), Local("spike"),
        new("refs/remotes/origin/main", RefKind.RemoteBranch, "a"), new("refs/remotes/origin/feature/login", RefKind.RemoteBranch, "b"),
        new("refs/tags/v1.0", RefKind.Tag, "c") { Date = DateTimeOffset.UnixEpoch },
    ];

    [Fact]
    public void ListsActionsThenRecentLocalRemoteAndTags()
    {
        var rows = BranchRows.Build(Actions, Refs, ["main", "fix/crash", "docs"], null);

        Assert.Equal(
            ["Update Project...", "Fetch", "#Recent", "fix/crash", "docs", "#Local", "main", "docs", "feature/login", "feature/search", "fix/crash", "release", "spike",
                "#Remote", "origin/feature/login", "origin/main", "#Tags", "v1.0"],
            rows.Select(Describe));
    }

    [Fact]
    public void FiltersEveryGroupAndLeavesOutRecent()
    {
        var rows = BranchRows.Build(Actions, Refs, ["fix/crash"], "LOGIN");

        Assert.Equal(["#Local", "feature/login", "#Remote", "origin/feature/login"], rows.Select(Describe));
    }

    private static string Describe(BranchRow row) => row switch
    {
        ActionRow action => action.Title,
        HeaderRow header => "#" + header.Title,
        RefRow reference => reference.Ref.Name,
        _ => "?",
    };

    private static GitRef Local(string name, bool head = false) => new("refs/heads/" + name, RefKind.LocalBranch, name) { IsHead = head };
}
