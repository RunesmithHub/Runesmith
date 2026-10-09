using System.Globalization;
using System.Text.RegularExpressions;

namespace Runesmith.Git.Clone;

/// <summary>Where a clone is, read from a line of Git's progress output.</summary>
/// <param name="Text">The step for people, such as "Receiving objects: 45% (450/1000), 1.20 MiB | 2.00 MiB/s".</param>
/// <param name="Fraction">How much of the whole clone is done, from 0 to 1, or null when the line says nothing about it.</param>
internal sealed record CloneStep(string Text, double? Fraction);

/// <summary>Turns <c>git clone --progress</c> output into one progress value for the whole clone.</summary>
internal static partial class CloneProgress
{
    // How much of the whole clone each phase stands for; receiving takes the longest by far.
    private static readonly (string Phase, double Start, double Weight)[] Phases =
    [
        ("Counting objects", 0, 0.02),
        ("Compressing objects", 0.02, 0.03),
        ("Receiving objects", 0.05, 0.75),
        ("Resolving deltas", 0.80, 0.12),
        ("Updating files", 0.92, 0.08),
    ];

    /// <summary>Reads one line; lines without a known phase keep their text and give no fraction.</summary>
    public static CloneStep Parse(string line)
    {
        var text = line.StartsWith("remote: ", StringComparison.Ordinal) ? line[8..] : line;
        text = text.Trim();
        var match = PercentPattern().Match(text);
        if (match.Success)
        {
            var phase = match.Groups[1].Value.Trim();
            var percent = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) / 100;
            foreach (var (name, start, weight) in Phases)
            {
                if (phase.Equals(name, StringComparison.Ordinal))
                    return new CloneStep(text, Math.Clamp(start + (weight * percent), 0, 1));
            }
        }

        return new CloneStep(text, null);
    }

    [GeneratedRegex(@"^([A-Za-z ]+):\s+(\d{1,3})%")]
    private static partial Regex PercentPattern();
}
