using System.Globalization;
using Runesmith.Sdk.Options;

namespace Runesmith.Java.Templates;

/// <summary>The ids and values of the Java templates' options.</summary>
public static class JavaTemplateOptions
{
    public const string BuildSystem = "buildSystem";
    public const string GradleDsl = "gradleDsl";
    public const string GroupId = "groupId";
    public const string ArtifactId = "artifactId";
    public const string JavaRelease = "javaRelease";
    public const string Jdk = "jdk";
    public const string ModuleInfo = "moduleInfo";
    public const string JUnit = "junit";
    public const string SampleCode = "sampleCode";

    public const string Maven = "maven";
    public const string Gradle = "gradle";
    public const string None = "none";
    public const string Kotlin = "kotlin";
    public const string Groovy = "groovy";

    /// <summary>The Java releases offered, oldest first.</summary>
    public static IReadOnlyList<int> Releases { get; } = [8, 11, 17, 21, 25];

    /// <summary>The release new projects use unless the default JDK is an older one of <see cref="Releases"/>.</summary>
    public const int DefaultRelease = 25;

    private const string PackagePattern = @"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$";

    /// <summary>Gets the options of a template.</summary>
    /// <param name="defaultRelease">The release selected at first, such as the default JDK's.</param>
    public static OptionSet For(JavaTemplateKind kind, int defaultRelease)
    {
        var sampleCode = new Option(SampleCode, "Sample code", OptionKind.Toggle)
        {
            Default = "true",
            Description = "Adds a small working example instead of empty classes.",
            Group = OptionGroup.Advanced,
        };
        var jdk = new Option(Jdk, "JDK", OptionKind.Sdk) { SdkKind = "jdk", Description = "The JDK that builds and runs the project." };
        if (kind == JavaTemplateKind.CompactSourceFile)
            return new OptionSet([jdk, sampleCode with { Group = OptionGroup.Main }]);

        string[] builds = kind == JavaTemplateKind.MultiModule ? [Maven, Gradle] : [Maven, Gradle, None];
        string[] withBuild = [Maven, Gradle];
        return new OptionSet(
        [
            new Option(BuildSystem, "Build system", OptionKind.Choice)
            {
                Default = Maven,
                Choices = [.. builds.Select(BuildChoice)],
            },
            new Option(GradleDsl, "Gradle DSL", OptionKind.Choice)
            {
                Default = Kotlin,
                Choices =
                [
                    new OptionChoice(Kotlin, "Kotlin") { Description = "build.gradle.kts and settings.gradle.kts" },
                    new OptionChoice(Groovy, "Groovy") { Description = "build.gradle and settings.gradle" },
                ],
                VisibleWhen = new OptionCondition(BuildSystem, [Gradle]),
            },
            new Option(GroupId, "Group id", OptionKind.Text)
            {
                Default = "com.example",
                IsRequired = true,
                Pattern = PackagePattern,
                PatternMessage = "Use a package name, such as com.example.",
                Description = "The organization's reversed domain; the code's package starts with it.",
            },
            new Option(ArtifactId, "Artifact id", OptionKind.Text)
            {
                Placeholder = "Derived from the name",
                Pattern = "^[A-Za-z0-9_.-]*$",
                PatternMessage = "Use letters, digits, dots, dashes and underscores.",
                VisibleWhen = new OptionCondition(BuildSystem, withBuild),
            },
            new Option(JavaRelease, "Java release", OptionKind.Choice)
            {
                Default = defaultRelease.ToString(CultureInfo.InvariantCulture),
                Choices = [.. Releases.Select(ReleaseChoice)],
            },
            jdk,
            new Option(JUnit, "JUnit tests", OptionKind.Toggle)
            {
                Default = "true",
                Description = "Adds JUnit 5 and a test for the sample code.",
                VisibleWhen = new OptionCondition(BuildSystem, withBuild),
            },
            new Option(ModuleInfo, "Module declaration", OptionKind.Toggle)
            {
                Default = "false",
                Description = "Adds module-info.java, making the project a named module.",
                Group = OptionGroup.Advanced,
                EnabledWhen = new OptionCondition(JavaRelease, [.. Releases.Where(r => r >= 11).Select(r => r.ToString(CultureInfo.InvariantCulture))]),
            },
            sampleCode,
        ]);
    }

    private static OptionChoice BuildChoice(string value) => value switch
    {
        Maven => new OptionChoice(Maven, "Maven") { Description = "pom.xml, built with mvn" },
        Gradle => new OptionChoice(Gradle, "Gradle") { Description = "Gradle build scripts, built with gradle" },
        _ => new OptionChoice(None, "None") { Description = "Sources only, compiled with javac" },
    };

    private static OptionChoice ReleaseChoice(int release)
    {
        var text = release.ToString(CultureInfo.InvariantCulture);
        return new OptionChoice(text, text) { Description = $"Java {text}, a long-term support release" };
    }
}
