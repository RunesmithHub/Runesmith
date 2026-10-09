namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>A class, interface, enum, record or annotation read from a class file.</summary>
/// <remarks>Its header and class attributes are read when it is created; its fields and methods are decoded the first time either is asked
/// for, after which the class file bytes are released. Instances are safe to share between threads.</remarks>
public sealed class ClassSymbol
{
    /// <summary>The highest class file version read: 69 is Java 25.</summary>
    public const int HighestMajorVersion = 69;

    private readonly Lock gate = new();
    private ReadOnlyMemory<byte> data;
    private ConstantPool? pool;
    private int fieldsStart;
    private int methodsStart;
    private IReadOnlyList<FieldSymbol>? fields;
    private IReadOnlyList<MethodSymbol>? methods;
    private Func<(IReadOnlyList<FieldSymbol> Fields, IReadOnlyList<MethodSymbol> Methods)>? members;

    private ClassSymbol(ReadOnlyMemory<byte> data, ConstantPool pool)
    {
        this.data = data;
        this.pool = pool;
    }

    /// <summary>Creates a type declared in source, whose fields and methods <paramref name="members"/> makes the first time either is asked for.</summary>
    internal ClassSymbol(Func<(IReadOnlyList<FieldSymbol> Fields, IReadOnlyList<MethodSymbol> Methods)> members)
    {
        this.members = members;
    }

    /// <summary>Gets the binary name, such as <c>java.util.Map$Entry</c>.</summary>
    public required string BinaryName { get; init; }

    /// <summary>Gets the name the type is declared with, such as <c>Entry</c>.</summary>
    public required string SimpleName { get; init; }

    public string PackageName => ClassNames.PackageName(BinaryName);

    /// <summary>Gets the binary name of the class this one is declared in, or null for a top-level class.</summary>
    public required string? OuterBinaryName { get; init; }

    public required ClassKind Kind { get; init; }

    public required JavaModifiers Modifiers { get; init; }

    /// <summary>Gets the class file's major version, such as 69 for Java 25.</summary>
    public required int MajorVersion { get; init; }

    public required bool IsDeprecated { get; init; }

    public required IReadOnlyList<TypeParameter> TypeParameters { get; init; }

    /// <summary>Gets the superclass, or null for <c>java.lang.Object</c>, interfaces and modules.</summary>
    public required ClassTypeReference? SuperClass { get; init; }

    public required IReadOnlyList<ClassTypeReference> Interfaces { get; init; }

    /// <summary>Gets the binary names of the classes a sealed type permits.</summary>
    public required IReadOnlyList<string> PermittedSubclasses { get; init; }

    public required IReadOnlyList<RecordComponent> RecordComponents { get; init; }

    /// <summary>Gets the binary names of the member classes declared directly in this class.</summary>
    public required IReadOnlyList<string> NestedClasses { get; init; }

    public required string? NestHost { get; init; }

    public required IReadOnlyList<string> NestMembers { get; init; }

    public required EnclosingMethod? EnclosingMethod { get; init; }

    /// <summary>Gets what a <c>module-info.class</c> declares, or null for other classes.</summary>
    public required ModuleInfo? Module { get; init; }

    public bool IsStatic => (Modifiers & JavaModifiers.Static) != 0;

    /// <summary>Gets the fields, without private and synthetic ones.</summary>
    public IReadOnlyList<FieldSymbol> Fields
    {
        get
        {
            DecodeMembers();
            return fields!;
        }
    }

    /// <summary>Gets the methods and constructors, without private, synthetic and bridge ones and the static initializer.</summary>
    public IReadOnlyList<MethodSymbol> Methods
    {
        get
        {
            DecodeMembers();
            return methods!;
        }
    }

    public IEnumerable<MethodSymbol> Constructors => Methods.Where(m => m.IsConstructor);

