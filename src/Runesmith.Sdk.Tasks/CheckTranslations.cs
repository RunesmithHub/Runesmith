using System.Runtime.Serialization.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Runesmith.Sdk.Analyzers.Translations;

namespace Runesmith.Sdk.Tasks;

/// <summary>Checks a project's Fluent files: the syntax of every file, the English source, each translation against its English source, and
/// the files against the languages <c>plugin.json</c> declares.</summary>
/// <remarks>Syntax errors and problems in the English source are errors. Missing and unused messages and variables that differ from the
/// English source are warnings, since what a translation lacks shows in English.</remarks>
public sealed class CheckTranslations : Microsoft.Build.Utilities.Task
{
    /// <summary>Gets or sets the project's <c>locales</c> folder.</summary>
    [Required]
    public string LocalesDirectory { get; set; } = "";

    /// <summary>Gets or sets the project's Fluent files, all in <see cref="LocalesDirectory"/>.</summary>
    public ITaskItem[] Files { get; set; } = [];

    /// <summary>Gets or sets English Fluent files of other projects that a language pack translates, each with the <c>TranslationTarget</c> it belongs
    /// to: <c>runesmith</c> or a plugin id.</summary>
    public ITaskItem[] EnglishSources { get; set; } = [];

    /// <summary>Gets or sets the plugin's <c>plugin.json</c>, or nothing for a project that is not a plugin.</summary>
    public string? Manifest { get; set; }

    /// <summary>Gets or sets the project file; plugin.json applies to the plugin's implementation project only.</summary>
    public string? Project { get; set; }

    public override bool Execute()
    {
        var root = Path.GetFullPath(LocalesDirectory).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        var files = new List<LocaleFile>();
        foreach (var item in Files)
        {
            var path = Path.GetFullPath(item.GetMetadata("FullPath") is { Length: > 0 } full ? full : item.ItemSpec);
            var relative = path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length) : Path.GetFileName(path);
            if (Read(path) is { } text)
                files.Add(new LocaleFile(path, relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries), text));
        }

        var sources = new List<EnglishSource>();
        foreach (var item in EnglishSources)
        {
            var path = Path.GetFullPath(item.GetMetadata("FullPath") is { Length: > 0 } full ? full : item.ItemSpec);
            var target = item.GetMetadata("TranslationTarget");
            if (target.Length == 0)
                Log.LogWarning(null, TranslationChecker.NoSource, null, path, 0, 0, 0, 0, "The RunesmithEnglishSource item has no TranslationTarget, such as runesmith or a plugin id, so it is not used.");
            else if (Read(path) is { } text)
                sources.Add(new EnglishSource(target, path, text));
        }

        foreach (var problem in TranslationChecker.Check(files, sources, ReadManifest()))
        {
            switch (problem.Severity)
            {
                case ProblemSeverity.Error:
                    Log.LogError(null, problem.Code, null, problem.Path, problem.Line, problem.Column, problem.EndLine, problem.EndColumn, "{0}", problem.Message);
                    break;
                case ProblemSeverity.Warning:
                    Log.LogWarning(null, problem.Code, null, problem.Path, problem.Line, problem.Column, problem.EndLine, problem.EndColumn, "{0}", problem.Message);
                    break;
                default:
                    Log.LogMessage(null, problem.Code, null, problem.Path, problem.Line, problem.Column, problem.EndLine, problem.EndColumn, MessageImportance.High, "{0}", problem.Message);
                    break;
            }
        }

        return !Log.HasLoggedErrors;
    }

    private string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.LogError(null, TranslationChecker.SyntaxError, null, path, 0, 0, 0, 0, "The file could not be read: {0}", exception.Message);
            return null;
        }
    }

    private bool IsImplementation(XElement manifest)
    {
        if (string.IsNullOrEmpty(Project))
            return true;

        var project = Path.GetFullPath(Project);
        if (manifest.Element("projects")?.Element("implementation") is { Value.Length: > 0 } implementation)
            return string.Equals(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Manifest))!, implementation.Value)), project, StringComparison.OrdinalIgnoreCase);
        return !Path.GetFileNameWithoutExtension(project).EndsWith(".Contracts", StringComparison.OrdinalIgnoreCase);
    }

    private ManifestFacts ReadManifest()
    {
        if (string.IsNullOrEmpty(Manifest) || !File.Exists(Manifest))
            return ManifestFacts.None;

        XElement root;
        try
        {
            // The JSON reader of the base library maps JSON to XML, which is all .NET Standard 2.0 has.
            using var reader = JsonReaderWriterFactory.CreateJsonReader(File.ReadAllBytes(Manifest), XmlDictionaryReaderQuotas.Max);
            root = XElement.Load(reader);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            Log.LogWarning(null, TranslationChecker.Undeclared, null, Manifest, 0, 0, 0, 0, "plugin.json could not be read, so the languages it declares are not checked: {0}", exception.Message);
            return ManifestFacts.None;
        }

        if (!IsImplementation(root))
            return ManifestFacts.None;

        var languages = root.Element("languages") is { } list && (string?)list.Attribute("type") == "array" ? list.Elements().Select(e => e.Value).ToList() : null;
        var pack = root.Element("languagePack");
        return new ManifestFacts(
            Path.GetFullPath(Manifest),
            (string?)root.Element("id"),
            languages,
            pack is null ? null : (string?)pack.Element("language") ?? "",
            pack is null ? null : (string?)pack.Element("name") ?? "",
            root.Element("capabilities")?.Elements().Any() == true,
            root.Element("dependencies")?.Elements().Any() == true);
    }
}
