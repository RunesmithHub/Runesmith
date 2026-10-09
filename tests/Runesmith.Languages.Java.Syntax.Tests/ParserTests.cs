namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ReadsPackageAndImports()
    {
        var tree = Java.ParseClean("""
            @Deprecated package com.example;
            import java.util.List;
            import java.util.*;
            import static java.lang.Math.max;
            import static java.lang.Math.*;
            import module java.base;
            class C {}
            """);
        Assert.Equal("com.example", tree.Root.Package!.Name.ToString());
        Assert.Single(tree.Root.Package.Annotations);
        Assert.Equal([ImportKind.Single, ImportKind.OnDemand, ImportKind.Static, ImportKind.StaticOnDemand, ImportKind.Module],
            tree.Root.Imports.Select(i => i.Kind));
        Assert.Equal(["java.util.List", "java.util", "java.lang.Math.max", "java.lang.Math", "java.base"],
            tree.Root.Imports.Select(i => i.Name.ToString()));
    }

    [Fact]
    public void AnImportOfAPackageNamedModuleIsNotAModuleImport()
    {
        var tree = Java.ParseClean("import module.Thing; class C {}");
        Assert.Equal(ImportKind.Single, tree.Root.Imports[0].Kind);
        Assert.Equal("module.Thing", tree.Root.Imports[0].Name.ToString());
    }

    [Fact]
    public void ReadsModuleDeclarations()
    {
        var tree = Java.ParseClean("""
            import java.util.spi.ToolProvider;
            @Deprecated
            open module com.example.app {
                requires transitive static java.sql;
                requires transitive;
                exports com.example.api to com.example.client, com.example.test;
                opens com.example.internal;
                uses ToolProvider;
                provides ToolProvider with com.example.Tool;
            }
            """);
        var module = tree.Root.Module!;
        Assert.True(module.IsOpen);
        Assert.Equal("com.example.app", module.Name.ToString());
        Assert.Equal(
            [ModuleDirectiveKind.Requires, ModuleDirectiveKind.Requires, ModuleDirectiveKind.Exports, ModuleDirectiveKind.Opens,
             ModuleDirectiveKind.Uses, ModuleDirectiveKind.Provides],
            module.Directives.Select(d => d.Kind));
        Assert.Equal(RequiresModifiers.Transitive | RequiresModifiers.Static, module.Directives[0].Modifiers);
        Assert.Equal("transitive", module.Directives[1].Name.ToString());
        Assert.Equal(2, module.Directives[2].Targets.Count);
        Assert.Equal("com.example.Tool", module.Directives[5].Targets[0].ToString());
    }

    [Fact]
    public void ReadsClassesWithEveryPart()
    {
        var tree = Java.ParseClean("""
            public abstract sealed class Shape<T extends Number & Comparable<T>, U> extends Base<T> implements A, B<U> permits Circle, Square {
                static { }
                { }
                protected Shape() throws Exception { this(1); }
                <V> Shape(V v) { super(); }
                abstract <R> R map(java.util.function.Function<? super T, ? extends R> f) throws X, Y;
                int values()[] { return null; }
                private static final int A = 1, B[] = {1, 2}, C;
                void varargs(final @Ann int... values) { }
                void receiver(Shape<T, U> this, int x) { }
            }
            """);
        var shape = Assert.IsType<ClassDeclarationSyntax>(tree.Root.Members[0]);
        Assert.True(shape.Modifiers.Has(JavaModifiers.Public | JavaModifiers.Abstract | JavaModifiers.Sealed));
        Assert.Equal(2, shape.TypeParameters!.Parameters.Count);
        Assert.Equal(2, shape.TypeParameters.Parameters[0].Bounds.Count);
        Assert.Equal("Base", ((ClassTypeSyntax)shape.Extends!).Name.Text);
        Assert.Equal(2, shape.Implements.Count);
        Assert.Equal(2, shape.Permits.Count);

        var members = shape.Body.Members;
        Assert.True(Assert.IsType<InitializerDeclarationSyntax>(members[0]).IsStatic);
        Assert.False(Assert.IsType<InitializerDeclarationSyntax>(members[1]).IsStatic);
        Assert.Single(Assert.IsType<ConstructorDeclarationSyntax>(members[2]).Throws);
        Assert.NotNull(Assert.IsType<ConstructorDeclarationSyntax>(members[3]).TypeParameters);
        var map = Assert.IsType<MethodDeclarationSyntax>(members[4]);
        Assert.Null(map.Body);
        Assert.Equal(2, map.Throws.Count);
        var argument = (ClassTypeSyntax)map.Parameters.Parameters[0].Type!;
        Assert.Equal("java.util.function.Function", argument.QualifiedName);
        Assert.Equal(WildcardBoundKind.Super, ((WildcardTypeSyntax)argument.TypeArguments!.Arguments[0]).BoundKind);
        Assert.Single(Assert.IsType<MethodDeclarationSyntax>(members[5]).Dimensions);
        var field = Assert.IsType<FieldDeclarationSyntax>(members[6]);
        Assert.Equal(["A", "B", "C"], field.Variables.Select(v => v.Name.Text));
        Assert.IsType<ArrayInitializerExpressionSyntax>(field.Variables[1].Initializer);
        var varargs = Assert.IsType<MethodDeclarationSyntax>(members[7]).Parameters.Parameters[0];
        Assert.True(varargs.IsVarargs);
        Assert.True(varargs.Modifiers.Has(JavaModifiers.Final));
        Assert.True(Assert.IsType<MethodDeclarationSyntax>(members[8]).Parameters.Parameters[0].IsReceiver);
    }

    [Fact]
    public void ReadsInterfacesEnumsAndAnnotationTypes()
    {
        var tree = Java.ParseClean("""
            interface I<T> extends A, B { default void d() {} static void s() {} private void p() {} int X = 1; }
            enum E implements I<String> { A, B(1) { void m() {} }, @Deprecated C; E() {} E(int x) {} }
            enum Empty { }
            enum Trailing { A, B, }
            @interface Ann { String value() default "x"; int[] list() default {1, 2}; Class<?> type(); }
            non-sealed class N extends S {}
            """);
        var i = Assert.IsType<InterfaceDeclarationSyntax>(tree.Root.Members[0]);
        Assert.Equal(2, i.Extends.Count);
        Assert.True(i.Body.Members[0].Modifiers.Has(JavaModifiers.Default));
        var e = Assert.IsType<EnumDeclarationSyntax>(tree.Root.Members[1]);
        Assert.Equal(["A", "B", "C"], e.Body.EnumConstants.Select(c => c.Name.Text));
        Assert.NotNull(e.Body.EnumConstants[1].Body);
        Assert.Equal(2, e.Body.Members.Count);
        Assert.Equal(2, Assert.IsType<EnumDeclarationSyntax>(tree.Root.Members[3]).Body.EnumConstants.Count);
        var annotation = Assert.IsType<AnnotationTypeDeclarationSyntax>(tree.Root.Members[4]);
        Assert.IsType<LiteralExpressionSyntax>(((MethodDeclarationSyntax)annotation.Body.Members[0]).DefaultValue);
        Assert.IsType<ElementValueArrayInitializerSyntax>(((MethodDeclarationSyntax)annotation.Body.Members[1]).DefaultValue);
        Assert.True(tree.Root.Members[5].Modifiers.Has(JavaModifiers.NonSealed));
    }

    [Fact]
    public void ReadsRecords()
    {
        var tree = Java.ParseClean("""
            record Point<T>(@Ann int x, T... rest) implements Shape {
                Point { if (x < 0) throw new IllegalArgumentException(); }
                Point(int x) { this(x, null); }
                static int count;
                record Inner() {}
            }
            """);
        var record = Assert.IsType<RecordDeclarationSyntax>(tree.Root.Members[0]);
        Assert.Equal(["x", "rest"], record.Header.Components.Select(c => c.Name.Text));
        Assert.True(record.Header.Components[1].IsVarargs);
        Assert.IsType<CompactConstructorDeclarationSyntax>(record.Body.Members[0]);
        Assert.IsType<ConstructorDeclarationSyntax>(record.Body.Members[1]);
        Assert.IsType<RecordDeclarationSyntax>(record.Body.Members[3]);
    }

    [Fact]
    public void ContextualKeywordsAreNamesWhereTheyAreNotKeywords()
    {
        Java.ParseClean("""
            class record { }
            class C {
                int var, yield, record, sealed, permits, module, open, when, non;
                void m() {
                    var var = 1;
                    record = sealed - non - permits;
                    yield = 2;
                    yield += 1;
                    this.yield(1);
                    int record = 3;
                    Object when = null;
                    String module = "m";
                }
                void yield(int x) {}
                void sealed() { sealed(); }
            }
            """);
    }

    [Fact]
    public void ReadsLocalDeclarationsAndTypes()
    {
        var block = Java.Statement<BlockSyntax>("""
            {
                int a = 1, b[] = {};
                final List<Map<String, int[]>> list = new ArrayList<>();
                var inferred = list;
                java.util.Map.Entry<String, ? extends Number>[] entries = null;
                Outer<String>.Inner<Integer> inner = null;
                @Ann String annotated;
                int @Ann [] annotatedArray;
                class Local {}
                record LocalRecord(int x) {}
                interface LocalInterface {}
                enum LocalEnum { A }
            }
            """);
        var statements = block.Statements;
        Assert.Equal(2, Assert.IsType<LocalVariableDeclarationSyntax>(statements[0]).Variables.Count);
        var list = Assert.IsType<LocalVariableDeclarationSyntax>(statements[1]);
        Assert.True(list.Modifiers.Has(JavaModifiers.Final));
        var map = (ClassTypeSyntax)((ClassTypeSyntax)list.Type).TypeArguments!.Arguments[0];
        Assert.IsType<ArrayTypeSyntax>(map.TypeArguments!.Arguments[1]);
        Assert.IsType<VarTypeSyntax>(Assert.IsType<LocalVariableDeclarationSyntax>(statements[2]).Type);
        var entries = Assert.IsType<ArrayTypeSyntax>(Assert.IsType<LocalVariableDeclarationSyntax>(statements[3]).Type);
        Assert.Equal("java.util.Map.Entry", ((ClassTypeSyntax)entries.ElementType).QualifiedName);
        var inner = (ClassTypeSyntax)Assert.IsType<LocalVariableDeclarationSyntax>(statements[4]).Type;
        Assert.NotNull(inner.Qualifier!.TypeArguments);
        Assert.Single(Assert.IsType<ArrayTypeSyntax>(Assert.IsType<LocalVariableDeclarationSyntax>(statements[6]).Type).Dimensions[0].Annotations);
        Assert.All(statements.Skip(7), s => Assert.IsType<LocalTypeDeclarationStatementSyntax>(s));
    }

    [Fact]
    public void ReadsStatements()
    {
        var block = Java.Statement<BlockSyntax>("""
            {
                if (a) b(); else if (c) d(); else { }
                while (x) x--;
                do { x++; } while (x < 10);
                for (int i = 0, j = 1; i < j; i++, j--) ;
                for (;;) break;
                for (i = 0; i < 1; i++) continue;
                for (final var item : items) { }
                for (Map.Entry<K, V> e : map.entrySet()) { }
                outer: for (int[] row : rows) { inner: while (true) { continue outer; } }
                return;
                throw new Error();
                try (var in = open(); out) { } catch (IOException | RuntimeException e) { } finally { }
                try { } catch (final Exception e) { }
                synchronized (lock) { }
                assert x > 0 : "positive";
                switch (x) { case 1: case 2: f(); break; default: g(); }
                ;
                label: ;
            }
            """);
        Assert.Equal(
            [typeof(IfStatementSyntax), typeof(WhileStatementSyntax), typeof(DoStatementSyntax), typeof(ForStatementSyntax), typeof(ForStatementSyntax),
             typeof(ForStatementSyntax), typeof(ForEachStatementSyntax), typeof(ForEachStatementSyntax), typeof(LabeledStatementSyntax),
             typeof(ReturnStatementSyntax), typeof(ThrowStatementSyntax), typeof(TryStatementSyntax), typeof(TryStatementSyntax),
             typeof(SynchronizedStatementSyntax), typeof(AssertStatementSyntax), typeof(SwitchStatementSyntax), typeof(EmptyStatementSyntax),
             typeof(LabeledStatementSyntax)],
            block.Statements.Select(s => s.GetType()));
        var @if = (IfStatementSyntax)block.Statements[0];
        Assert.IsType<IfStatementSyntax>(@if.Else);
        var @for = (ForStatementSyntax)block.Statements[3];
        Assert.Single(@for.Initializers);
        Assert.Equal(2, @for.Updates.Count);
        var empty = (ForStatementSyntax)block.Statements[4];
        Assert.Null(empty.Condition);
        Assert.IsType<ExpressionStatementSyntax>(((ForStatementSyntax)block.Statements[5]).Initializers[0]);
        var forEach = (ForEachStatementSyntax)block.Statements[6];
        Assert.IsType<VarTypeSyntax>(forEach.Type);
        Assert.True(forEach.Modifiers.Has(JavaModifiers.Final));
        var @try = (TryStatementSyntax)block.Statements[11];
        Assert.Equal(2, @try.Resources.Count);
        Assert.IsType<UnionTypeSyntax>(@try.Catches[0].Type);
        Assert.NotNull(@try.Finally);
        var @switch = (SwitchStatementSyntax)block.Statements[15];
        Assert.Equal(3, @switch.Cases.Count);
        Assert.Equal(2, @switch.Cases[1].Statements.Count);
    }

    [Fact]
    public void ReadsExplicitConstructorInvocations()
    {
        var tree = Java.ParseClean("""
            class C extends B {
                C() { this(1); }
                C(int x) { <String>super(x); }
                C(Outer o) { o.super(); }
                C(Outer o, int x) { o.<T>super(x); }
                C(long x) { int y = 1; super(y); }
            }
            """);
        ExplicitConstructorInvocationSyntax First(int member) =>
            Assert.IsType<ExplicitConstructorInvocationSyntax>(((ConstructorDeclarationSyntax)Java.Class(tree).Body.Members[member]).Body.Statements[0]);
        Assert.False(First(0).IsSuper);
        Assert.NotNull(First(1).TypeArguments);
        Assert.IsType<NameExpressionSyntax>(First(2).Qualifier);
        Assert.NotNull(First(3).TypeArguments);
        var flexible = ((ConstructorDeclarationSyntax)Java.Class(tree).Body.Members[4]).Body.Statements;
        Assert.IsType<ExplicitConstructorInvocationSyntax>(flexible[1]);
    }

    [Fact]
    public void ReadsOperatorsByPrecedence()
    {
        var expression = Java.Expression<BinaryExpressionSyntax>("a || b && c | d ^ e & f == g < h << i + j * k");
        Assert.Equal(BinaryOperator.LogicalOr, expression.Operator);
        var right = (BinaryExpressionSyntax)expression.Right;
        Assert.Equal(BinaryOperator.LogicalAnd, right.Operator);
        var chain = new List<BinaryOperator>();
        for (ExpressionSyntax e = right; e is BinaryExpressionSyntax binary; e = binary.Right)
            chain.Add(binary.Operator);
        Assert.Equal(
            [BinaryOperator.LogicalAnd, BinaryOperator.BitwiseOr, BinaryOperator.ExclusiveOr, BinaryOperator.BitwiseAnd, BinaryOperator.Equals,
             BinaryOperator.LessThan, BinaryOperator.LeftShift, BinaryOperator.Add, BinaryOperator.Multiply],
            chain);
    }

    [Fact]
    public void OperatorsOfEqualPrecedenceGroupToTheLeft()
    {
        var expression = Java.Expression<BinaryExpressionSyntax>("a - b - c");
        Assert.IsType<BinaryExpressionSyntax>(expression.Left);
        Assert.IsType<NameExpressionSyntax>(expression.Right);
    }

    [Fact]
    public void ReadsShiftsFromAdjacentGreaterThanTokens()
    {
        Assert.Equal(BinaryOperator.RightShift, Java.Expression<BinaryExpressionSyntax>("a >> 2").Operator);
        Assert.Equal(BinaryOperator.UnsignedRightShift, Java.Expression<BinaryExpressionSyntax>("a >>> 2").Operator);
        var spaced = Java.Parse("class C { Object f = a > > 2; }");
        Assert.NotEmpty(spaced.Diagnostics);
    }

    [Fact]
    public void AssignmentsAndConditionalsGroupToTheRight()
    {
        var assignment = Java.Statement<ExpressionStatementSyntax>("a = b += c >>>= 2;");
        var outer = Assert.IsType<AssignmentExpressionSyntax>(assignment.Expression);
        Assert.Equal(AssignmentOperator.Add, Assert.IsType<AssignmentExpressionSyntax>(outer.Right).Operator);
        var conditional = Java.Expression<ConditionalExpressionSyntax>("a ? b : c ? d : e");
        Assert.IsType<ConditionalExpressionSyntax>(conditional.WhenFalse);
    }

    [Theory]
    [InlineData("(int) x", true)]
    [InlineData("(int[]) x", true)]
    [InlineData("(int) -x", true)]
    [InlineData("(String) x", true)]
    [InlineData("(String) (x)", true)]
    [InlineData("(String) !x", true)]
    [InlineData("(List<String>) x", true)]
    [InlineData("(java.util.List<?>[]) x", true)]
    [InlineData("(Runnable & java.io.Serializable) () -> {}", true)]
    [InlineData("(Function<String, String>) s -> s", true)]
    [InlineData("(a) - b", false)]
    [InlineData("(a) + b", false)]
    [InlineData("(a)", false)]
    [InlineData("(a.b) * c", false)]
    [InlineData("(a < b)", false)]
    public void TellsCastsFromParentheses(string expression, bool isCast)
    {
        var tree = Java.ParseClean($"class C {{ Object f = {expression}; }}");
        var initializer = ((FieldDeclarationSyntax)Java.Class(tree).Body.Members[0]).Variables[0].Initializer!;
        Assert.Equal(isCast, initializer is CastExpressionSyntax);
    }

    [Theory]
    [InlineData("x -> x", 1, false)]
    [InlineData("_ -> 1", 1, false)]
    [InlineData("() -> {}", 0, true)]
    [InlineData("(a, b) -> a + b", 2, true)]
    [InlineData("(int a, final String b) -> a", 2, true)]
    [InlineData("(var a, var b) -> a", 2, true)]
    [InlineData("(String... args) -> args", 1, true)]
    [InlineData("(a) -> (b) -> a", 1, true)]
    public void ReadsLambdas(string expression, int parameters, bool hasParentheses)
    {
        var lambda = Java.Expression<LambdaExpressionSyntax>(expression);
        Assert.Equal(parameters, lambda.Parameters.Count);
        Assert.Equal(hasParentheses, lambda.HasParentheses);
    }

    [Theory]
    [InlineData("String::valueOf", false)]
    [InlineData("this::run", false)]
    [InlineData("super::run", false)]
    [InlineData("ArrayList::new", true)]
    [InlineData("int[]::new", true)]
    [InlineData("String[]::clone", false)]
    [InlineData("List<String>::size", false)]
    [InlineData("java.util.Map.Entry<K, V>::getKey", false)]
    [InlineData("Outer<A>.Inner<B>::new", true)]
    [InlineData("a.b()::<String>c", false)]
    [InlineData("List::<String>of", false)]
    public void ReadsMethodReferences(string expression, bool isConstructor)
    {
        var reference = Java.Expression<MethodReferenceExpressionSyntax>(expression);
        Assert.Equal(isConstructor, reference.IsConstructorReference);
    }

    [Fact]
    public void TellsGenericsFromComparisons()
    {
        var comparison = Java.Expression<BinaryExpressionSyntax>("a < b && c > d");
        Assert.Equal(BinaryOperator.LogicalAnd, comparison.Operator);
        var call = Java.Expression<MethodInvocationExpressionSyntax>("Collections.<String>emptyList()");
        Assert.NotNull(call.TypeArguments);
        var nested = Java.Statement<LocalVariableDeclarationSyntax>("Map<String, List<Map<K, V>>> m;");
        Assert.IsType<ClassTypeSyntax>(nested.Type);
        var shifts = Java.Statement<LocalVariableDeclarationSyntax>("boolean b = i < n >> 1;");
        Assert.IsType<BinaryExpressionSyntax>(shifts.Variables[0].Initializer);
    }

    [Fact]
    public void ReadsPrimaryAndPostfixExpressions()
    {
        Java.Expression<ClassLiteralExpressionSyntax>("String.class");
        Java.Expression<ClassLiteralExpressionSyntax>("int[][].class");
        Java.Expression<ClassLiteralExpressionSyntax>("void.class");
        Java.Expression<ClassLiteralExpressionSyntax>("java.util.List.class");
        Assert.NotNull(Java.Expression<ThisExpressionSyntax>("Outer.this").Qualifier);
        Java.Expression<MethodInvocationExpressionSyntax>("Outer.super.toString()");
        Java.Expression<ArrayAccessExpressionSyntax>("a[1][2]");
        Assert.Equal(UnaryOperator.PostIncrement, Java.Expression<UnaryExpressionSyntax>("a.b[c]++").Operator);
        Assert.Equal(UnaryOperator.LogicalNot, Java.Expression<UnaryExpressionSyntax>("!a").Operator);
        Assert.Equal(UnaryOperator.Minus, Java.Expression<UnaryExpressionSyntax>("-~a").Operator);
        Java.Expression<FieldAccessExpressionSyntax>("a().b");
        Java.Expression<FieldAccessExpressionSyntax>("new int[1].length");
        Java.Expression<FieldAccessExpressionSyntax>("\"s\".length");
        Java.Expression<MethodInvocationExpressionSyntax>("this.<T>m()");
    }

    [Fact]
    public void ReadsObjectAndArrayCreation()
    {
        var anonymous = Java.Expression<ObjectCreationExpressionSyntax>("new Comparator<>() { public int compare(Object a, Object b) { return 0; } }");
        Assert.True(((ClassTypeSyntax)anonymous.Type).TypeArguments!.IsDiamond);
        Assert.Single(anonymous.Body!.Members);
        var inner = Java.Expression<ObjectCreationExpressionSyntax>("outer.new <String>Inner<Integer>(1)");
        Assert.NotNull(inner.Outer);
        Assert.NotNull(inner.ConstructorTypeArguments);
        var sized = Java.Expression<ArrayCreationExpressionSyntax>("new int[3][][]");
        Assert.Equal(3, sized.Dimensions.Count);
        Assert.NotNull(sized.Dimensions[0].Size);
        var initialized = Java.Expression<ArrayCreationExpressionSyntax>("new String[][] { {\"a\"}, {}, }");
        Assert.Equal(2, initialized.Initializer!.Elements.Count);
        Java.Expression<ArrayCreationExpressionSyntax>("new @Ann int @Ann [1]");
        Java.Expression<ObjectCreationExpressionSyntax>("new java.util.@Ann ArrayList<>()");
    }

    [Fact]
    public void ReadsSwitchExpressionsAndArrowCases()
    {
        var expression = Java.Expression<SwitchExpressionSyntax>("""
            switch (day) {
                case MONDAY, FRIDAY -> 6;
                case TUESDAY -> { yield 7; }
                case WEDNESDAY -> throw new IllegalStateException();
                default -> { int k = day.length(); yield k; }
            }
            """);
        Assert.Equal(4, expression.Cases.Count);
        Assert.Equal(2, expression.Cases[0].Labels.Count);
        Assert.All(expression.Cases, c => Assert.True(c.IsArrow));
        Assert.IsType<BlockSyntax>(expression.Cases[1].ArrowBody);
        Assert.IsType<ThrowStatementSyntax>(expression.Cases[2].ArrowBody);
        Assert.IsType<DefaultLabelSyntax>(expression.Cases[3].Labels[0]);
        var colon = Java.Expression<SwitchExpressionSyntax>("switch (x) { case 1: yield 2; default: { yield 3; } }");
        Assert.False(colon.Cases[0].IsArrow);
        Assert.IsType<YieldStatementSyntax>(colon.Cases[0].Statements[0]);
    }

    [Fact]
    public void ReadsPatterns()
    {
        var instanceofPattern = Java.Expression<InstanceofExpressionSyntax>("o instanceof final String s");
        Assert.IsType<TypePatternSyntax>(instanceofPattern.TypeOrPattern);
        Assert.IsType<ClassTypeSyntax>(Java.Expression<InstanceofExpressionSyntax>("o instanceof String").TypeOrPattern);
        Assert.IsType<ArrayTypeSyntax>(Java.Expression<InstanceofExpressionSyntax>("o instanceof int[]").TypeOrPattern);
        var record = Assert.IsType<RecordPatternSyntax>(Java.Expression<InstanceofExpressionSyntax>("o instanceof Pair<?, ?>(var a, Point(int x, _))").TypeOrPattern);
        Assert.IsType<RecordPatternSyntax>(record.Subpatterns[1]);
        Assert.IsType<UnnamedPatternSyntax>(((RecordPatternSyntax)record.Subpatterns[1]).Subpatterns[1]);
        Assert.IsType<VarTypeSyntax>(((TypePatternSyntax)record.Subpatterns[0]).Type);

        var expression = Java.Expression<SwitchExpressionSyntax>("""
            switch (o) {
                case null -> 0;
                case Integer i when i > 0 && i < 10 -> 1;
                case String _ -> 2;
                case Point(var x, var y) when x == y -> 3;
                case Color.RED -> 4;
                case int[] a -> 5;
                case A _, B _ -> 6;
                case null, default -> 7;
            }
            """);
        Assert.IsType<LiteralExpressionSyntax>(expression.Cases[0].Labels[0]);
        Assert.NotNull(expression.Cases[1].Guard);
        Assert.Equal("_", Assert.IsType<TypePatternSyntax>(expression.Cases[2].Labels[0]).Name.Text);
        Assert.IsType<RecordPatternSyntax>(expression.Cases[3].Labels[0]);
        Assert.IsType<FieldAccessExpressionSyntax>(expression.Cases[4].Labels[0]);
        Assert.IsType<ArrayTypeSyntax>(Assert.IsType<TypePatternSyntax>(expression.Cases[5].Labels[0]).Type);
        Assert.Equal(2, expression.Cases[6].Labels.Count);
        Assert.IsType<DefaultLabelSyntax>(expression.Cases[7].Labels[1]);
    }

    [Fact]
    public void ReadsCaseConstantsThatLookLikeCasts()
    {
        var expression = Java.Expression<SwitchExpressionSyntax>("switch (x) { case (int) A, (int) B -> 1; case 'c' + 1 -> 2; default -> 0; }");
        Assert.IsType<CastExpressionSyntax>(expression.Cases[0].Labels[1]);
    }

    [Fact]
    public void ReadsTheUnnamedVariable()
    {
        Java.ParseClean("""
            class C {
                void m() {
                    int _ = f();
                    for (var _ : list) { }
                    try { } catch (Exception _) { }
                    try (var _ = open()) { }
                    BiFunction<A, B, C> f = (_, _) -> null;
                }
            }
            """);
    }

    [Fact]
    public void ReadsCompactSourceFiles()
    {
        var tree = Java.ParseClean("""
            import java.util.List;

            final String greeting = "Hello";

            void main() {
                IO.println(greeting);
            }

            record Helper(int x) {}
            """);
        Assert.True(tree.Root.IsCompactSourceFile);
        Assert.IsType<FieldDeclarationSyntax>(tree.Root.Members[0]);
        Assert.IsType<MethodDeclarationSyntax>(tree.Root.Members[1]);
        Assert.False(Java.ParseClean("class C {}").Root.IsCompactSourceFile);
    }

    [Fact]
    public void ReadsTextBlocksAndLiterals()
    {
        var expression = Java.Expression<BinaryExpressionSyntax>("\"\"\"\n    text\n    \"\"\" + 'c' + 1L + 1.5f + 0x10 + true + null");
        var kinds = new List<LiteralKind>();
        for (ExpressionSyntax e = expression; e is BinaryExpressionSyntax binary; e = binary.Left)
            kinds.Add(((LiteralExpressionSyntax)binary.Right).Kind);
        Assert.Equal([LiteralKind.Null, LiteralKind.True, LiteralKind.Integer, LiteralKind.Float, LiteralKind.Long, LiteralKind.Character], kinds);
    }

    [Fact]
    public void ReadsAnnotationsEverywhere()
    {
        Java.ParseClean("""
            @Target({ElementType.TYPE_USE, ElementType.METHOD})
            @Retention(value = RetentionPolicy.RUNTIME)
            @interface A { }
            @A @B(x = 1, y = {"a", "b"}, z = @C) class D<@A T> extends @A Base implements @A I {
                @A int @A [] f;
                @A <@A T> @A T m(@A D<T> this, @A T @A ... ts) throws @A Exception {
                    @A var x = (@A String) null;
                    Object o = new @A D<@A ? extends @A Number>();
                    java.lang.@A String s = null;
                    return null;
                }
            }
            """);
    }

    [Fact]
    public void SpansCoverTheirSource()
    {
        var tree = Java.ParseClean("class C { int f(int x) { return x + 1; } }");
        var method = (MethodDeclarationSyntax)Java.Class(tree).Body.Members[0];
        Assert.Equal("int f(int x) { return x + 1; }", Java.TextOf(tree, method));
        Assert.Equal("{ return x + 1; }", Java.TextOf(tree, method.Body!));
        var @return = (ReturnStatementSyntax)method.Body!.Statements[0];
        Assert.Equal("x + 1", Java.TextOf(tree, @return.Expression!));
        Java.AssertWellFormed(tree);
    }

    [Fact]
    public void SpansAreInTheOriginalTextWithUnicodeEscapes()
    {
        var text = "class C { int f = \\u0031 + 2; }";
        var tree = Java.ParseClean(text);
        var field = (FieldDeclarationSyntax)Java.Class(tree).Body.Members[0];
        Assert.Equal("\\u0031 + 2", Java.TextOf(tree, field.Variables[0].Initializer!));
        Java.AssertWellFormed(tree);
    }

    [Fact]
    public void ParsesTheGeneratedSampleCleanly()
    {
        var tree = Java.ParseClean(JavaSamples.File(500));
        Java.AssertWellFormed(tree);
    }
}
