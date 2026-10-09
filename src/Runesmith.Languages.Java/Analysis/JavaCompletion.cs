using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Text;
using JavaModifiers = Runesmith.Languages.Java.ClassFiles.JavaModifiers;
using TextChange = Runesmith.Text.TextChange;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>What resolving a suggestion needs: the file it was made in, and what it stands for.</summary>
/// <param name="Symbol">A <see cref="LocalSymbol"/>, a field <see cref="MemberRef{T}"/>, a <see cref="TypeName"/> or a <see cref="PackageName"/>; null for keywords.</param>
/// <param name="Overloads">The methods a method suggestion stands for, all with its name.</param>
/// <param name="Import">The type an import is added for when the suggestion is accepted.</param>
internal sealed record JavaCompletionData(SemanticModel Model, object? Symbol, IReadOnlyList<MemberRef<MethodSymbol>>? Overloads = null, string? Import = null);

/// <summary>Completion for Java: finds what is being completed, produces the candidates once per word, and keeps them, so typing more of the
/// same word only filters the kept list again.</summary>
internal sealed class JavaCompletion
{
    private const byte LocalGroup = 0;
    private const byte MemberGroup = 1;
    private const byte TypeGroup = 2;
    private const byte KeywordGroup = 3;

    private readonly ConcurrentDictionary<string, KeptList> kept = new(SourceLibrary.PathComparer);
    private readonly ConcurrentDictionary<string, EditLog> edits = new(SourceLibrary.PathComparer);
    private readonly ConditionalWeakTable<JavaProjectModel, Lazy<TypeNameIndex>> libraryTypes = [];
    private readonly ConditionalWeakTable<JavaProjectModel, Tuple<int, TypeNameIndex>> sourceTypes = [];
    private int computed;

    /// <summary>Gets how many lists were computed, as opposed to filtered from a kept one.</summary>
    public int Computed => computed;

    /// <summary>Adds the suggestions at the query's position to the sink.</summary>
    public void Complete(SemanticModel model, CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken)
    {
        var snapshot = query.Document.Snapshot;
        var word = CompletionSink.DefaultWordSpan(snapshot, query.Offset);
        var wordSpan = TextSpan.FromBounds(word.Start, WordEnd(snapshot, query.Offset));
        var trigger = query.Trigger == CompletionTriggerKind.Character ? query.Character : null;
        if (kept.TryGetValue(query.Document.Path, out var list) && list.WordStart == word.Start && list.Project == model.Project.Model
            && (trigger is null || list.Trigger == trigger))
        {
            sink.SetWordSpan(wordSpan);
            list.AddMatching(sink);
            return;
        }

        var context = CompletionContext.Find(model.Tree, word.Start);
        if (trigger == '.' && context.Kind != CompletionContextKind.MemberAccess && context.Kind != CompletionContextKind.Import)
            context = CompletionContext.None;

        cancellationToken.ThrowIfCancellationRequested();
        var builder = new ListBuilder(this, model, word.Start);
        builder.Add(context);
        Interlocked.Increment(ref computed);
        var computedList = builder.ToList(trigger);
        if (!edits.TryGetValue(query.Document.Path, out var log) || log.EarliestSince(query.Document.Version) >= word.Start)
            kept[query.Document.Path] = computedList;

        sink.SetWordSpan(wordSpan);
        computedList.AddMatching(sink);
    }

    /// <summary>Forgets a document's kept list when an edit changes the text before its word.</summary>
    public void OnChanged(string path, int version, IReadOnlyList<TextChange> changes)
    {
        if (changes.Count == 0)
            return;

        var first = changes.Min(change => change.Span.Start);
        edits.GetOrAdd(path, _ => new EditLog()).Add(version, first);
        if (kept.TryGetValue(path, out var list) && first < list.WordStart)
            kept.TryRemove(path, out _);
    }

    public void Forget(string path)
    {
        kept.TryRemove(path, out _);
        edits.TryRemove(path, out _);
    }