    /// <summary>Reads a class file.</summary>
    /// <exception cref="ClassFileFormatException">The bytes are not a class file this reader understands.</exception>
    public static ClassSymbol Read(ReadOnlyMemory<byte> bytes)
    {
        var span = bytes.Span;
        if (span.Length < 10 || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(span) != 0xCAFEBABE)
            throw new ClassFileFormatException("The bytes are not a class file.");

        var major = ClassReader.U2(span, 6);
        if (major > HighestMajorVersion)
            throw new ClassFileFormatException($"Class file version {major} is newer than this reader knows ({HighestMajorVersion}).");

        var pool = new ConstantPool(bytes, 8, out var position);
        var access = ClassReader.U2(span, position);
        var binaryName = pool.GetClassName(ClassReader.U2(span, position + 2));
        var superIndex = ClassReader.U2(span, position + 4);
        var interfaceCount = ClassReader.U2(span, position + 6);
        position += 8;
        var interfaceNames = new string[interfaceCount];
        for (var i = 0; i < interfaceCount; i++, position += 2)
            interfaceNames[i] = pool.GetClassName(ClassReader.U2(span, position));

        var fieldsStart = position;
        position = SkipMembers(span, position);
        var methodsStart = position;
        position = SkipMembers(span, position);

        var attributes = new ClassAttributes();
        ReadClassAttributes(span, pool, position, binaryName, attributes);

        var isModule = (access & AccessFlags.Module) != 0;
        var effectiveAccess = attributes.InnerAccess ?? access;
        var kind = isModule ? ClassKind.Module
            : (access & AccessFlags.Annotation) != 0 ? ClassKind.Annotation
            : (access & AccessFlags.Interface) != 0 ? ClassKind.Interface
            : (access & AccessFlags.Enum) != 0 ? ClassKind.Enum
            : attributes.RecordComponents is not null || superIndex != 0 && pool.GetClassName(superIndex) == "java.lang.Record" ? ClassKind.Record
            : ClassKind.Class;

        var modifiers = AccessFlags.Common(effectiveAccess);
        if (kind is ClassKind.Interface or ClassKind.Annotation)
            modifiers |= JavaModifiers.Abstract;
        if (attributes.PermittedSubclasses.Count > 0)
            modifiers |= JavaModifiers.Sealed;

        ClassTypeReference? superClass = null;
        IReadOnlyList<ClassTypeReference> interfaces;
        IReadOnlyList<TypeParameter> typeParameters = [];
        if (attributes.Signature is { } signature)
        {
            var parsed = SignatureParser.ParseClass(signature);
            typeParameters = parsed.TypeParameters;
            superClass = kind is ClassKind.Interface or ClassKind.Annotation ? null : parsed.SuperClass;
            interfaces = parsed.Interfaces;
        }
        else
        {
            superClass = superIndex == 0 || kind is ClassKind.Interface or ClassKind.Annotation ? null : new ClassTypeReference(pool.GetClassName(superIndex), []);
            interfaces = [.. interfaceNames.Select(name => new ClassTypeReference(name, []))];
        }

        if (superClass is { BinaryName: "java.lang.Object" } && binaryName == "java.lang.Object")
            superClass = null;

        return new ClassSymbol(bytes, pool)
        {
            BinaryName = binaryName,
            SimpleName = attributes.InnerName ?? ClassNames.SimpleName(binaryName),
            OuterBinaryName = attributes.OuterName ?? (attributes.InnerAccess is null ? null : ClassNames.OuterName(binaryName)),
            Kind = kind,
            Modifiers = modifiers,
            MajorVersion = major,
            IsDeprecated = attributes.IsDeprecated,
            TypeParameters = typeParameters,
            SuperClass = superClass,
            Interfaces = interfaces,
            PermittedSubclasses = attributes.PermittedSubclasses,
            RecordComponents = attributes.RecordComponents ?? [],
            NestedClasses = attributes.NestedClasses,
            NestHost = attributes.NestHost,
            NestMembers = attributes.NestMembers,
            EnclosingMethod = attributes.EnclosingMethod,
            Module = attributes.Module,
            fieldsStart = fieldsStart,
            methodsStart = methodsStart,
        };
    }

