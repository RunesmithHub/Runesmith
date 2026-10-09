using Runesmith.LanguageServices;

namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class DiagnosticsTests
{
    private static readonly string[] SemanticCodes = ["JAVA-IMPORT", "JAVA-TYPE", "JAVA-MEMBER"];

    private static string Describe(IEnumerable<Problem> problems) => string.Join("; ", problems.Select(p => $"{p.Code} {p.Message} at {p.Span}"));

    [Fact]
    public async Task RecordsAtJava11AreAReleaseError()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string>
        {
            ["pom.xml"] = JavaTestProject.Pom(11),
            ["src/main/java/Point.java"] = "",
        });
        project.Open("src/main/java/Point.java", "record Point(int x, int y) { }\n");

        var problems = await project.DiagnosticsAsync("src/main/java/Point.java");

        var problem = Assert.Single(problems, p => p.Code == "JAVA-RELEASE");
        Assert.Contains("Records need Java 16 or later", problem.Message, StringComparison.Ordinal);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
    }

    [Fact]
    public async Task GradleToolchainsSetTheRelease()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string>
        {
            ["settings.gradle"] = "rootProject.name = 'app'",
            ["build.gradle"] = JavaTestProject.GradleBuild(21),
            ["src/main/java/App.java"] = "",
        });
        project.Open("src/main/java/App.java", """
            import module java.base;

            class App {
                String describe(Object value) {
                    return switch (value) {
                        case Integer i when i > 0 -> "positive";
                        case String s -> s.strip();
                        default -> "other";
                    };
                }
            }
            """);

        var problems = await project.DiagnosticsAsync("src/main/java/App.java");

        var problem = Assert.Single(problems);
        Assert.Equal("JAVA-RELEASE", problem.Code);
        Assert.Contains("Module import declarations", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnresolvedImportsAreReported()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["App.java"] = "" });
        project.Open("App.java", """
            import java.util.Lisst;
            import java.utill.*;
            import org.example.missing.Library;
            import java.util.List;

            class App { List<String> names; Library library; }
            """);

        var problems = (await project.DiagnosticsAsync("App.java")).Where(p => SemanticCodes.Contains(p.Code)).ToList();

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Code == "JAVA-IMPORT" && p.Message.Contains("java.util.Lisst", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Code == "JAVA-IMPORT" && p.Message.Contains("java.utill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownTypesAndMembersAreReportedWhereCertain()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["App.java"] = "" });
        var text = """
            import java.util.ArrayList;

            class App {
                Strin name;
                void run() {
                    var list = new ArrayList<String>();
                    list.ad("x");
                    "abc".lenght();
                    System.ot.println();
                }
            }
            """;
        project.Open("App.java", text);

        var problems = (await project.DiagnosticsAsync("App.java")).Where(p => SemanticCodes.Contains(p.Code)).ToList();

        Assert.Equal(4, problems.Count);
        Assert.Contains(problems, p => p.Code == "JAVA-TYPE" && p.Span.Start == text.IndexOf("Strin", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Code == "JAVA-MEMBER" && p.Span.Start == text.IndexOf("ad(", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Code == "JAVA-MEMBER" && p.Span.Start == text.IndexOf("lenght", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Code == "JAVA-MEMBER" && p.Span.Start == text.IndexOf("ot.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UncertainCodeIsLeftAlone()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string>
        {
            ["Base.java"] = "import org.example.missing.Framework;\nclass Base extends Framework { }",
        });
        project.Open("App.java", """
            import java.util.*;
            import java.util.function.*;
            import org.example.missing.Helper;

            class App extends Base {
                <T extends Comparable<T>> T largest(List<T> values) {
                    T best = values.get(0);
                    best.compareTo(best);
                    return best;
                }

                void run(Helper helper, Map<String, List<Integer>> map) {
                    helper.anything().goes();
                    inherited();
                    frameworkField.whatever();
                    map.values().stream().map(l -> l.size()).forEach(n -> n.intValue());
                    Function<String, Integer> length = s -> s.length();
                    Runnable task = new Runnable() { public void run() { extra(); } void extra() { } };
                    Object o = map;
                    if (o instanceof Map<?, ?> m && !m.isEmpty()) { m.size(); }
                    record Pair(String left, String right) { }
                    var pair = new Pair("a", "b");
                    pair.left().length();
                    Unknown.call();
                }
            }
            """);

        var problems = (await project.DiagnosticsAsync("App.java")).Where(p => SemanticCodes.Contains(p.Code)).ToList();

        Assert.True(problems.Count == 0, Describe(problems));
    }

    [Fact]
    public async Task SyntaxErrorsAreReported()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["App.java"] = "" });
        project.Open("App.java", "class App { void run() { int x = ; } }");

        var problems = await project.DiagnosticsAsync("App.java");

        Assert.Contains(problems, p => p.Code == "JAVA-SYNTAX");
    }
}
