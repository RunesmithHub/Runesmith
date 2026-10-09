namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>A method's type parameters, parameter types, result and thrown types, as its signature or descriptor gives them.</summary>
public sealed record MethodSignature(
    IReadOnlyList<TypeParameter> TypeParameters,
    IReadOnlyList<TypeReference> ParameterTypes,
    TypeReference ReturnType,
    IReadOnlyList<TypeReference> Throws);

/// <summary>A class's type parameters, superclass and interfaces, as its signature gives them.</summary>
public sealed record ClassSignature(IReadOnlyList<TypeParameter> TypeParameters, ClassTypeReference? SuperClass, IReadOnlyList<ClassTypeReference> Interfaces);

/// <summary>Reads field and method descriptors and generic signatures (JVMS 4.3 and 4.7.9.1).</summary>
public static class SignatureParser
{
    /// <summary>Reads a field descriptor or a field's generic signature, such as <c>Ljava/util/List&lt;Ljava/lang/String;&gt;;</c>.</summary>
    /// <exception cref="ClassFileFormatException">The text is not a type signature.</exception>
    public static TypeReference ParseType(string signature)
    {
        var index = 0;
        var type = ReadType(signature, ref index);
        Expect(index == signature.Length, signature, index);
        return type;
    }

    /// <summary>Reads a method descriptor or a method's generic signature.</summary>
    public static MethodSignature ParseMethod(string signature)
    {
        var index = 0;
        var typeParameters = ReadTypeParameters(signature, ref index);
        Expect(index < signature.Length && signature[index] == '(', signature, index);
        index++;
        var parameters = new List<TypeReference>();
        while (index < signature.Length && signature[index] != ')')
            parameters.Add(ReadType(signature, ref index));
        Expect(index < signature.Length, signature, index);
        index++;
        var result = ReadType(signature, ref index);
        var throws = new List<TypeReference>();
        while (index < signature.Length && signature[index] == '^')
        {
            index++;
            throws.Add(ReadType(signature, ref index));
        }

        Expect(index == signature.Length, signature, index);
        return new MethodSignature(typeParameters, parameters, result, throws);
    }

    /// <summary>Reads a class's generic signature.</summary>
    public static ClassSignature ParseClass(string signature)
    {
        var index = 0;
        var typeParameters = ReadTypeParameters(signature, ref index);
        var superClass = ReadType(signature, ref index) as ClassTypeReference;
        var interfaces = new List<ClassTypeReference>();
        while (index < signature.Length)
        {
            if (ReadType(signature, ref index) is ClassTypeReference type)
                interfaces.Add(type);
        }

        return new ClassSignature(typeParameters, superClass, interfaces);
    }

    private static List<TypeParameter> ReadTypeParameters(string text, ref int index)
    {
        var parameters = new List<TypeParameter>();
        if (index >= text.Length || text[index] != '<')
            return parameters;

        index++;
        while (index < text.Length && text[index] != '>')
        {
            var colon = text.IndexOf(':', index);
            Expect(colon > index, text, index);
            var name = text[index..colon];
            index = colon;
            var bounds = new List<TypeReference>();
            while (index < text.Length && text[index] == ':')
            {
                index++;
                // An empty class bound (an interface-only bound list) is written as "::".
                if (index < text.Length && text[index] is not ':' and not '>')
                    bounds.Add(ReadType(text, ref index));
            }

            parameters.Add(new TypeParameter(name, bounds));
        }

        Expect(index < text.Length, text, index);
        index++;
        return parameters;
    }

    private static TypeReference ReadType(string text, ref int index)
    {
        Expect(index < text.Length, text, index);
        var c = text[index++];
        switch (c)
        {
            case 'Z': return PrimitiveTypeReference.Boolean;
            case 'B': return PrimitiveTypeReference.Byte;
            case 'C': return PrimitiveTypeReference.Char;
            case 'S': return PrimitiveTypeReference.Short;
            case 'I': return PrimitiveTypeReference.Int;
            case 'J': return PrimitiveTypeReference.Long;
            case 'F': return PrimitiveTypeReference.Float;
            case 'D': return PrimitiveTypeReference.Double;
            case 'V': return PrimitiveTypeReference.Void;
            case '[': return new ArrayTypeReference(ReadType(text, ref index));
            case 'T':
            {
                var end = text.IndexOf(';', index);
                Expect(end > index, text, index);
                var name = text[index..end];
                index = end + 1;
                return new TypeVariableReference(name);
            }

            case 'L':
                return ReadClassType(text, ref index);
            default:
                throw new ClassFileFormatException($"Unexpected '{c}' at {index - 1} in the signature {text}.");
        }
    }

    private static ClassTypeReference ReadClassType(string text, ref int index)
    {
        var start = index;
        while (index < text.Length && text[index] is not ('<' or ';' or '.'))
            index++;
        Expect(index < text.Length, text, index);
        var binaryName = ClassNames.FromInternal(text[start..index]);
        var current = new ClassTypeReference(binaryName, ReadTypeArguments(text, ref index));
        while (index < text.Length && text[index] == '.')
        {
            index++;
            var nameStart = index;
            while (index < text.Length && text[index] is not ('<' or ';' or '.'))
                index++;
            Expect(index < text.Length, text, index);
            current = new ClassTypeReference(current.BinaryName + "$" + text[nameStart..index], ReadTypeArguments(text, ref index), current);
        }

        Expect(index < text.Length && text[index] == ';', text, index);
        index++;
        return current;
    }

    private static List<TypeArgument> ReadTypeArguments(string text, ref int index)
    {
        var arguments = new List<TypeArgument>();
        if (index >= text.Length || text[index] != '<')
            return arguments;

        index++;
        while (index < text.Length && text[index] != '>')
        {
            switch (text[index])
            {
                case '*':
                    index++;
                    arguments.Add(TypeArgument.Unbounded);
                    break;
                case '+':
                    index++;
                    arguments.Add(new TypeArgument(WildcardKind.Extends, ReadType(text, ref index)));
                    break;
                case '-':
                    index++;
                    arguments.Add(new TypeArgument(WildcardKind.Super, ReadType(text, ref index)));
                    break;
                default:
                    arguments.Add(new TypeArgument(WildcardKind.None, ReadType(text, ref index)));
                    break;
            }
        }

        Expect(index < text.Length, text, index);
        index++;
        return arguments;
    }

    private static void Expect(bool condition, string text, int index)
    {
        if (!condition)
            throw new ClassFileFormatException($"The signature {text} is not valid at {index}.");
    }
}

/// <summary>A class file or signature that does not follow the class file format.</summary>
public sealed class ClassFileFormatException(string message) : FormatException(message);