    /// <summary>Builds the index of the JDK's and the libraries' types ahead of the first completion that needs it.</summary>
    public void WarmUp(JavaProjectModel project) => _ = LibraryTypes(project);

    /// <summary>Gets a suggestion's signature and documentation, and the import accepting it adds.</summary>
    public static CompletionDetails? Resolve(CompletionEntry entry)
    {
        if (entry.Data is not JavaCompletionData data)
            return null;

        var model = data.Model;
        string? detail = null;
        string? documentation = null;
        if (data.Overloads is { Count: > 0 } overloads)
        {
            detail = JavaSymbols.DescribeMethod(overloads[0]) + (overloads.Count > 1 ? $" (+{overloads.Count - 1} overloads)" : "");
            documentation = JavaSymbols.Documentation(model, overloads[0]);
        }
        else if (data.Symbol is { } symbol)
        {
            detail = JavaSymbols.Describe(model, symbol);
            documentation = JavaSymbols.Documentation(model, symbol);
        }

        IReadOnlyList<TextChange> changes = data.Import is { } import ? [ImportChange(model.Tree, import)] : [];
        return new CompletionDetails(detail, documentation, changes);
    }

    /// <summary>Gets the edit that adds an import: after the last import, or after the package declaration, or at the top of the file.</summary>
    public static TextChange ImportChange(JavaSyntaxTree tree, string binaryName)
    {
        var line = $"import {ClassNames.SourceName(binaryName)};";
        var root = tree.Root;
        if (root.Imports.Count > 0)
            return new TextChange(new TextSpan(root.Imports[^1].End, 0), "\n" + line);
        if (root.Package is { } package)
            return new TextChange(new TextSpan(package.End, 0), "\n\n" + line);
        return new TextChange(new TextSpan(0, 0), line + "\n\n");
    }

    private TypeNameIndex LibraryTypes(JavaProjectModel project) =>
        libraryTypes.GetValue(project, p => new Lazy<TypeNameIndex>(() => TypeNameIndex.Create(
            (p.Jdk is null ? p.Libraries : p.Libraries.Prepend(p.Jdk)).SelectMany(l => l.TypeNames)))).Value;

    private TypeNameIndex SourceTypes(ProjectSnapshot snapshot)
    {
        var version = snapshot.Sources.TypeSetVersion;
        if (sourceTypes.TryGetValue(snapshot.Model, out var cached) && cached.Item1 == version)
            return cached.Item2;

        var index = TypeNameIndex.Create(snapshot.Sources.TypeNames);
        sourceTypes.AddOrUpdate(snapshot.Model, Tuple.Create(version, index));
        return index;
    }

    private static int WordEnd(TextSnapshot snapshot, int offset)
    {
        var end = offset;
        while (end < snapshot.Length && (char.IsLetterOrDigit(snapshot[end]) || snapshot[end] == '_'))
            end++;
        return end;
    }

    /// <summary>Collects the candidates for one word.</summary>
    private sealed class ListBuilder(JavaCompletion owner, SemanticModel model, int wordStart)
    {
        private readonly List<CompletionCandidate> candidates = [];
        private readonly HashSet<string> labels = new(StringComparer.Ordinal);
        private readonly HashSet<string> typesInScope = new(StringComparer.Ordinal);
        private readonly ExpressionTyper typer = new(model);
        private bool offerUnimportedTypes;
        private Scope? scope;

        private Scope Scope => scope ??= new ScopeBuilder(typer).ScopeAt(wordStart, out _);

        private JavaVersion Version => model.Project.Version;

        public KeptList ToList(char? trigger) => new(wordStart, trigger, model.Project.Model, [.. candidates],
            offerUnimportedTypes ? [owner.SourceTypes(model.Project), owner.LibraryTypes(model.Project.Model)] : [], typesInScope, model);