    private void DecodeMembers()
    {
        if (methods is not null)
            return;

        lock (gate)
        {
            if (methods is not null)
                return;

            if (members is not null)
            {
                (fields, methods) = members();
                members = null;
                return;
            }

            var span = data.Span;
            var pool = this.pool!;
            var inInterface = Kind is ClassKind.Interface or ClassKind.Annotation;
            // A constructor of an inner (non-static) class takes the outer instance first in its descriptor, but not in its signature.
            var hasOuterInstance = OuterBinaryName is not null && !IsStatic && Kind == ClassKind.Class;
            var readFields = new List<FieldSymbol>();
            var position = fieldsStart;
            var count = ClassReader.U2(span, position);
            position += 2;
            for (var i = 0; i < count; i++)
            {
                var field = ReadField(span, pool, ref position);
                if (field is not null)
                    readFields.Add(field);
            }

            var readMethods = new List<MethodSymbol>();
            position = methodsStart;
            count = ClassReader.U2(span, position);
            position += 2;
            for (var i = 0; i < count; i++)
            {
                var method = ReadMethod(span, pool, ref position, inInterface, hasOuterInstance);
                if (method is not null)
                    readMethods.Add(method);
            }

            fields = readFields;
            methods = readMethods;
            data = default;
            this.pool = null;
        }
    }

    private static FieldSymbol? ReadField(ReadOnlySpan<byte> span, ConstantPool pool, ref int position)
    {
        var access = ClassReader.U2(span, position);
        var name = pool.GetUtf8(ClassReader.U2(span, position + 2));
        var descriptor = pool.GetUtf8(ClassReader.U2(span, position + 4));
        var attributeCount = ClassReader.U2(span, position + 6);
        position += 8;
        string? signature = null;
        object? constant = null;
        var deprecated = false;
        for (var i = 0; i < attributeCount; i++)
        {
            var attributeName = pool.GetUtf8(ClassReader.U2(span, position));
            var length = ClassReader.U4(span, position + 2);
            var body = position + 6;
            switch (attributeName)
            {
                case "Signature":
                    signature = pool.GetUtf8(ClassReader.U2(span, body));
                    break;
                case "ConstantValue":
                    constant = pool.GetConstant(ClassReader.U2(span, body));
                    break;
                case "Deprecated":
                    deprecated = true;
                    break;
                case "RuntimeVisibleAnnotations":
                    deprecated |= Annotations.HasDeprecated(span, pool, body);
                    break;
            }

            position = body + length;
        }

        if ((access & (AccessFlags.Private | AccessFlags.Synthetic)) != 0)
            return null;

        var type = SignatureParser.ParseType(signature ?? descriptor);
        return new FieldSymbol(name, type, AccessFlags.Field(access), constant, (access & AccessFlags.Enum) != 0, deprecated);
    }

