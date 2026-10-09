namespace Runesmith.Git.Git;

/// <summary>How a commit is made.</summary>
/// <param name="Amend">Replace the last commit instead of adding one.</param>
/// <param name="Signoff">Add a <c>Signed-off-by</c> line.</param>
/// <param name="NoVerify">Skip the pre-commit and commit-msg hooks.</param>
internal sealed record CommitOptions(bool Amend = false, bool Signoff = false, bool NoVerify = false);

/// <summary>How a pull brings in the upstream's commits.</summary>
internal enum PullStrategy
{
    /// <summary>As the user's Git configuration says (<c>pull.rebase</c>), merging when it says nothing.</summary>
    UserConfig,

    /// <summary>Rebase the local commits on the upstream's.</summary>
    Rebase,

    /// <summary>Merge the upstream into the branch.</summary>
    Merge,
}

/// <summary>How far a reset goes.</summary>
internal enum ResetMode
{
    /// <summary>Move the branch only; the changes stay staged.</summary>
    Soft,

    /// <summary>Move the branch and the index; the changes stay in the working tree.</summary>
    Mixed,

    /// <summary>Move the branch, the index and the working tree; the changes are lost.</summary>
    Hard,
}

/// <summary>A multi-step operation that stopped part way, such as at merge conflicts.</summary>
internal enum OngoingOperation
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

/// <summary>A stash entry.</summary>
/// <param name="Index">Its position, 0 for the newest, as in <c>stash@{0}</c>.</param>
/// <param name="Sha">The stash commit.</param>
/// <param name="Message">Its description, such as <c>On main: work in progress</c>.</param>
/// <param name="Date">When it was made.</param>
internal sealed record StashEntry(int Index, string Sha, string Message, DateTimeOffset Date)
{
    /// <summary>Gets the name Git knows it by, such as <c>stash@{0}</c>.</summary>
    public string Name => $"stash@{{{Index}}}";
}

/// <summary>What a push did.</summary>
/// <param name="Remote">The remote pushed to.</param>
/// <param name="Branch">The branch on the remote.</param>
/// <param name="Commits">How many commits went, as far as was known before the push; -1 when unknown, such as for a new branch.</param>
internal sealed record PushResult(string Remote, string Branch, int Commits);
