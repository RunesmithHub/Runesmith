using System.Text;

namespace Runesmith.Workspace.Files;

/// <summary>Reads text files, working out their encoding.</summary>
internal static class TextFileReader
{
    public const int BinaryProbeLength = 8192;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>UTF-8 without a byte order mark.</summary>
    public static Encoding Utf8 { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>UTF-8 with a byte order mark.</summary>
    public static Encoding Utf8WithBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Decodes a file's bytes; returns null when they look binary.</summary>
    /// <remarks>A byte order mark decides the encoding; without one the bytes are UTF-8 when they are valid UTF-8, and Latin-1 otherwise.</remarks>
    public static (string Text, Encoding Encoding)? Decode(ReadOnlySpan<byte> bytes)
    {
        var (encoding, preamble) = DetectBom(bytes);
        if (encoding is not null)
            return (encoding.GetString(bytes[preamble..]), encoding);

        if (bytes[..Math.Min(bytes.Length, BinaryProbeLength)].Contains((byte)0))
            return null;

        try
        {
            return (StrictUtf8.GetString(bytes), Utf8);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
        }
    }

    /// <summary>Encodes text with an encoding, starting with its byte order mark if it has one.</summary>
    public static byte[] Encode(string text, Encoding encoding)
    {
        var preamble = encoding.Preamble;
        var bytes = new byte[preamble.Length + encoding.GetByteCount(text)];
        preamble.CopyTo(bytes);
        encoding.GetBytes(text, bytes.AsSpan(preamble.Length));
        return bytes;
    }

    private static (Encoding? Encoding, int Preamble) DetectBom(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => (Utf8WithBom, 3),
        [0xFF, 0xFE, 0x00, 0x00, ..] => (Encoding.UTF32, 4),
        [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4),
        [0xFF, 0xFE, ..] => (Encoding.Unicode, 2),
        [0xFE, 0xFF, ..] => (Encoding.BigEndianUnicode, 2),
        _ => (null, 0),
    };
}