    private static MethodSymbol? ReadMethod(ReadOnlySpan<byte> span, ConstantPool pool, ref int position, bool inInterface, bool hasOuterInstance)
    {
        var access = ClassReader.U2(span, position);
        var name = pool.GetUtf8(ClassReader.U2(span, position + 2));
        var descriptor = pool.GetUtf8(ClassReader.U2(span, position + 4));
        var attributeCount = ClassReader.U2(span, position + 6);
        position += 8;
        string? signature = null;
        string[]? parameterNames = null;
        List<TypeReference>? exceptions = null;
        var deprecated = false;
        for (var i = 0; i < attributeCount; i++)
        {
            var attributeName = pool.GetUtf8(ClassReader.U2(span, position));
            var length = ClassReader.U4(span, position + 2);
            var body = position + 6;
            switch (attributeName)
            {
                case "Signature":
                    signature = pool.GetUtf8(ClassReader.U2(span, body));
                    break;
                case "Exceptions":
                {
                    var count = ClassReader.U2(span, body);
                    exceptions = new List<TypeReference>(count);
                    for (var e = 0; e < count; e++)
                        exceptions.Add(new ClassTypeReference(pool.GetClassName(ClassReader.U2(span, body + 2 + e * 2)), []));
                    break;
                }

                case "MethodParameters":
                {
                    var count = ClassReader.U1(span, body);
                    parameterNames = new string[count];
                    for (var p = 0; p < count; p++)
                    {
                        var nameIndex = ClassReader.U2(span, body + 1 + p * 4);
                        parameterNames[p] = nameIndex == 0 ? "" : pool.GetUtf8(nameIndex);
                    }

                    break;
                }

                case "Deprecated":
                    deprecated = true;
                    break;
                case "RuntimeVisibleAnnotations":
                    deprecated |= Annotations.HasDeprecated(span, pool, body);
                    break;
            }

            position = body + length;
        }

        if ((access & (AccessFlags.Private | AccessFlags.Synthetic | AccessFlags.VolatileOrBridge)) != 0 || name == "<clinit>")
            return null;

        var parsed = SignatureParser.ParseMethod(signature ?? descriptor);
        var parameterTypes = parsed.ParameterTypes;
        var names = parameterNames;
        if (signature is null && hasOuterInstance && name == MethodSymbol.ConstructorName && parameterTypes.Count > 0)
            parameterTypes = [.. parameterTypes.Skip(1)];
        if (names is not null && names.Length != parameterTypes.Count)
            names = names.Length > parameterTypes.Count ? names[(names.Length - parameterTypes.Count)..] : null;

        var hasNames = names is not null && names.All(n => n.Length > 0);
        var finalNames = hasNames ? names! : [.. Enumerable.Range(0, parameterTypes.Count).Select(i => "arg" + i)];
        var throws = parsed.Throws.Count > 0 ? parsed.Throws : (IReadOnlyList<TypeReference>?)exceptions ?? [];
        return new MethodSymbol(name, AccessFlags.Method(access, inInterface), parsed.TypeParameters, parameterTypes, finalNames, hasNames,
            parsed.ReturnType, throws, deprecated);
    }

    private static int SkipMembers(ReadOnlySpan<byte> span, int position)
    {
        var count = ClassReader.U2(span, position);
        position += 2;
        for (var i = 0; i < count; i++)
        {
            var attributes = ClassReader.U2(span, position + 6);
            position += 8;
            for (var a = 0; a < attributes; a++)
                position += 6 + ClassReader.U4(span, position + 2);
        }

        return position;
    }

    private static void ReadClassAttributes(ReadOnlySpan<byte> span, ConstantPool pool, int position, string binaryName, ClassAttributes result)
    {
        var count = ClassReader.U2(span, position);
        position += 2;
        for (var i = 0; i < count; i++)
        {
            var name = pool.GetUtf8(ClassReader.U2(span, position));
            var length = ClassReader.U4(span, position + 2);
            var body = position + 6;
            switch (name)
            {
                case "Signature":
                    result.Signature = pool.GetUtf8(ClassReader.U2(span, body));
                    break;
                case "Deprecated":
                    result.IsDeprecated = true;
                    break;
                case "RuntimeVisibleAnnotations":
                    result.IsDeprecated |= Annotations.HasDeprecated(span, pool, body);
                    break;
                case "InnerClasses":
                    ReadInnerClasses(span, pool, body, binaryName, result);
                    break;
                case "EnclosingMethod":
                {
                    var classIndex = ClassReader.U2(span, body);
                    var methodIndex = ClassReader.U2(span, body + 2);
                    string? methodName = null;
                    string? methodDescriptor = null;
                    if (methodIndex != 0)
                    {
                        var nameAndType = pool.OffsetOf(methodIndex);
                        methodName = pool.GetUtf8(ClassReader.U2(span, nameAndType + 1));
                        methodDescriptor = pool.GetUtf8(ClassReader.U2(span, nameAndType + 3));
                    }

                    result.EnclosingMethod = new EnclosingMethod(pool.GetClassName(classIndex), methodName, methodDescriptor);
                    break;
                }

                case "Record":
                    result.RecordComponents = ReadRecord(span, pool, body);
                    break;
                case "PermittedSubclasses":
                    result.PermittedSubclasses = ReadClassList(span, pool, body);
                    break;
                case "NestHost":
                    result.NestHost = pool.GetClassName(ClassReader.U2(span, body));
                    break;
                case "NestMembers":
                    result.NestMembers = ReadClassList(span, pool, body);
                    break;
                case "Module":
                    result.Module = ReadModule(span, pool, body);
                    break;
            }

            position = body + length;
        }
    }

