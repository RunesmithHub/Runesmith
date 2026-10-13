using System.Reflection;
using Microsoft.VisualStudio.Composition;

namespace Runesmith.Composition;

/// <summary>Turns the errors VS-MEF reports while scanning an assembly into what caused them.</summary>
/// <remarks>VS-MEF reports one error per type it could not scan, and its message names only the type. The cause, such as an assembly
/// that would not load, is in the inner exceptions, and is usually the same for every type.</remarks>
internal static class DiscoveryFailures
{
    /// <summary>Gets one line per distinct cause, with how many types it stopped from being scanned.</summary>
    public static List<string> Describe(IEnumerable<PartDiscoveryException> errors) =>
    [
        .. errors.GroupBy(Cause, StringComparer.Ordinal).Select(group => group.Count() == 1
            ? (group.Key == group.First().Message ? group.Key : $"{group.First().Message} {group.Key}")
            : $"{group.Count()} types could not be scanned, such as {Scanned(group.First())}. {group.Key}"),
    ];

    /// <summary>Gets the messages of the exceptions at the bottom of an exception's chain, once each.</summary>
    public static string Cause(Exception exception)
    {
        var causes = new List<string>();
        Collect(exception, causes);
        return string.Join(" ", causes.Distinct(StringComparer.Ordinal));
    }

    private static void Collect(Exception exception, List<string> causes)
    {
        if (exception is ReflectionTypeLoadException { LoaderExceptions: var loaderExceptions } && loaderExceptions.Any(e => e is not null))
        {
            foreach (var loaderException in loaderExceptions.OfType<Exception>())
                Collect(loaderException, causes);
        }
        else if (exception.InnerException is { } inner)
        {
            Collect(inner, causes);
        }
        else
        {
            causes.Add(exception.Message);
        }
    }

    private static string Scanned(PartDiscoveryException error) => error.ScannedType?.FullName ?? error.AssemblyPath ?? error.Message;
}
