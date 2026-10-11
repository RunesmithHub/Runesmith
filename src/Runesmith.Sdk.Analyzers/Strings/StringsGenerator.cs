using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Runesmith.Sdk.Analyzers.Fluent;

namespace Runesmith.Sdk.Analyzers.Strings;

/// <summary>Generates the <c>Strings</c> class from a project's English Fluent files: a member for each message, which looks the message up
/// in the language Runesmith shows each time it is read.</summary>
/// <remarks>The English files are the additional files the SDK's build adds from <c>locales/en/*.ftl</c>. The class is in the namespace
/// <c>RunesmithStringsNamespace</c> names, which is the project's root namespace unless the project sets it.</remarks>
[Generator(LanguageNames.CSharp)]
public sealed class StringsGenerator : IIncrementalGenerator
{
    /// <summary>The name of the generated class.</summary>
    public const string ClassName = "Strings";

    /// <summary>The additional files' metadata that marks the English source; without it, files in <c>locales/en/</c> count.</summary>
    public const string SourceMetadata = "build_metadata.AdditionalFiles.RunesmithStrings";

    /// <summary>The MSBuild property that names the class's namespace.</summary>
    public const string NamespaceProperty = "build_property.RunesmithStringsNamespace";

    private const string Sdk = "global::Runesmith.Sdk.Localization";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => IsEnglishSource(pair.Left, pair.Right))
            .Select(static (pair, cancellationToken) => (pair.Left.Path, Text: pair.Left.GetText(cancellationToken)?.ToString() ?? ""))
            .Collect();
        var space = context.AnalyzerConfigOptionsProvider
            .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName ?? ""))
            .Select(static (pair, _) => pair.Left.GlobalOptions.TryGetValue(NamespaceProperty, out var name) && name.Trim().Length > 0 ? name.Trim() : pair.Right);
        context.RegisterSourceOutput(files.Combine(space), static (output, data) => Generate(output, data.Left, data.Right));
    }

    /// <summary>Gets whether an additional file is a Fluent file of the English source.</summary>
    internal static bool IsEnglishSource(AdditionalText file, AnalyzerConfigOptionsProvider options)
    {
        if (!file.Path.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
            return false;
        if (options.GetOptions(file).TryGetValue(SourceMetadata, out var marked) && marked.Length > 0)
            return string.Equals(marked, "true", StringComparison.OrdinalIgnoreCase);

        var folder = Path.GetDirectoryName(file.Path);
        return string.Equals(Path.GetFileName(folder), "en", StringComparison.Ordinal)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(folder)), "locales", StringComparison.Ordinal);
    }

    /// <summary>Gets where a span of a Fluent file is, for a diagnostic.</summary>
    internal static Location Locate(string path, FluentResource resource, FluentSpan span)
    {
        var (startLine, startColumn) = resource.Position(span.Start);
        var (endLine, endColumn) = resource.Position(span.End);
        return Location.Create(path, new TextSpan(span.Start, span.Length),
            new LinePositionSpan(new LinePosition(startLine, startColumn), new LinePosition(endLine, endColumn)));
    }

    private static void Generate(SourceProductionContext output, ImmutableArray<(string Path, string Text)> files, string space)
    {
        if (files.IsDefaultOrEmpty)
            return;

        var messages = new List<MessageModel>();
        var terms = new List<FluentTerm>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal) { [ClassName] = "the class itself" };
        foreach (var (path, text) in files.OrderBy(f => f.Path, StringComparer.Ordinal))
        {
            output.CancellationToken.ThrowIfCancellationRequested();
            var resource = FluentParser.Parse(text);
            foreach (var error in resource.Errors)
                output.ReportDiagnostic(Diagnostic.Create(Descriptors.FluentSyntax, Locate(path, resource, error.Span), error.Message));

            terms.AddRange(resource.Terms);

            foreach (var message in resource.Messages)
            {
                if (seen.ContainsKey(message.Id))
                {
                    output.ReportDiagnostic(Diagnostic.Create(Descriptors.DuplicateMessage, Locate(path, resource, message.IdSpan), message.Id));
                    continue;
                }

                seen[message.Id] = path;
                var model = MessageModel.Create(message);
                var clash = new[] { model.Member, model.HasAttributes ? model.Type : null }.FirstOrDefault(n => n is not null && names.ContainsKey(n));
                if (clash is not null)
                {
                    output.ReportDiagnostic(Diagnostic.Create(Descriptors.NameClash, Locate(path, resource, message.IdSpan), message.Id, clash, names[clash]));
                    continue;
                }

                names[model.Member] = $"the message {message.Id}";
                if (model.HasAttributes)
                    names[model.Type] = $"the type of the message {message.Id}";
                messages.Add(model);
            }
        }

        output.AddSource(ClassName + ".g.cs", SourceText.From(Write(messages, terms, space), Encoding.UTF8));
    }

    private static string Write(List<MessageModel> messages, IReadOnlyList<FluentTerm> terms, string space)
    {
        var code = new StringBuilder();
        code.AppendLine("// <auto-generated/>");
        code.AppendLine("#nullable enable");
        code.AppendLine();
        var indent = "";
        if (space.Length > 0)
        {
            code.Append("namespace ").AppendLine(space);
            code.AppendLine("{");
            indent = "    ";
        }

        var version = typeof(StringsGenerator).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        code.Append(indent).AppendLine("/// <summary>The messages of the project's English Fluent files, in the language Runesmith shows. Each member looks its");
        code.Append(indent).AppendLine("/// message up when it is read, so it follows a change of language.</summary>");
        code.Append(indent).AppendLine($"[global::System.CodeDom.Compiler.GeneratedCode(\"Runesmith.Sdk.Analyzers\", \"{version}\")]");
        code.Append(indent).AppendLine($"internal static partial class {ClassName}");
        code.Append(indent).AppendLine("{");
        var body = indent + "    ";
        code.Append(body).AppendLine($"private static {Sdk}.ILocalizer localizer => {Sdk}.Localized.For(typeof({ClassName}).Assembly, {EnglishWriter.ClassName}.Instance);");
        foreach (var message in messages)
        {
            code.AppendLine();
            message.Write(code, body);
        }

        code.AppendLine();
        EnglishWriter.Write(code, body, [.. messages.Select(message => message.Message)], terms);

        code.Append(indent).AppendLine("}");
        if (space.Length > 0)
            code.AppendLine("}");
        return code.ToString();
    }

    private sealed class MessageModel
    {
        private MessageModel(FluentMessage message, string summary, IReadOnlyList<ParameterModel> parameters)
        {
            Message = message;
            Member = Naming.Member(message.Id);
            Type = Member + "Message";
            Summary = summary;
            Parameters = parameters;
        }

        public FluentMessage Message { get; }

        public string Member { get; }

        public string Type { get; }

        public string Summary { get; }

        public IReadOnlyList<ParameterModel> Parameters { get; }

        public bool HasAttributes => Message.Attributes.Count > 0;

        public static MessageModel Create(FluentMessage message)
        {
            var (summary, documented) = FluentAnalysis.ReadComment(message.Comment);
            var parameters = new List<ParameterModel>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var variable in FluentAnalysis.Variables(message))
            {
                documented.TryGetValue(variable.Name, out var described);
                var type = FluentAnalysis.ParseType(described.Type) ?? variable.Type;
                var name = Naming.Parameter(variable.Name);
                for (var suffix = 2; !used.Add(name); suffix++)
                    name = Naming.Parameter(variable.Name) + suffix.ToString(CultureInfo.InvariantCulture);
                parameters.Add(new ParameterModel(variable.Name, name, type, described.Description));
            }

            return new MessageModel(message, summary, parameters);
        }

        public void Write(StringBuilder code, string indent)
        {
            var english = Message.Value is { } value ? FluentAnalysis.Render(value) : null;
            var signature = Parameters.Count == 0 ? "" : "(" + string.Join(", ", Parameters.Select(p => p.CSharpType + " " + p.Name)) + ")";
            var args = Parameters.Count == 0
                ? "null"
                : $"new {Sdk}.LocalizationArgs {{ " + string.Join(", ", Parameters.Select(p => $"{{ {Literal(p.Variable)}, {p.Argument} }}")) + " }";

            WriteDocs(code, indent, Summary.Length > 0 ? Summary : english ?? $"The message {Message.Id} and its attributes.", english);
            foreach (var parameter in Parameters.Where(p => p.Description.Length > 0))
                code.Append(indent).AppendLine($"/// <param name=\"{parameter.Name.TrimStart('@')}\">{Xml(parameter.Description)}</param>");

            if (!HasAttributes)
            {
                var call = Parameters.Count == 0 ? $"localizer.Get({Literal(Message.Id)})" : $"localizer.Get({Literal(Message.Id)}, {args})";
                code.Append(indent).AppendLine($"public static string {Member}{signature} => {call};");
                return;
            }

            code.Append(indent).AppendLine($"public static {Type} {Member}{signature} => new({args});");
            code.AppendLine();
            code.Append(indent).AppendLine($"/// <summary>The message {Xml(Message.Id)}{(Message.Value is null ? "" : ", which converts to its text,")} and its attributes.</summary>");
            code.Append(indent).AppendLine($"public readonly struct {Type}");
            code.Append(indent).AppendLine("{");
            var inner = indent + "    ";
            code.Append(inner).AppendLine($"private readonly {Sdk}.LocalizationArgs? args;");
            code.AppendLine();
            code.Append(inner).AppendLine($"internal {Type}({Sdk}.LocalizationArgs? args) => this.args = args;");
            var reserved = new HashSet<string>(StringComparer.Ordinal) { "Value", "ToString", "Equals", "GetHashCode", "GetType", Type };
            if (english is not null)
            {
                code.AppendLine();
                WriteDocs(code, inner, "The message's text.", english);
                code.Append(inner).AppendLine($"public string Value => localizer.Get({Literal(Message.Id)}, args);");
            }

            foreach (var attribute in Message.Attributes)
            {
                var name = Naming.Member(attribute.Id);
                if (!reserved.Add(name))
                    name += "Attribute";
                code.AppendLine();
                WriteDocs(code, inner, $"The attribute {attribute.Id}.", FluentAnalysis.Render(attribute.Value));
                code.Append(inner).AppendLine($"public string {name} => localizer.Get({Literal(Message.Id + "." + attribute.Id)}, args);");
            }

            code.AppendLine();
            var text = english is not null ? "Value" : $"localizer.Get({Literal(Message.Id + "." + Message.Attributes[0].Id)}, args)";
            code.Append(inner).AppendLine($"public override string ToString() => {text};");
            if (english is not null)
            {
                code.AppendLine();
                code.Append(inner).AppendLine($"public static implicit operator string({Type} message) => message.Value;");
            }

            code.Append(indent).AppendLine("}");
        }

        private static void WriteDocs(StringBuilder code, string indent, string summary, string? english)
        {
            code.Append(indent).AppendLine($"/// <summary>{Xml(summary)}</summary>");
            if (english is not null && english != summary)
                code.Append(indent).AppendLine($"/// <remarks>In English: {Xml(english)}</remarks>");
        }

        private static string Xml(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private sealed class ParameterModel
    {
        public ParameterModel(string variable, string name, FluentVariableType type, string? description)
        {
            Variable = variable;
            Name = name;
            Type = type;
            Description = description ?? "";
        }

        public string Variable { get; }

        public string Name { get; }

        public FluentVariableType Type { get; }

        public string Description { get; }

        public string CSharpType => Type switch
        {
            FluentVariableType.Number => "double",
            FluentVariableType.Date => "global::System.DateTimeOffset",
            _ => "string",
        };

        public string Argument => Type == FluentVariableType.Text ? Name + " ?? \"\"" : Name;
    }
}