    private static void ReadInnerClasses(ReadOnlySpan<byte> span, ConstantPool pool, int body, string binaryName, ClassAttributes result)
    {
        var count = ClassReader.U2(span, body);
        var nested = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var entry = body + 2 + i * 8;
            var innerIndex = ClassReader.U2(span, entry);
            var outerIndex = ClassReader.U2(span, entry + 2);
            var nameIndex = ClassReader.U2(span, entry + 4);
            var access = ClassReader.U2(span, entry + 6);
            var inner = pool.GetClassName(innerIndex);
            if (inner == binaryName)
            {
                result.InnerAccess = access;
                result.InnerName = nameIndex == 0 ? "" : pool.GetUtf8(nameIndex);
                if (outerIndex != 0)
                    result.OuterName = pool.GetClassName(outerIndex);
            }
            else if (outerIndex != 0 && nameIndex != 0 && (access & AccessFlags.Synthetic) == 0 && pool.GetClassName(outerIndex) == binaryName)
            {
                nested.Add(inner);
            }
        }

        result.NestedClasses = nested;
    }

    private static List<RecordComponent> ReadRecord(ReadOnlySpan<byte> span, ConstantPool pool, int body)
    {
        var count = ClassReader.U2(span, body);
        var components = new List<RecordComponent>(count);
        var position = body + 2;
        for (var i = 0; i < count; i++)
        {
            var name = pool.GetUtf8(ClassReader.U2(span, position));
            var descriptor = pool.GetUtf8(ClassReader.U2(span, position + 2));
            var attributeCount = ClassReader.U2(span, position + 4);
            position += 6;
            string? signature = null;
            for (var a = 0; a < attributeCount; a++)
            {
                var length = ClassReader.U4(span, position + 2);
                if (pool.GetUtf8(ClassReader.U2(span, position)) == "Signature")
                    signature = pool.GetUtf8(ClassReader.U2(span, position + 6));
                position += 6 + length;
            }

            components.Add(new RecordComponent(name, SignatureParser.ParseType(signature ?? descriptor)));
        }

        return components;
    }

    private static List<string> ReadClassList(ReadOnlySpan<byte> span, ConstantPool pool, int body)
    {
        var count = ClassReader.U2(span, body);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
            names.Add(pool.GetClassName(ClassReader.U2(span, body + 2 + i * 2)));
        return names;
    }

    private static ModuleInfo ReadModule(ReadOnlySpan<byte> span, ConstantPool pool, int body)
    {
        var name = pool.GetModuleOrPackageName(ClassReader.U2(span, body));
        var position = body + 6;
        var requiresCount = ClassReader.U2(span, position);
        position += 2;
        var requires = new List<string>(requiresCount);
        var transitive = new List<string>();
        for (var i = 0; i < requiresCount; i++, position += 6)
        {
            var required = pool.GetModuleOrPackageName(ClassReader.U2(span, position));
            requires.Add(required);
            if ((ClassReader.U2(span, position + 2) & AccessFlags.Transitive) != 0)
                transitive.Add(required);
        }

        var exportsCount = ClassReader.U2(span, position);
        position += 2;
        var exports = new List<ModuleExport>(exportsCount);
        for (var i = 0; i < exportsCount; i++)
        {
            var package = pool.GetModuleOrPackageName(ClassReader.U2(span, position));
            var toCount = ClassReader.U2(span, position + 4);
            exports.Add(new ModuleExport(package, toCount > 0));
            position += 6 + toCount * 2;
        }

        return new ModuleInfo(name, exports, requires) { TransitiveRequires = transitive };
    }

    private sealed class ClassAttributes
    {
        public string? Signature;
        public bool IsDeprecated;
        public int? InnerAccess;
        public string? InnerName;
        public string? OuterName;
        public List<string> NestedClasses = [];
        public EnclosingMethod? EnclosingMethod;
        public List<RecordComponent>? RecordComponents;
        public List<string> PermittedSubclasses = [];
        public string? NestHost;
        public List<string> NestMembers = [];
        public ModuleInfo? Module;
    }
}
