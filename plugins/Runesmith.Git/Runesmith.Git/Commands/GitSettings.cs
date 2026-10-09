using System.Composition;
using Runesmith.Git.Git;
using Runesmith.Sdk.Settings;

namespace Runesmith.Git.Commands;

/// <summary>The Git settings: how pulls bring in commits, how often to fetch, and the commit subject's length hint.</summary>
[Export(typeof(ISettingContributor))]
internal sealed class GitSettings : ISettingContributor
{
    /// <summary>How Update Project pulls: <c>gitConfig</c>, <c>rebase</c> or <c>merge</c>.</summary>
    public const string PullStrategy = "git.pullStrategy";

    /// <summary>Minutes between automatic fetches; 0 turns them off.</summary>
    public const string AutoFetchMinutes = "git.autoFetchMinutes";

    /// <summary>The length past which the commit message box points out a long subject.</summary>
    public const string SubjectLength = "git.subjectLengthHint";

    private const string Category = "Git";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(PullStrategy, "Pull strategy", Category, "gitConfig")
        {
            Description = "How Update Project brings in the upstream's commits: as your Git configuration's pull.rebase says (merging when it says nothing), by rebasing your commits on them, or with a merge.",
            Choices = ["gitConfig", "rebase", "merge"],
        },
        new(AutoFetchMinutes, "Fetch automatically every (minutes)", Category, 0)
        {
            Description = "Fetches every remote in the background, so the branch widget shows what there is to pull; 0 turns it off.",
            Minimum = 0,
            Maximum = 1440,
        },
        new(SubjectLength, "Commit subject length hint", Category, 72)
        {
            Description = "The commit message box shows the subject's length once it is longer than this.",
            Minimum = 20,
            Maximum = 200,
        },
    ];

    /// <summary>Reads the pull strategy setting's value.</summary>
    public static PullStrategy ParsePullStrategy(string? value) => value switch
    {
        "rebase" => Git.PullStrategy.Rebase,
        "merge" => Git.PullStrategy.Merge,
        _ => Git.PullStrategy.UserConfig,
    };
}
