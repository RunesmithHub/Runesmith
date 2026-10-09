using System.Globalization;
using System.Text;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Which characters may start and continue a Java identifier.</summary>
internal static class CharacterClasses
{
    public static bool IsAsciiIdentifierStart(char c) => char.IsAsciiLetter(c) || c is '_' or '$';

    public static bool IsAsciiIdentifierPart(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '$';

    public static bool IsIdentifierStart(Rune rune) =>
        rune.IsAscii
            ? IsAsciiIdentifierStart((char)rune.Value)
            : Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber
                or UnicodeCategory.CurrencySymbol or UnicodeCategory.ConnectorPunctuation;

    public static bool IsIdentifierPart(Rune rune)
    {
        if (rune.IsAscii)
            return IsAsciiIdentifierPart((char)rune.Value) || rune.Value is <= 0x08 or (>= 0x0E and <= 0x1B) or 0x7F;

        return IsIdentifierStart(rune)
            || rune.Value is >= 0x80 and <= 0x9F
            || Rune.GetUnicodeCategory(rune) is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;
    }
}
