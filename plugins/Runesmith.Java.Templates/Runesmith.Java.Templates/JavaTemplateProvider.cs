using System.Composition;
using System.Globalization;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Templates;

namespace Runesmith.Java.Templates;

/// <summary>Offers Runesmith's own Java templates and writes their files; nothing is downloaded and no build tool runs.</summary>
[Export(typeof(IProjectTemplateProvider))]
[method: ImportingConstructor]
public sealed class JavaTemplateProvider([Import(AllowDefault = true)] ISdkService? sdks) : IProjectTemplateProvider
{
    private const string Source = "Runesmith";

    public async Task<IReadOnlyList<ProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var release = await DefaultReleaseAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            Template(JavaTemplateKind.Application, "Application", TemplateCategories.Console, "terminal",
                "A program with a main class, built with Maven, Gradle or javac.", ["application", "console", "jar", "maven", "gradle"], release),
            Template(JavaTemplateKind.Library, "Library", TemplateCategories.Library, "package",
                "A library with a public class and its tests, built with Maven, Gradle or javac.", ["library", "jar", "maven", "gradle"], release),
            Template(JavaTemplateKind.CompactSourceFile, "Compact source file", TemplateCategories.Console, "file-code",
                "One Java 25 file with a void main() and no build, run with java File.java.", ["compact", "script", "single file", "java 25"], release),
            Template(JavaTemplateKind.MultiModule, "Multi-module project", TemplateCategories.Other, "layers",
                "An application module that uses a library module, built with Maven or Gradle.", ["modules", "multi-project", "maven", "gradle", "jpms"],
                release),
        ];
    }

    public async Task<ProjectCreationResult> CreateAsync(ProjectTemplate template, ProjectCreationRequest request, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        if (template.Data is not JavaTemplateKind kind)
            throw new ArgumentException($"{template.Name} is not a Java template.", nameof(template));

        var folder = Path.Combine(request.Location, request.Name.Trim());
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException($"{folder} exists already and is not empty.");

        var project = JavaProjectGenerator.Generate(kind, request.Name, request.Values);
        foreach (var file in project.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"Writing {file.Path}");
            var path = Path.Combine(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, file.Content, cancellationToken).ConfigureAwait(false);
        }

        return new ProjectCreationResult(folder, [Path.GetFullPath(Path.Combine(folder, project.MainFile))]);
    }

    /// <summary>Gets the major version of a JDK version, such as 25 for <c>25.0.1</c> and 8 for <c>1.8.0_392</c>, or null.</summary>
    internal static int? Major(string version)
    {
        var parts = version.Split('.', '_', '-', '+');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
            return null;
        return major == 1 && parts.Length > 1 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ? minor : major;
    }

    private async Task<int> DefaultReleaseAsync(CancellationToken cancellationToken)
    {
        if (sdks is null)
            return JavaTemplateOptions.DefaultRelease;
        try
        {
            var jdk = await sdks.GetDefaultAsync("jdk", cancellationToken).ConfigureAwait(false);
            return jdk is not null && Major(jdk.Version) is { } major && JavaTemplateOptions.Releases.Contains(major) ? major : JavaTemplateOptions.DefaultRelease;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return JavaTemplateOptions.DefaultRelease;
        }
    }

    private static ProjectTemplate Template(JavaTemplateKind kind, string name, string category, string icon, string description, IReadOnlyList<string> tags,
        int release) =>
        new($"runesmith.java.{kind.ToString().ToLowerInvariant()}", name, "Java", category)
        {
            Description = description,
            Tags = tags,
            Icon = icon,
            Author = Source,
            Source = Source,
            Options = JavaTemplateOptions.For(kind, release),
            Data = kind,
        };
}
