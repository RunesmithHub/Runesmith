namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class ProjectAnalysisTests
{
    private static readonly string[] SemanticCodes = ["JAVA-IMPORT", "JAVA-TYPE", "JAVA-MEMBER"];

    [Fact]
    public async Task ModulesSeeTheSourcesOfTheModulesTheyDependOn()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string>
        {
            ["pom.xml"] = """
                <project><groupId>test</groupId><artifactId>parent</artifactId><version>1.0</version><packaging>pom</packaging>
                  <properties><maven.compiler.release>17</maven.compiler.release></properties>
                  <modules><module>core</module><module>app</module></modules>
                </project>
                """,
            ["core/pom.xml"] = """
                <project><parent><groupId>test</groupId><artifactId>parent</artifactId><version>1.0</version></parent>
                  <artifactId>core</artifactId>
                </project>
                """,
            ["app/pom.xml"] = """
                <project><parent><groupId>test</groupId><artifactId>parent</artifactId><version>1.0</version></parent>
                  <artifactId>app</artifactId>
                  <dependencies><dependency><groupId>test</groupId><artifactId>core</artifactId><version>${project.version}</version></dependency></dependencies>
                </project>
                """,
            ["core/src/main/java/core/Inventory.java"] = "package core;\n\npublic class Inventory { public int count() { return 0; } }\n",
            ["app/src/main/java/app/App.java"] = "",
        });
        var offset = project.Open("app/src/main/java/app/App.java", """
            package app;

            import core.Inventory;

            class App {
                int run(Inventory inventory) {
                    inventory.$$
                    return inventory.count();
                }
            }
            """);

        var completion = await project.CompleteMemberAsync("app/src/main/java/app/App.java", offset);
        var problems = await project.DiagnosticsAsync("app/src/main/java/app/App.java");

        Assert.Contains(completion.Items, i => i.Label == "count");
        Assert.DoesNotContain(problems, p => SemanticCodes.Contains(p.Code));
    }

    [Fact]
    public async Task EditsAreSeenByTheNextRequest()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["App.java"] = "" });
        var offset = project.Open("App.java", "class App {\n    $$\n    void run() { this. }\n}\n");
        offset = project.Insert("App.java", offset, "int added;");
        var dot = project.Snapshot("App.java").GetText().IndexOf("this.", StringComparison.Ordinal) + "this.".Length;

        var result = await project.CompleteMemberAsync("App.java", dot);

        Assert.Contains(result.Items, i => i.Label == "added");
        Assert.Contains(result.Items, i => i.Label == "run");
    }
}
