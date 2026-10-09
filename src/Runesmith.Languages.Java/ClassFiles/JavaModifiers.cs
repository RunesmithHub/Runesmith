namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>The modifiers of a class, field or method.</summary>
[Flags]
public enum JavaModifiers
{
    None = 0,
    Public = 1 << 0,
    Private = 1 << 1,
    Protected = 1 << 2,
    Static = 1 << 3,
    Final = 1 << 4,
    Abstract = 1 << 5,
    Synchronized = 1 << 6,
    Native = 1 << 7,
    Transient = 1 << 8,
    Volatile = 1 << 9,
    Strict = 1 << 10,

    /// <summary>An interface method with a body that is not static or private.</summary>
    Default = 1 << 11,

    /// <summary>A method whose last parameter takes any number of arguments.</summary>
    Varargs = 1 << 12,

    /// <summary>A class with permitted subclasses.</summary>
    Sealed = 1 << 13,
}

/// <summary>What kind of type a class file declares.</summary>
public enum ClassKind : byte
{
    Class,
    Interface,
    Enum,
    Record,
    Annotation,
    Module,
}

/// <summary>Converts class file access flags (JVMS 4.1, 4.5, 4.6 and 4.7.6) to <see cref="JavaModifiers"/>.</summary>
internal static class AccessFlags
{
    public const int Public = 0x0001;
    public const int Private = 0x0002;
    public const int Protected = 0x0004;
    public const int Static = 0x0008;
    public const int Final = 0x0010;
    public const int SynchronizedOrSuper = 0x0020;
    public const int Transitive = 0x0020;
    public const int VolatileOrBridge = 0x0040;
    public const int TransientOrVarargs = 0x0080;
    public const int Native = 0x0100;
    public const int Interface = 0x0200;
    public const int Abstract = 0x0400;
    public const int Strict = 0x0800;
    public const int Synthetic = 0x1000;
    public const int Annotation = 0x2000;
    public const int Enum = 0x4000;
    public const int Module = 0x8000;

    public static JavaModifiers Common(int flags)
    {
        var modifiers = JavaModifiers.None;
        if ((flags & Public) != 0)
            modifiers |= JavaModifiers.Public;
        if ((flags & Private) != 0)
            modifiers |= JavaModifiers.Private;
        if ((flags & Protected) != 0)
            modifiers |= JavaModifiers.Protected;
        if ((flags & Static) != 0)
            modifiers |= JavaModifiers.Static;
        if ((flags & Final) != 0)
            modifiers |= JavaModifiers.Final;
        if ((flags & Abstract) != 0)
            modifiers |= JavaModifiers.Abstract;
        return modifiers;
    }

    public static JavaModifiers Field(int flags)
    {
        var modifiers = Common(flags);
        if ((flags & VolatileOrBridge) != 0)
            modifiers |= JavaModifiers.Volatile;
        if ((flags & TransientOrVarargs) != 0)
            modifiers |= JavaModifiers.Transient;
        return modifiers;
    }

    public static JavaModifiers Method(int flags, bool inInterface)
    {
        var modifiers = Common(flags);
        if ((flags & SynchronizedOrSuper) != 0)
            modifiers |= JavaModifiers.Synchronized;
        if ((flags & TransientOrVarargs) != 0)
            modifiers |= JavaModifiers.Varargs;
        if ((flags & Native) != 0)
            modifiers |= JavaModifiers.Native;
        if ((flags & Strict) != 0)
            modifiers |= JavaModifiers.Strict;
        if (inInterface && (flags & (Abstract | Static | Private)) == 0)
            modifiers |= JavaModifiers.Default;
        return modifiers;
    }
}
