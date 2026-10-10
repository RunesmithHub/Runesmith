using System.Text;

namespace Runesmith.Shell.Terminal;

/// <summary>What a <see cref="VtParser"/> found in a terminal's output.</summary>
internal interface IVtHandler
{
    /// <summary>A character to show at the cursor.</summary>
    void Print(int codePoint);

    /// <summary>A C0 control character, such as a line feed or a backspace.</summary>
    void Execute(int control);

    /// <summary>A control sequence, <c>ESC [</c>, such as <c>ESC [ 1 ; 31 m</c>.</summary>
    /// <param name="parameters">The numbers, with -1 for one left out.</param>
    /// <param name="marker">The private marker before the numbers, such as <c>?</c>, or 0.</param>
    /// <param name="intermediates">The characters between the numbers and the final one, such as a space.</param>
    void Csi(VtParameters parameters, char marker, string intermediates, char final);

    /// <summary>An escape sequence that is not a control sequence, such as <c>ESC 7</c> or <c>ESC ( 0</c>.</summary>
    void Escape(string intermediates, char final);

    /// <summary>An operating system command, <c>ESC ]</c>, such as the window title.</summary>
    void OperatingSystemCommand(string text);
}

/// <summary>The numbers of a control sequence; a number given after a colon is a sub-parameter of the one before it.</summary>
internal sealed class VtParameters
{
    private const int Limit = 32;
    private readonly int[] values = new int[Limit];
    private readonly bool[] sub = new bool[Limit];

    public int Count { get; private set; }

    public int this[int index] => index < Count ? values[index] : -1;

    /// <summary>Gets whether the number at <paramref name="index"/> followed a colon.</summary>
    public bool IsSub(int index) => index < Count && sub[index];

    /// <summary>Gets a number, or <paramref name="fallback"/> when it is left out or 0 and 0 means the default.</summary>
    public int Get(int index, int fallback, bool zeroIsDefault = true)
    {
        var value = this[index];
        return value < 0 || (zeroIsDefault && value == 0) ? fallback : value;
    }

    internal void Clear()
    {
        Count = 0;
        values[0] = -1;
        sub[0] = false;
    }

    internal void Digit(int digit)
    {
        if (Count == 0)
            Count = 1;
        ref var value = ref values[Count - 1];
        value = value < 0 ? digit : Math.Min(65535, (value * 10) + digit);
    }

    internal void Next(bool isSub)
    {
        if (Count == 0)
            Count = 1;
        if (Count == Limit)
            return;

        values[Count] = -1;
        sub[Count] = isSub;
        Count++;
    }

    internal void Begin()
    {
        if (Count == 0)
        {
            Count = 1;
            values[0] = -1;
            sub[0] = false;
        }
    }
}

/// <summary>Splits a terminal's output into characters, controls and sequences, following the state machine of DEC's VT500 series that
/// xterm and most terminals use. Sequences may be split across calls.</summary>
internal sealed class VtParser(IVtHandler handler)
{
    private const int MaximumStringLength = 65536;

    private readonly VtParameters parameters = new();
    private readonly StringBuilder intermediates = new();
    private readonly StringBuilder text = new();
    private State state;
    private char marker;

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        CsiIgnore,
        Osc,
        IgnoredString,
    }

    /// <summary>Parses characters; a surrogate pair is passed as its two halves in order.</summary>
    public void Feed(ReadOnlySpan<char> characters)
    {
        for (var i = 0; i < characters.Length; i++)
        {
            var c = characters[i];
            if (char.IsHighSurrogate(c) && i + 1 < characters.Length && char.IsLowSurrogate(characters[i + 1]))
            {
                Feed(char.ConvertToUtf32(c, characters[i + 1]));
                i++;
            }
            else
                Feed(c);
        }
    }

    /// <summary>Parses one code point.</summary>
    public void Feed(int c)
    {
        if (c is 0x18 or 0x1A)
        {
            state = State.Ground;
            return;
        }

        if (c == 0x1B)
        {
            if (state == State.Osc)
                DispatchOsc();
            BeginEscape();
            return;
        }

        switch (state)
        {
            case State.Ground:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c is not (0x7F or (>= 0x80 and <= 0x9F)))
                    handler.Print(c);
                break;

            case State.Escape:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c <= 0x2F)
                {
                    intermediates.Append((char)c);
                    state = State.EscapeIntermediate;
                }
                else if (c == '[')
                {
                    parameters.Clear();
                    marker = '\0';
                    state = State.CsiEntry;
                }
                else if (c == ']')
                {
                    text.Clear();
                    state = State.Osc;
                }
                else if (c is 'P' or 'X' or '^' or '_')
                    state = State.IgnoredString;
                else if (c <= 0x7E)
                {
                    state = State.Ground;
                    if (c != '\\')
                        handler.Escape(intermediates.ToString(), (char)c);
                }
                else
                    state = State.Ground;
                break;

            case State.EscapeIntermediate:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c <= 0x2F)
                    intermediates.Append((char)c);
                else
                {
                    state = State.Ground;
                    if (c <= 0x7E)
                        handler.Escape(intermediates.ToString(), (char)c);
                }

                break;

            case State.CsiEntry:
            case State.CsiParam:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c is >= '0' and <= '9')
                {
                    parameters.Digit(c - '0');
                    state = State.CsiParam;
                }
                else if (c is ';' or ':')
                {
                    parameters.Next(isSub: c == ':');
                    state = State.CsiParam;
                }
                else if (c is >= 0x3C and <= 0x3F)
                {
                    if (state == State.CsiEntry)
                    {
                        marker = (char)c;
                        state = State.CsiParam;
                    }
                    else
                        state = State.CsiIgnore;
                }
                else if (c <= 0x2F)
                {
                    intermediates.Append((char)c);
                    state = State.CsiIntermediate;
                }
                else
                    DispatchCsi(c);
                break;

            case State.CsiIntermediate:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c <= 0x2F)
                    intermediates.Append((char)c);
                else if (c <= 0x3F)
                    state = State.CsiIgnore;
                else
                    DispatchCsi(c);
                break;

            case State.CsiIgnore:
                if (c < 0x20)
                    handler.Execute(c);
                else if (c is >= 0x40 and <= 0x7E)
                    state = State.Ground;
                break;

            case State.Osc:
                if (c == 0x07)
                    DispatchOsc();
                else if (c >= 0x20 && text.Length < MaximumStringLength)
                    text.Append(char.ConvertFromUtf32(c));
                break;

            case State.IgnoredString:
                break;
        }
    }

    private void BeginEscape()
    {
        intermediates.Clear();
        state = State.Escape;
    }

    private void DispatchCsi(int final)
    {
        state = State.Ground;
        if (final > 0x7E)
            return;

        parameters.Begin();
        handler.Csi(parameters, marker, intermediates.ToString(), (char)final);
    }

    private void DispatchOsc()
    {
        state = State.Ground;
        handler.OperatingSystemCommand(text.ToString());
        text.Clear();
    }
}
