using System.Text;

namespace Runesmith.Localization;

/// <summary>Pseudo-localization: text accented and about 35 percent longer, in brackets, so text that skips localization and layouts that
/// cut text off stand out.</summary>
/// <remarks>The text of a message is accented and lengthened; the values of its variables, such as file names, stay as they are.</remarks>
internal static class PseudoLocalization
{
    private const double Growth = 0.35;
    private const string Plain = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Accented = "àƀçðéƒĝĥîĵķļɱñôþǫŕšţûṽŵẋýžÀƁÇÐÉƑĜĤÎĴĶĻṀÑÔÞǪŔŠŢÛṼŴẊÝŽ";

    /// <summary>Accents a message's text and lengthens it by repeating vowels.</summary>
    public static string Transform(string text)
    {
        var extra = (int)Math.Ceiling(text.Count(char.IsLetter) * Growth);
        var result = new StringBuilder(text.Length + extra);
        foreach (var character in text)
        {
            var index = Plain.IndexOf(character, StringComparison.Ordinal);
            var accented = index < 0 ? character : Accented[index];
            result.Append(accented);
            if (extra > 0 && "aeiouyAEIOUY".Contains(character, StringComparison.Ordinal))
            {
                result.Append(accented);
                extra--;
            }
        }

        return result.ToString();
    }

    /// <summary>Puts a message in brackets, so where it is cut off shows.</summary>
    public static string Wrap(string text) => "[" + text + "]";
}
