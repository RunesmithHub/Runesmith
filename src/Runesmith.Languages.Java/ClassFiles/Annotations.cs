namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>Reads just enough of an annotations attribute (JVMS 4.7.16) to find <c>@Deprecated</c>.</summary>
internal static class Annotations
{
    private const string DeprecatedDescriptor = "Ljava/lang/Deprecated;";

    public static bool HasDeprecated(ReadOnlySpan<byte> span, ConstantPool pool, int body)
    {
        var count = ClassReader.U2(span, body);
        var position = body + 2;
        for (var i = 0; i < count; i++)
        {
            if (pool.GetUtf8(ClassReader.U2(span, position)) == DeprecatedDescriptor)
                return true;
            position = SkipAnnotation(span, position);
        }

        return false;
    }

    private static int SkipAnnotation(ReadOnlySpan<byte> span, int position)
    {
        var pairs = ClassReader.U2(span, position + 2);
        position += 4;
        for (var i = 0; i < pairs; i++)
            position = SkipElementValue(span, position + 2);
        return position;
    }

    private static int SkipElementValue(ReadOnlySpan<byte> span, int position)
    {
        var tag = (char)span[position];
        position++;
        switch (tag)
        {
            case 'e':
                return position + 4;
            case '@':
                return SkipAnnotation(span, position);
            case '[':
            {
                var count = ClassReader.U2(span, position);
                position += 2;
                for (var i = 0; i < count; i++)
                    position = SkipElementValue(span, position);
                return position;
            }

            default:
                return position + 2;
        }
    }
}
