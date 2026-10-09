namespace Runesmith.Git.Git;

/// <summary>The failures of Git commands that Runesmith recognizes, so it can offer a way out, such as stashing before a checkout.</summary>
internal enum GitErrorKind
{
    /// <summary>Any other failure; Git's message says what happened.</summary>
    Other,

    /// <summary>A checkout, merge or pull would overwrite local changes or untracked files.</summary>
    LocalChangesWouldBeOverwritten,

    /// <summary>A branch to delete has commits that are not merged anywhere.</summary>
    BranchNotFullyMerged,

    /// <summary>The remote has commits the push would lose.</summary>
    PushRejected,

    /// <summary>A forced push found the remote moved since the last fetch.</summary>
    StaleRemote,

    /// <summary>The current branch has no upstream to pull from or push to.</summary>
    NoUpstream,

    /// <summary>A merge, rebase, cherry-pick or revert stopped at conflicts.</summary>
    Conflict,

    /// <summary>Nothing is staged to commit.</summary>
    NothingToCommit,

    /// <summary>The remote refused the credentials, or there were none.</summary>
    AuthenticationFailed,
}

/// <summary>Recognizes Git's failures from what it printed; Git's messages are in English because Runesmith runs it with <c>LC_ALL=C</c>.</summary>
internal static class GitErrors
{
    /// <summary>Finds what kind of failure Git's output describes.</summary>
    public static GitErrorKind Classify(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Has(text, "would be overwritten by") || Has(text, "Please commit your changes or stash them") || Has(text, "untracked working tree files would be"))
            return GitErrorKind.LocalChangesWouldBeOverwritten;
        if (Has(text, "is not fully merged"))
            return GitErrorKind.BranchNotFullyMerged;
        if (Has(text, "stale info"))
            return GitErrorKind.StaleRemote;
        if (Has(text, "[rejected]") || Has(text, "Updates were rejected") || Has(text, "non-fast-forward"))
            return GitErrorKind.PushRejected;
        if (Has(text, "has no upstream branch") || Has(text, "no tracking information"))
            return GitErrorKind.NoUpstream;
        if (Has(text, "CONFLICT") || Has(text, "Automatic merge failed") || Has(text, "could not apply") || Has(text, "after resolving the conflicts")
            || Has(text, "you need to resolve your current index first"))
            return GitErrorKind.Conflict;
        if (Has(text, "nothing to commit") || Has(text, "no changes added to commit") || Has(text, "nothing added to commit"))
            return GitErrorKind.NothingToCommit;
        if (Has(text, "Authentication failed") || Has(text, "could not read Username") || Has(text, "Permission denied (publickey)")
            || Has(text, "terminal prompts disabled") || Has(text, "Invalid username or password"))
            return GitErrorKind.AuthenticationFailed;
        return GitErrorKind.Other;
    }

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.Ordinal);
}