        public void Add(CompletionContext context)
        {
            switch (context.Kind)
            {
                case CompletionContextKind.MemberAccess:
                    AddMemberAccess(context.Dot!.Value);
                    break;
                case CompletionContextKind.Import:
                    AddImport(context.Qualifier, context.IsStaticImport);
                    break;
                case CompletionContextKind.ModuleImport:
                    AddModules(context.Qualifier);
                    break;
                case CompletionContextKind.Package:
                    AddPackages(context.Qualifier);
                    break;
                case CompletionContextKind.Annotation or CompletionContextKind.NewType or CompletionContextKind.Type:
                    AddTypes();
                    if (context.Kind != CompletionContextKind.Annotation)
                        AddKeywords(KeywordPlace.Type);
                    break;
                case CompletionContextKind.TypeHeader:
                    AddKeywords(KeywordPlace.TypeHeader);
                    break;
                case CompletionContextKind.ClassMember:
                    AddTypes();
                    AddKeywords(KeywordPlace.Member, inInterface: Scope.EnclosingType()?.Type?.Kind is ClassKind.Interface or ClassKind.Annotation);
                    break;
                case CompletionContextKind.TopLevel:
                    if (model.Tree.Root.IsCompactSourceFile || JavaFeatures.IsAvailable(JavaFeature.CompactSourceFiles, Version))
                        AddTypes();
                    AddKeywords(KeywordPlace.TopLevel, compact: model.Tree.Root.IsCompactSourceFile);
                    break;
                case CompletionContextKind.Statement:
                    AddNames();
                    AddKeywords(KeywordPlace.Statement);
                    break;
                case CompletionContextKind.Expression:
                    AddNames();
                    AddKeywords(KeywordPlace.Expression);
                    break;
            }
        }

        private void AddNames()
        {
            AddLocals();
            AddScopeMembers();
            AddStaticImports();
            AddTypes();
        }

