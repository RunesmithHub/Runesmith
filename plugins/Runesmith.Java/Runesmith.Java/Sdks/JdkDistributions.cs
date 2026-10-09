namespace Runesmith.Java.Sdks;

/// <summary>A JDK distribution Runesmith offers.</summary>
/// <param name="Id">Its id in the foojay Discovery API, such as <c>temurin</c>; also the start of its install folder's name.</param>
/// <param name="Name">Its name, such as "Temurin".</param>
/// <param name="Implementors">The <c>IMPLEMENTOR</c> values its <c>release</c> file has.</param>
internal sealed record JdkDistribution(string Id, string Name, IReadOnlyList<string> Implementors);

/// <summary>The JDK distributions Runesmith offers to download, and how to tell an installed JDK's distribution.</summary>
internal static class JdkDistributions
{
    /// <summary>Gets the distributions, in the order the download form lists them.</summary>
    public static IReadOnlyList<JdkDistribution> All { get; } =
    [
        new("temurin", "Temurin", ["Eclipse Adoptium", "Eclipse Foundation"]),
        new("zulu", "Zulu", ["Azul Systems, Inc."]),
        new("corretto", "Corretto", ["Amazon.com Inc."]),
        new("liberica", "Liberica", ["BellSoft"]),
        new("microsoft", "Microsoft", ["Microsoft"]),
        new("oracle_open_jdk", "Oracle OpenJDK", ["Oracle Corporation"]),
        new("graalvm_community", "GraalVM Community", ["GraalVM Community"]),
        new("sap_machine", "SapMachine", ["SAP SE"]),
        new("semeru", "Semeru", ["IBM Corporation", "International Business Machines Corporation", "Eclipse OpenJ9"]),
    ];

    /// <summary>Finds a distribution by its id.</summary>
    public static JdkDistribution? ById(string id) => All.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a distribution by its name.</summary>
    public static JdkDistribution? ByName(string? name) => All.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the distribution of an installed JDK by its <c>IMPLEMENTOR</c>.</summary>
    public static JdkDistribution? ByImplementor(string? implementor) =>
        All.FirstOrDefault(d => d.Implementors.Contains(implementor ?? "", StringComparer.OrdinalIgnoreCase));
}
