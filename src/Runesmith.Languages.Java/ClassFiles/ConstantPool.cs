using System.Buffers.Binary;
using System.Text;

namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>A class file's constant pool: the offset of every entry, and its strings decoded once, when first read.</summary>
internal sealed class ConstantPool
{
    private const byte Utf8 = 1;
    private const byte Integer = 3;
    private const byte Float = 4;
    private const byte Long = 5;
    private const byte Double = 6;
    private const byte Class = 7;
    private const byte String = 8;
    private const byte Module = 19;
    private const byte Package = 20;

    private readonly ReadOnlyMemory<byte> data;
    private readonly int[] offsets;
    private readonly string?[] strings;

    /// <summary>Reads the pool that starts at <paramref name="start"/>; <paramref name="end"/> is the offset after it.</summary>
    public ConstantPool(ReadOnlyMemory<byte> data, int start, out int end)
    {
        this.data = data;
        var span = data.Span;
        var count = ClassReader.U2(span, start);
        offsets = new int[count];
        strings = new string?[count];
        var position = start + 2;
        for (var index = 1; index < count; index++)
        {
            offsets[index] = position;
            var tag = span[position];
            position += tag switch
            {
                Utf8 => 3 + ClassReader.U2(span, position + 1),
                Integer or Float => 5,
                Long or Double => 9,
                Class or String or 16 or Module or Package => 3,
                9 or 10 or 11 or 12 or 17 or 18 => 5,
                15 => 4,
                _ => throw new ClassFileFormatException($"Unknown constant pool tag {tag} at {position}."),
            };

            // Long and double entries take two slots (JVMS 4.4.5).
            if (tag is Long or Double)
                index++;
        }

        end = position;
    }

    /// <summary>Gets the offset of an entry's tag in the class file.</summary>
    public int OffsetOf(int index) =>
        index > 0 && index < offsets.Length ? offsets[index] : throw new ClassFileFormatException($"Constant {index} does not exist.");

    /// <summary>Gets a UTF-8 entry's text.</summary>
    public string GetUtf8(int index)
    {
        if (strings[index] is { } cached)
            return cached;

        var span = data.Span;
        var offset = Offset(index, Utf8);
        var text = DecodeModifiedUtf8(span.Slice(offset + 3, ClassReader.U2(span, offset + 1)));
        strings[index] = text;
        return text;
    }

    /// <summary>Gets the binary name of a class entry, such as <c>java.util.Map$Entry</c>; array classes keep their descriptor.</summary>
    public string GetClassName(int index) => ClassNames.FromInternal(GetUtf8(ClassReader.U2(data.Span, Offset(index, Class) + 1)));

    /// <summary>Gets the name of a module or package entry; packages use dots.</summary>
    public string GetModuleOrPackageName(int index)
    {
        var offset = offsets[index];
        var tag = data.Span[offset];
        if (tag is not (Module or Package))
            throw new ClassFileFormatException($"Constant {index} is not a module or package.");
        var name = GetUtf8(ClassReader.U2(data.Span, offset + 1));
        return tag == Package ? ClassNames.FromInternal(name) : name;
    }

    /// <summary>Gets the value of a constant: an int, long, float, double or string.</summary>
    public object? GetConstant(int index)
    {
        var span = data.Span;
        var offset = offsets[index];
        return span[offset] switch
        {
            Integer => BinaryPrimitives.ReadInt32BigEndian(span[(offset + 1)..]),
            Float => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(span[(offset + 1)..])),
            Long => BinaryPrimitives.ReadInt64BigEndian(span[(offset + 1)..]),
            Double => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(span[(offset + 1)..])),
            String => GetUtf8(ClassReader.U2(span, offset + 1)),
            _ => null,
        };
    }

    private int Offset(int index, byte tag)
    {
        if (index <= 0 || index >= offsets.Length || data.Span[offsets[index]] != tag)
            throw new ClassFileFormatException($"Constant {index} is not of kind {tag}.");
        return offsets[index];
    }

    // Class files use modified UTF-8: NUL is two bytes and characters outside the BMP are two three-byte surrogates (JVMS 4.4.7).
    private static string DecodeModifiedUtf8(ReadOnlySpan<byte> bytes)
    {
        var ascii = true;
        foreach (var b in bytes)
        {
            if (b >= 0x80)
            {
                ascii = false;
                break;
            }
        }

        if (ascii)
            return Encoding.ASCII.GetString(bytes);

        var chars = new char[bytes.Length];
        var count = 0;
        for (var i = 0; i < bytes.Length;)
        {
            var b = bytes[i];
            if (b < 0x80)
            {
                chars[count++] = (char)b;
                i++;
            }
            else if ((b & 0xE0) == 0xC0 && i + 1 < bytes.Length)
            {
                chars[count++] = (char)(((b & 0x1F) << 6) | (bytes[i + 1] & 0x3F));
                i += 2;
            }
            else if (i + 2 < bytes.Length)
            {
                chars[count++] = (char)(((b & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F));
                i += 3;
            }
            else
            {
                throw new ClassFileFormatException("A string in the constant pool is not valid modified UTF-8.");
            }
        }

        return new string(chars, 0, count);
    }
}

/// <summary>Reads big-endian numbers from class file bytes.</summary>
internal static class ClassReader
{
    public static int U1(ReadOnlySpan<byte> span, int offset) => span[offset];

    public static int U2(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16BigEndian(span[offset..]);

    public static int U4(ReadOnlySpan<byte> span, int offset)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(span[offset..]);
        return value > int.MaxValue ? throw new ClassFileFormatException("An attribute is too long.") : (int)value;
    }
}