        private void AddLocals()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = Scope; current is not null; current = current.Parent)
            {
                for (var i = current.Locals.Count - 1; i >= 0; i--)
                {
                    var local = current.Locals[i];
                    if (local.NameSpan.Start < wordStart && seen.Add(local.Name) && labels.Add(local.Name))
                        candidates.Add(new CompletionCandidate(local.Name, CompletionKind.Variable, LocalGroup, Data: new JavaCompletionData(model, local)));
                }
            }
        }

        private void AddScopeMembers()
        {
            for (var current = Scope; current is not null; current = current.Parent)
            {
                if (current.Kind != ScopeKind.Type || current.Type is not { } type)
                    continue;

                var staticOnly = Scope.IsStaticBelow(current);
                AddMembers(SemanticModel.SelfType(type), staticOnly ? MemberFilter.Static : MemberFilter.All);
            }
        }

        private void AddStaticImports()
        {
            foreach (var (type, member) in Scope.File.StaticImports)
                AddMembers(new ClassTypeReference(type, []), MemberFilter.Static, member);
            foreach (var type in Scope.File.StaticOnDemandImports)
                AddMembers(new ClassTypeReference(type, []), MemberFilter.Static);
        }

        private enum MemberFilter
        {
            All,
            Static,
            Instance,
        }

        private void AddMembers(ClassTypeReference type, MemberFilter filter, string? onlyName = null)
        {
            foreach (var field in model.Fields(type, onlyName))
            {
                if (!Matches(field.Member.IsStatic, filter) || !IsAccessible(field.Member.Modifiers, field.Owner) || !labels.Add(field.Member.Name))
                    continue;
                var kind = field.Member.IsEnumConstant ? CompletionKind.EnumMember : field.Member.IsStatic && (field.Member.Modifiers & JavaModifiers.Final) != 0
                    ? CompletionKind.Constant
                    : CompletionKind.Field;
                candidates.Add(new CompletionCandidate(field.Member.Name, kind, MemberGroup, JavaTypes.Display(field.Substitute(field.Member.Type)),
                    Data: new JavaCompletionData(model, field)));
            }

            foreach (var group in model.Methods(type, onlyName)
                .Where(m => Matches(m.Member.IsStatic, filter) && IsAccessible(m.Member.Modifiers, m.Owner))
                .GroupBy(m => m.Member.Name, StringComparer.Ordinal))
            {
                if (!labels.Add(group.Key))
                    continue;
                var overloads = group.ToList();
                var first = overloads[0];
                var detail = $"{JavaTypes.Display(first.Substitute(first.Member.ReturnType))} {group.Key}({string.Join(", ", JavaSymbols.Parameters(first))})";
                candidates.Add(new CompletionCandidate(group.Key, CompletionKind.Method, MemberGroup, detail,
                    Data: new JavaCompletionData(model, first, overloads)));
            }
        }

        private static bool Matches(bool isStatic, MemberFilter filter) => filter switch
        {
            MemberFilter.Static => isStatic,
            MemberFilter.Instance => !isStatic,
            _ => true,
        };

        // Private members are visible inside the same top-level class, package-private ones inside the same package.
        private bool IsAccessible(JavaModifiers modifiers, ClassSymbol owner)
        {
            if ((modifiers & (JavaModifiers.Public | JavaModifiers.Protected)) != 0)
                return true;
            if ((modifiers & JavaModifiers.Private) != 0)
                return model.IsLocalType(owner.BinaryName) || model.File.Types.Any(t => t.Outer is null && Outermost(owner.BinaryName) == t.BinaryName);
            return ClassNames.PackageName(owner.BinaryName) == model.FileScope.PackageName || model.IsLocalType(owner.BinaryName);
        }

        private static string Outermost(string binaryName)
        {
            var dot = binaryName.LastIndexOf('.');
            var dollar = binaryName.IndexOf('$', dot + 1);
            return dollar < 0 ? binaryName : binaryName[..dollar];
        }

        private void AddTypes()
        {
            for (var current = Scope; current is not null; current = current.Parent)
            {
                foreach (var parameter in current.TypeParameters)
                    AddType(parameter.Name, null, CompletionKind.TypeParameter);
                foreach (var (simple, binary) in current.LocalTypes)
                    AddType(simple, binary, CompletionKind.Class);
                if (current.Kind == ScopeKind.Type && current.Type is { } type)
                {
                    foreach (var parameter in type.TypeParameters)
                        AddType(parameter.Name, null, CompletionKind.TypeParameter);
                    foreach (var nested in model.MemberTypes(type.BinaryName))
                        AddType(ClassNames.SimpleName(nested), nested, KindOf(nested));
                    if (model.IsLocalType(type.BinaryName) && type.SimpleName.Length > 0)
                        AddType(type.SimpleName, type.BinaryName, CompletionKind.Class);
                }
            }

            foreach (var (simple, binary) in model.FileScope.TypesInScope())
                AddType(simple, binary, KindOf(binary));
            offerUnimportedTypes = true;
        }

        private void AddType(string simpleName, string? binaryName, CompletionKind kind)
        {
            typesInScope.Add(simpleName);
            if (!labels.Add(simpleName))
                return;
            object? symbol = binaryName is null ? null : new TypeName(binaryName);
            candidates.Add(new CompletionCandidate(simpleName, kind, TypeGroup, binaryName is null ? null : ClassNames.PackageName(binaryName),
                Data: symbol is null ? null : new JavaCompletionData(model, symbol)));
        }

        // Only source types tell their kind without reading a class file; library types read as classes.
        private CompletionKind KindOf(string binaryName) => model.Project.FindSourceType(binaryName)?.Declaration switch
        {
            InterfaceDeclarationSyntax or AnnotationTypeDeclarationSyntax => CompletionKind.Interface,
            EnumDeclarationSyntax => CompletionKind.Enum,
            RecordDeclarationSyntax => CompletionKind.Struct,
            _ => CompletionKind.Class,
        };

        private void AddKeywords(KeywordPlace place, bool inInterface = false, bool compact = false)
        {
            var inSwitchExpression = place == KeywordPlace.Statement && model.Tree.GetPath(wordStart).Any(n => n is SwitchExpressionSyntax);
            foreach (var keyword in JavaKeywords.At(place, Version, inInterface, inSwitchExpression, compact))
            {
                if (labels.Add(keyword))
                    candidates.Add(new CompletionCandidate(keyword, CompletionKind.Keyword, KeywordGroup));
            }
        }

        private void AddMemberAccess(JavaToken dot)
        {
            var path = model.Tree.GetPath(dot.Span.Start);
            for (var i = path.Count - 1; i >= 0; i--)
            {
                switch (path[i])
                {
                    case FieldAccessExpressionSyntax access when access.Target.End <= dot.Span.Start:
                        AddMembersOf(access.Target, dot);
                        return;
                    case MethodInvocationExpressionSyntax { Target: { } target } call when target.End <= dot.Span.Start && call.Name.Start >= dot.Span.End:
                        AddMembersOf(target, dot);
                        return;
                    case ClassTypeSyntax { Qualifier: { } qualifier } when qualifier.End <= dot.Span.Start:
                        if (model.ResolveQualifierAsType(qualifier, Scope) is { } outer)
                            AddMemberTypes(outer);
                        else
                            AddPackageContents(qualifier.QualifiedName);
                        return;
                    case NameSyntax name:
                        AddPackageContents(string.Join('.', name.Parts.TakeWhile(p => p.End <= dot.Span.Start).Select(p => p.Text)));
                        return;
                    case BlockSyntax or ClassBodySyntax:
                        return;
                }
            }
        }

        private void AddMembersOf(ExpressionSyntax target, JavaToken dot)
        {
            var scopeAtDot = new ScopeBuilder(typer).ScopeAt(dot.Span.Start, out _);
            scope = scopeAtDot;
            var typed = typer.TypeOf(target, scopeAtDot);
            switch (typed.Kind)
            {
                case ExpressionKind.Package:
                    AddPackageContents((string)typed.Symbol!);
                    break;
                case ExpressionKind.Type when typed.Type is ClassTypeReference type:
                    AddMembers(type, MemberFilter.Static);
                    AddMemberTypes(type.BinaryName);
                    AddKeyword("class");
                    if (Enclosing(scopeAtDot, type.BinaryName))
                    {
                        AddKeyword("this");
                        AddKeyword("super");
                    }

                    break;
                case ExpressionKind.Value:
                    if (typed.Type is ArrayTypeReference)
                    {
                        candidates.Add(new CompletionCandidate("length", CompletionKind.Field, MemberGroup, "int"));
                        labels.Add("length");
                    }

                    if (ExpressionTyper.ReceiverClass(typed.Type, scopeAtDot) is { } receiver)
                        AddMembers(receiver, target is SuperExpressionSyntax or ThisExpressionSyntax ? MemberFilter.All : MemberFilter.Instance);
                    break;
            }
        }

        private static bool Enclosing(Scope scope, string binaryName)
        {
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Kind == ScopeKind.Type && current.TypeName == binaryName)
                    return true;
            }

            return false;
        }

        private void AddMemberTypes(string owner)
        {
            foreach (var nested in model.MemberTypes(owner))
                AddType(ClassNames.SimpleName(nested), nested, KindOf(nested));
        }

        private void AddKeyword(string keyword)
        {
            if (labels.Add(keyword))
                candidates.Add(new CompletionCandidate(keyword, CompletionKind.Keyword, KeywordGroup));
        }

        private void AddPackageContents(string package)
        {
            foreach (var child in model.Project.SubPackages(package))
            {
                var simple = child[(child.LastIndexOf('.') + 1)..];
                if (labels.Add(simple))
                    candidates.Add(new CompletionCandidate(simple, CompletionKind.Module, TypeGroup, Data: new JavaCompletionData(model, new PackageName(child))));
            }

            foreach (var type in model.Project.ClassPath.GetTypes(package))
            {
                if (ClassNames.OuterName(type) is null && !ClassNames.IsAnonymousOrLocal(type))
                    AddType(ClassNames.SimpleName(type), type, KindOf(type));
            }
        }

        private void AddImport(string qualifier, bool isStatic)
        {
            if (qualifier.Length == 0)
            {
                AddKeyword("static");
                if (!isStatic && JavaFeatures.GetSupport(JavaFeature.ModuleImports, Version) != JavaFeatureSupport.NeedsNewerRelease)
                    AddKeyword("module");
            }

            if (isStatic && model.FileScope.ResolveCanonical(qualifier) is { } type)
            {
                AddMembers(new ClassTypeReference(type, []), MemberFilter.Static);
                AddMemberTypes(type);
                return;
            }

            if (model.FileScope.ResolveCanonical(qualifier) is { } outer)
            {
                AddMemberTypes(outer);
                return;
            }

            AddPackageContents(qualifier);
        }

        private void AddModules(string qualifier)
        {
            var prefix = qualifier.Length == 0 ? "" : qualifier + ".";
            foreach (var module in model.Project.ModuleNames)
            {
                if (!module.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                var rest = module[prefix.Length..];
                if (labels.Add(rest))
                    candidates.Add(new CompletionCandidate(rest, CompletionKind.Module, TypeGroup, module));
            }
        }

        private void AddPackages(string qualifier)
        {
            foreach (var child in model.Project.SubPackages(qualifier))
            {
                var simple = child[(child.LastIndexOf('.') + 1)..];
                if (labels.Add(simple))
                    candidates.Add(new CompletionCandidate(simple, CompletionKind.Module, TypeGroup));
            }
        }
    }

    /// <summary>A list computed for one word, with each candidate's character mask; types that are not imported yet are added from shared
    /// indexes by the first typed letter.</summary>
    private sealed class KeptList(int wordStart, char? trigger, JavaProjectModel project, CompletionCandidate[] candidates, TypeNameIndex[] unimported,
        HashSet<string> typesInScope, SemanticModel model)
    {
        private readonly ulong[] masks = [.. candidates.Select(c => CompletionMatcher.MaskOf(c.FilterText ?? c.Label))];

        public int WordStart => wordStart;

        public char? Trigger => trigger;

        public JavaProjectModel Project => project;

        public void AddMatching(CompletionSink sink)
        {
            var typed = sink.Typed;
            var mask = CompletionMatcher.MaskOf(typed);
            for (var i = 0; i < candidates.Length; i++)
            {
                if ((mask & ~masks[i]) == 0)
                    sink.Add(candidates[i]);
            }

            if (typed.Length == 0)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var index in unimported)
            {
                foreach (var entry in index.StartingWith(typed[0]))
                {
                    if ((mask & ~entry.Mask) != 0 || typesInScope.Contains(entry.SimpleName) || !seen.Add(entry.BinaryName))
                        continue;
                    sink.Add(new CompletionCandidate(entry.SimpleName, CompletionKind.Class, TypeGroup, entry.Package,
                        Data: new JavaCompletionData(model, new TypeName(entry.BinaryName), Import: entry.BinaryName)));
                }
            }
        }
    }

    /// <summary>The earliest offset each recent version's edits started at.</summary>
    private sealed class EditLog
    {
        private const int Capacity = 64;
        private readonly Queue<(int Version, int Start)> entries = new();

        public void Add(int version, int start)
        {
            lock (entries)
            {
                entries.Enqueue((version, start));
                if (entries.Count > Capacity)
                    entries.Dequeue();
            }
        }

        public int EarliestSince(int version)
        {
            lock (entries)
            {
                if (entries.Count == Capacity && entries.Peek().Version > version + 1)
                    return 0;
                return entries.Where(entry => entry.Version > version).Select(entry => entry.Start).DefaultIfEmpty(int.MaxValue).Min();
            }
        }
    }
}
