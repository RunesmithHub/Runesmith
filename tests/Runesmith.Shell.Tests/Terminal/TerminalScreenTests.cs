using System.Text;
using Runesmith.Shell.Terminal;

namespace Runesmith.Shell.Tests.Terminal;

public sealed class TerminalScreenTests
{
    private const string Esc = "\u001b";

    [Fact]
    public void TextAndLineBreaksFillTheRows()
    {
        var screen = Screen("hello\r\nworld");

        Assert.Equal("hello", screen.RowText(0));
        Assert.Equal("world", screen.RowText(1));
        Assert.Equal((1, 5), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void ALineFeedAloneKeepsTheColumn()
    {
        var screen = Screen("ab\ncd");

        Assert.Equal("ab", screen.RowText(0));
        Assert.Equal("  cd", screen.RowText(1));
    }

    [Fact]
    public void TextWrapsAtTheRightMarginOnlyWhenTheNextCharacterComes()
    {
        var screen = Screen("0123456789", columns: 10);

        Assert.Equal((0, 9), (screen.CursorRow, screen.CursorColumn));
        Assert.False(screen.Row(0).IsWrapped);

        screen.Feed("x");

        Assert.Equal("x", screen.RowText(1));
        Assert.True(screen.Row(0).IsWrapped);
        Assert.Equal("0123456789x", screen.GetAllText());
    }

    [Fact]
    public void ASpaceAndABackspaceAtTheMarginWrapLikeAPagerExpects()
    {
        var screen = Screen($"0123456789{Esc}[m \b{Esc}[33m{Esc}[mnext", columns: 10);

        Assert.Equal("0123456789", screen.RowText(0));
        Assert.Equal("next", screen.RowText(1));
    }

    [Fact]
    public void ACarriageReturnAtTheMarginCancelsThePendingWrap()
    {
        var screen = Screen("0123456789\rA", columns: 10);

        Assert.Equal("A123456789", screen.RowText(0));
        Assert.Equal(0, screen.CursorRow);
    }

    [Fact]
    public void WithoutAutoWrapTheLastColumnIsOverwritten()
    {
        var screen = Screen($"{Esc}[?7l0123456789XYZ", columns: 10);

        Assert.Equal("012345678Z", screen.RowText(0));
        Assert.Equal(0, screen.CursorRow);
    }

    [Theory]
    [InlineData("31", 1, false)]
    [InlineData("91", 9, false)]
    [InlineData("1;34", 4, true)]
    [InlineData("38;5;196", 196, false)]
    [InlineData("38:5:208", 208, false)]
    public void SgrSetsIndexedForegroundColors(string parameters, int index, bool bold)
    {
        var screen = Screen($"{Esc}[{parameters}mX");

        var style = screen.Cell(0, 0).Style;
        Assert.True(style.Foreground.IsIndexed);
        Assert.Equal(index, style.Foreground.Index);
        Assert.Equal(bold, style.Flags.HasFlag(CellFlags.Bold));
    }

    [Theory]
    [InlineData("38;2;10;20;30")]
    [InlineData("38:2::10:20:30")]
    [InlineData("38:2:10:20:30")]
    public void SgrSetsTrueColors(string parameters)
    {
        var screen = Screen($"{Esc}[{parameters};48;2;1;2;3mX");

        var style = screen.Cell(0, 0).Style;
        Assert.Equal(TerminalColor.Rgb(10, 20, 30), style.Foreground);
        Assert.Equal(TerminalColor.Rgb(1, 2, 3), style.Background);
    }

    [Fact]
    public void SgrBackgroundsAndResets()
    {
        var screen = Screen($"{Esc}[42;4;7mA{Esc}[24;27mB{Esc}[49;39mC{Esc}[1;3;9mD{Esc}[0mE{Esc}[103mF");

        Assert.Equal(TerminalColor.Indexed(2), screen.Cell(0, 0).Style.Background);
        Assert.Equal(CellFlags.Underline | CellFlags.Inverse, screen.Cell(0, 0).Style.Flags);
        Assert.Equal(CellFlags.None, screen.Cell(0, 1).Style.Flags);
        Assert.Equal(TerminalColor.Indexed(2), screen.Cell(0, 1).Style.Background);
        Assert.Equal(CellStyle.Default, screen.Cell(0, 2).Style);
        Assert.Equal(CellFlags.Bold | CellFlags.Italic | CellFlags.Strikethrough, screen.Cell(0, 3).Style.Flags);
        Assert.Equal(CellStyle.Default, screen.Cell(0, 4).Style);
        Assert.Equal(TerminalColor.Indexed(11), screen.Cell(0, 5).Style.Background);
    }

    [Fact]
    public void SgrWithoutParametersResets()
    {
        var screen = Screen($"{Esc}[1;31mA{Esc}[mB");

        Assert.Equal(CellStyle.Default, screen.Cell(0, 1).Style);
    }

    [Fact]
    public void CursorPositionIsCountedFromOne()
    {
        var screen = Screen($"{Esc}[3;5HX{Esc}[HY");

        Assert.Equal("    X", screen.RowText(2));
        Assert.Equal("Y", screen.RowText(0));
    }

    [Fact]
    public void RelativeMovesStopAtTheEdges()
    {
        var screen = Screen($"{Esc}[5;5H{Esc}[2A{Esc}[3C{Esc}[B{Esc}[1D");
        Assert.Equal((3, 6), (screen.CursorRow, screen.CursorColumn));

        screen.Feed($"{Esc}[99A{Esc}[99D");
        Assert.Equal((0, 0), (screen.CursorRow, screen.CursorColumn));

        screen.Feed($"{Esc}[99B{Esc}[99C");
        Assert.Equal((screen.Rows - 1, screen.Columns - 1), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void ColumnAndLineAbsoluteAndNextAndPreviousLine()
    {
        var screen = Screen($"{Esc}[4;10H{Esc}[3G");
        Assert.Equal((3, 2), (screen.CursorRow, screen.CursorColumn));

        screen.Feed($"{Esc}[7d");
        Assert.Equal((6, 2), (screen.CursorRow, screen.CursorColumn));

        screen.Feed($"{Esc}[2E");
        Assert.Equal((8, 0), (screen.CursorRow, screen.CursorColumn));

        screen.Feed($"{Esc}[5C{Esc}[3F");
        Assert.Equal((5, 0), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void SaveAndRestoreTheCursorWithItsStyle()
    {
        var screen = Screen($"{Esc}[2;3H{Esc}[31m{Esc}7{Esc}[0m{Esc}[10;10H{Esc}8X{Esc}[5;5H{Esc}[s{Esc}[1;1H{Esc}[uY");

        Assert.Equal('X', screen.Cell(1, 2).Rune);
        Assert.Equal(TerminalColor.Indexed(1), screen.Cell(1, 2).Style.Foreground);
        Assert.Equal('Y', screen.Cell(4, 4).Rune);
    }

    [Fact]
    public void EraseInLine()
    {
        var screen = Screen($"abcdef\r\nabcdef\r\nabcdef{Esc}[1;3H{Esc}[K{Esc}[2;3H{Esc}[1K{Esc}[3;3H{Esc}[2K");

        Assert.Equal("ab", screen.RowText(0));
        Assert.Equal("   def", screen.RowText(1));
        Assert.Equal("", screen.RowText(2));
    }

    [Fact]
    public void EraseInDisplay()
    {
        var screen = Screen($"aaa\r\nbbb\r\nccc{Esc}[2;2H{Esc}[J");
        Assert.Equal(["aaa", "b", ""], Rows(screen, 3));

        screen = Screen($"aaa\r\nbbb\r\nccc{Esc}[2;2H{Esc}[1J");
        Assert.Equal(["", "  b", "ccc"], Rows(screen, 3));

        screen = Screen($"aaa\r\nbbb\r\nccc{Esc}[2J");
        Assert.Equal(["", "", ""], Rows(screen, 3));
        Assert.Equal((2, 3), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void EraseInDisplayThreeClearsTheScrollback()
    {
        var screen = Screen(string.Join("\r\n", Enumerable.Range(0, 30).Select(i => $"line {i}")), rows: 5);
        Assert.Equal(25, screen.ScrollbackCount);

        screen.Feed($"{Esc}[3J");

        Assert.Equal(0, screen.ScrollbackCount);
        Assert.Equal("line 29", screen.RowText(4));
    }

    [Fact]
    public void ErasedCellsTakeTheCurrentBackground()
    {
        var screen = Screen($"{Esc}[44m{Esc}[2K{Esc}[0mX");

        Assert.Equal(TerminalColor.Indexed(4), screen.Cell(0, 5).Style.Background);
        Assert.Equal(TerminalColor.Default, screen.Cell(0, 0).Style.Background);
    }

    [Fact]
    public void EraseCharactersDoesNotMoveTheRest()
    {
        var screen = Screen($"abcdef{Esc}[1;2H{Esc}[3X");

        Assert.Equal("a   ef", screen.RowText(0));
    }

    [Fact]
    public void InsertAndDeleteCharacters()
    {
        var screen = Screen($"abcdef{Esc}[1;3H{Esc}[2@XY");
        Assert.Equal("abXYcdef", screen.RowText(0));

        screen.Feed($"{Esc}[1;2H{Esc}[3P");
        Assert.Equal("acdef", screen.RowText(0));
    }

    [Fact]
    public void InsertModeShiftsTheLine()
    {
        var screen = Screen($"abc{Esc}[1;2H{Esc}[4hXY{Esc}[4lZ");

        Assert.Equal("aXYZc", screen.RowText(0));
    }

    [Fact]
    public void InsertAndDeleteLines()
    {
        var screen = Screen($"1\r\n2\r\n3\r\n4{Esc}[2;1H{Esc}[L", rows: 4);
        Assert.Equal(["1", "", "2", "3"], Rows(screen, 4));

        screen.Feed($"{Esc}[1;1H{Esc}[2M");
        Assert.Equal(["2", "3", "", ""], Rows(screen, 4));
    }

    [Fact]
    public void AScrollRegionScrollsOnlyItsLines()
    {
        var screen = Screen($"top\r\na\r\nb\r\nc\r\nbottom{Esc}[2;4r{Esc}[4;1H\nd", rows: 5);

        Assert.Equal(["top", "b", "c", "d", "bottom"], Rows(screen, 5));
        Assert.Equal(0, screen.ScrollbackCount);
    }

    [Fact]
    public void ReverseIndexAtTheTopOfTheRegionScrollsItDown()
    {
        var screen = Screen($"top\r\na\r\nb\r\nc\r\nbottom{Esc}[2;4r{Esc}[2;1H{Esc}Mz", rows: 5);

        Assert.Equal(["top", "z", "a", "b", "bottom"], Rows(screen, 5));
    }

    [Fact]
    public void ScrollUpAndDownMoveTheRegion()
    {
        var screen = Screen($"1\r\n2\r\n3\r\n4{Esc}[2S", rows: 4);
        Assert.Equal(["3", "4", "", ""], Rows(screen, 4));

        screen.Feed($"{Esc}[1T");
        Assert.Equal(["", "3", "4", ""], Rows(screen, 4));
    }

    [Fact]
    public void OriginModeCountsFromTheRegion()
    {
        var screen = Screen($"{Esc}[3;5r{Esc}[?6h{Esc}[1;1HX{Esc}[9;1HY", rows: 6);

        Assert.Equal("X", screen.RowText(2));
        Assert.Equal("Y", screen.RowText(4));
    }

    [Fact]
    public void LinesThatScrollOffTheTopGoToTheScrollback()
    {
        var screen = Screen(string.Join("\r\n", Enumerable.Range(1, 8).Select(i => $"line {i}")), rows: 3);

        Assert.Equal(5, screen.ScrollbackCount);
        Assert.Equal("line 1", screen.LineAt(0).GetText());
        Assert.Equal("line 6", screen.RowText(0));
        Assert.Equal("line 8", screen.RowText(2));
    }

    [Fact]
    public void AFullScrollbackDropsItsOldestLines()
    {
        var screen = new TerminalScreen(20, 2, scrollbackLines: 3);
        screen.Feed(string.Join("\r\n", Enumerable.Range(1, 10).Select(i => $"line {i}")));

        Assert.Equal(3, screen.ScrollbackCount);
        Assert.Equal(5, screen.DroppedLines);
        Assert.Equal("line 6", screen.LineAt(0).GetText());
        Assert.Equal(new TerminalPoint(5, 0), screen.PointAt(0, 0));
    }

    [Fact]
    public void TheAlternateScreenKeepsThePrimaryAndTheCursor()
    {
        var screen = Screen($"shell${Esc}[?1049h{Esc}[Hfull screen{Esc}[5;5Hmore", rows: 6);
        Assert.True(screen.IsAlternateScreen);
        Assert.Equal("full screen", screen.RowText(0));

        screen.Feed($"{Esc}[?1049l");

        Assert.False(screen.IsAlternateScreen);
        Assert.Equal("shell$", screen.RowText(0));
        Assert.Equal((0, 6), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void TheAlternateScreenStartsEmptyAndKeepsNoScrollback()
    {
        var screen = Screen($"before{Esc}[?1049h", rows: 3);
        Assert.Equal("", screen.RowText(0));

        screen.Feed("1\r\n2\r\n3\r\n4\r\n5");

        Assert.Equal(0, screen.ScrollbackCount);
        Assert.Equal(["3", "4", "5"], Rows(screen, 3));
    }

    [Fact]
    public void WideCharactersTakeTwoCells()
    {
        var screen = Screen("a中b");

        Assert.Equal(2, screen.Cell(0, 1).Width);
        Assert.Equal(0, screen.Cell(0, 2).Width);
        Assert.Equal('b', screen.Cell(0, 3).Rune);
        Assert.Equal(4, screen.CursorColumn);
        Assert.Equal("a中b", screen.RowText(0));
    }

    [Fact]
    public void AWideCharacterAtTheLastColumnWrapsWhole()
    {
        var screen = Screen("123456789中", columns: 10);

        Assert.Equal("123456789", screen.RowText(0));
        Assert.Equal("中", screen.RowText(1));
        Assert.True(screen.Row(0).IsWrapped);
    }

    [Fact]
    public void OverwritingHalfAWideCharacterClearsTheOtherHalf()
    {
        var screen = Screen($"中文{Esc}[1;2Hx");

        Assert.Equal(" x文", screen.RowText(0));
        Assert.Equal(1, screen.Cell(0, 0).Width);
    }

    [Fact]
    public void EmojiAreWide()
    {
        var screen = Screen("\U0001F600!");

        Assert.Equal(2, screen.Cell(0, 0).Width);
        Assert.Equal('!', screen.Cell(0, 2).Rune);
    }

    [Fact]
    public void CombiningMarksJoinTheCharacterBefore()
    {
        var screen = Screen("éx");

        Assert.Equal("é", screen.Cell(0, 0).Text);
        Assert.Equal('x', screen.Cell(0, 1).Rune);
    }

    [Fact]
    public void TabsStopEveryEightColumnsAndCanBeSet()
    {
        var screen = Screen("a\tb");
        Assert.Equal('b', screen.Cell(0, 8).Rune);

        screen.Feed($"\r{Esc}[3g{Esc}[1;4H{Esc}H\r\tc");
        Assert.Equal('c', screen.Cell(0, 3).Rune);
    }

    [Fact]
    public void RepeatPrintsTheLastCharacterAgain()
    {
        var screen = Screen($"-{Esc}[4b");

        Assert.Equal("-----", screen.RowText(0));
    }

    [Fact]
    public void DecSpecialGraphicsDrawLines()
    {
        var screen = Screen($"{Esc}(0lqk{Esc}(B q");

        Assert.Equal("┌─┐ q", screen.RowText(0));
    }

    [Fact]
    public void TheScreenAlignmentTestFillsTheScreen()
    {
        var screen = Screen($"{Esc}#8", columns: 4, rows: 2);

        Assert.Equal(["EEEE", "EEEE"], Rows(screen, 2));
    }

    [Fact]
    public void StatusReportsAnswer()
    {
        var replies = new List<string>();
        var screen = new TerminalScreen(80, 24) { Reply = replies.Add };

        screen.Feed($"{Esc}[3;7H{Esc}[6n{Esc}[5n{Esc}[c{Esc}[>c{Esc}[18t");

        Assert.Equal([$"{Esc}[3;7R", $"{Esc}[0n", $"{Esc}[?62;22c", $"{Esc}[>1;10;0c", $"{Esc}[8;24;80t"], replies);
    }

    [Fact]
    public void OperatingSystemCommandsSetTheTitleAndTheFolder()
    {
        var screen = Screen($"{Esc}]0;my title\u0007{Esc}]7;file://host/home/me/my%20project{Esc}\\{Esc}]133;D;3\u0007after");

        Assert.Equal("my title", screen.Title);
        Assert.Equal("/home/me/my project", screen.WorkingDirectory);
        Assert.Equal(3, screen.LastExitCode);
        Assert.Equal("after", screen.RowText(0));
    }

    [Fact]
    public void ColorQueriesAreAnswered()
    {
        var replies = new List<string>();
        var screen = new TerminalScreen(80, 24) { Reply = replies.Add, ColorQuery = which => which == 11 ? "rgb:1e1e/1f1f/2424" : null };

        screen.Feed($"{Esc}]11;?\u0007{Esc}]10;?\u0007");

        Assert.Equal([$"{Esc}]11;rgb:1e1e/1f1f/2424{Esc}\\"], replies);
    }

    [Fact]
    public void SequencesAndCharactersSplitAcrossReadsStillWork()
    {
        var screen = new TerminalScreen(80, 24);
        var bytes = Encoding.UTF8.GetBytes($"{Esc}[31m中{Esc}[0mé");

        foreach (var b in bytes)
            screen.Feed([b]);

        Assert.Equal("中é", screen.RowText(0));
        Assert.Equal(TerminalColor.Indexed(1), screen.Cell(0, 0).Style.Foreground);
        Assert.Equal(CellStyle.Default, screen.Cell(0, 2).Style);
    }

    [Fact]
    public void UnknownAndPrivateSequencesAreIgnored()
    {
        var screen = Screen($"a{Esc}[?2026h{Esc}[>4;1m{Esc}P1$r0m{Esc}\\{Esc}[12;34;56zb");

        Assert.Equal("ab", screen.RowText(0));
    }

    [Fact]
    public void ModesAreTracked()
    {
        var screen = Screen($"{Esc}[?1h{Esc}[?25l{Esc}[?2004h{Esc}[?1002h{Esc}[?1006h{Esc}[5 q{Esc}=");

        Assert.True(screen.ApplicationCursorKeys);
        Assert.False(screen.CursorVisible);
        Assert.True(screen.BracketedPaste);
        Assert.Equal(MouseTracking.Drag, screen.MouseTracking);
        Assert.True(screen.SgrMouse);
        Assert.Equal(CursorShape.Bar, screen.CursorShape);
        Assert.True(screen.ApplicationKeypad);

        screen.Feed($"{Esc}c");

        Assert.False(screen.ApplicationCursorKeys);
        Assert.True(screen.CursorVisible);
        Assert.Equal(MouseTracking.None, screen.MouseTracking);
    }

    [Fact]
    public void ShrinkingMovesTopLinesToTheScrollbackAndGrowingBringsThemBack()
    {
        var screen = Screen("1\r\n2\r\n3\r\n4\r\n5", rows: 5);

        screen.Resize(10, 3);
        Assert.Equal(["3", "4", "5"], Rows(screen, 3));
        Assert.Equal(2, screen.ScrollbackCount);
        Assert.Equal(2, screen.CursorRow);

        screen.Resize(10, 5);
        Assert.Equal(["1", "2", "3", "4", "5"], Rows(screen, 5));
        Assert.Equal(0, screen.ScrollbackCount);
        Assert.Equal(4, screen.CursorRow);
    }

    [Fact]
    public void ShrinkingDropsEmptyLinesBelowTheCursorFirst()
    {
        var screen = Screen("1\r\n2", rows: 6);

        screen.Resize(10, 3);

        Assert.Equal(0, screen.ScrollbackCount);
        Assert.Equal(["1", "2", ""], Rows(screen, 3));
    }

    [Fact]
    public void NarrowingCutsLinesAndKeepsTheCursorInside()
    {
        var screen = Screen("abcdefghij", columns: 20);

        screen.Resize(5, 24);

        Assert.Equal("abcde", screen.RowText(0));
        Assert.Equal(4, screen.CursorColumn);
        Assert.Equal(5, screen.Columns);
    }

    [Fact]
    public void ResizingResetsTheScrollRegion()
    {
        var screen = Screen($"{Esc}[2;4r", rows: 10);

        screen.Resize(80, 12);

        Assert.Equal((0, 11), (screen.ScrollTop, screen.ScrollBottom));
    }

    [Fact]
    public void SelectedTextJoinsWrappedLines()
    {
        var screen = Screen("0123456789abc\r\nnext", columns: 10);

        var text = screen.GetText(screen.PointAt(0, 2), screen.PointAt(2, 3));

        Assert.Equal("23456789abc\nnex", text);
    }

    private static TerminalScreen Screen(string text, int columns = 80, int rows = 24)
    {
        var screen = new TerminalScreen(columns, rows);
        screen.Feed(text);
        return screen;
    }

    private static string[] Rows(TerminalScreen screen, int count) => [.. Enumerable.Range(0, count).Select(screen.RowText)];
}
