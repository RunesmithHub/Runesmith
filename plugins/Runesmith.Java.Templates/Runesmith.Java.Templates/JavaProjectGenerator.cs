using System.Globalization;
using System.Text;
using Runesmith.Sdk.Options;
using static Runesmith.Java.Templates.JavaTemplateOptions;

namespace Runesmith.Java.Templates;

/// <summary>A project a Java template makes.</summary>
public enum JavaTemplateKind
{
    /// <summary>A program with a main class.</summary>
    Application,

    /// <summary>A library with a public class.</summary>
    Library,

    /// <summary>One Java 25 compact source file with a <c>void main()</c>, run with <c>java File.java</c>.</summary>
    CompactSourceFile,

    /// <summary>An application module that depends on a library module.</summary>
    MultiModule,
}

/// <summary>A file a template writes.</summary>
/// <param name="Path">The path relative to the project's folder, with forward slashes.</param>
internal sealed record GeneratedFile(string Path, string Content);

/// <summary>The files of a new project, and the one to open first.</summary>
internal sealed record GeneratedProject(IReadOnlyList<GeneratedFile> Files, string MainFile);

/// <summary>Writes the files of Runesmith's Java templates. The code's package is the group id followed by the artifact id made a valid
/// package segment, such as <c>com.example.myapp</c>.</summary>
internal sealed class JavaProjectGenerator
{
    public const string JUnitVersion = "5.13.4";
    private const string CompilerPluginVersion = "3.14.1";
    private const string SurefirePluginVersion = "3.5.4";
    private const string JarPluginVersion = "3.4.2";
    private const string ExecPluginVersion = "3.5.1";
    private const string Version = "0.1.0";
    private const string ProjectVersion = "${project.version}";

    private readonly string name;
    private readonly string build;
    private readonly bool kotlin;
    private readonly string groupId;
    private readonly string artifactId;
    private readonly int release;
    private readonly bool moduleInfo;
    private readonly bool junit;
    private readonly bool sample;
    private readonly string basePackage;
    private readonly List<GeneratedFile> files = [];

    private JavaProjectGenerator(string name, OptionValues values)
    {
        this.name = name.Trim();
        build = values.Get(BuildSystem) is Gradle or None ? values.Get(BuildSystem)! : Maven;
        kotlin = values.Get(GradleDsl) != Groovy;
        groupId = JavaNames.Package(values.Get(GroupId) is { Length: > 0 } group ? group : "com.example");
        artifactId = values.Get(ArtifactId) is { Length: > 0 } artifact ? artifact : JavaNames.ArtifactId(this.name);
        release = int.TryParse(values.Get(JavaRelease), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && Releases.Contains(number)
            ? number
            : DefaultRelease;
        moduleInfo = values.GetBool(ModuleInfo) && release >= 11;
        junit = build != None && (values.Get(JUnit) is null || values.GetBool(JUnit));
        sample = values.Get(SampleCode) is null || values.GetBool(SampleCode);
        basePackage = groupId + "." + JavaNames.PackageSegment(artifactId);
    }

    /// <summary>Gets the files of a project made from a template with the given name and option values.</summary>
    public static GeneratedProject Generate(JavaTemplateKind kind, string name, OptionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var generator = new JavaProjectGenerator(name, values);
        var main = kind switch
        {
            JavaTemplateKind.Library => generator.Library(),
            JavaTemplateKind.CompactSourceFile => generator.CompactSourceFile(),
            JavaTemplateKind.MultiModule => generator.MultiModule(),
            _ => generator.Application(),
        };
        return new GeneratedProject(generator.files, main);
    }

    private string MainSources(string module) => build == None ? Join(module, "src") : Join(module, "src/main/java");

    private static string TestSources(string module) => Join(module, "src/test/java");

    private static string Join(string module, string path) => module.Length == 0 ? path : module + "/" + path;

    private static string Folder(string package) => package.Replace('.', '/');

    private void Add(string path, string content) => files.Add(new GeneratedFile(path, content));

    private string Application()
    {
        var main = $"{MainSources("")}/{Folder(basePackage)}/Main.java";
        Add(main, MainClass(basePackage, null));
        if (junit)
            Add($"{TestSources("")}/{Folder(basePackage)}/MainTest.java", MainTest(basePackage, null));
        if (moduleInfo)
            Add($"{MainSources("")}/module-info.java", $"module {basePackage} {{\n}}\n");
        BuildFiles("", artifactId, $"{basePackage}.Main", basePackage, dependsOn: null);
        GitIgnore();
        return main;
    }

    private string Library()
    {
        var type = JavaNames.ClassName(name);
        var main = $"{MainSources("")}/{Folder(basePackage)}/{type}.java";
        Add(main, LibraryClass(basePackage, type));
        if (junit)
            Add($"{TestSources("")}/{Folder(basePackage)}/{type}Test.java", LibraryTest(basePackage, type));
        if (moduleInfo)
            Add($"{MainSources("")}/module-info.java", $"module {basePackage} {{\n    exports {basePackage};\n}}\n");
        BuildFiles("", artifactId, null, basePackage, dependsOn: null);
        GitIgnore();
        return main;
    }

    private string CompactSourceFile()
    {
        var file = JavaNames.ClassName(name) + ".java";
        Add(file, sample
            ? $"void main() {{\n    IO.println(\"Hello from {JavaNames.JavaString(name)}!\");\n}}\n"
            : "void main() {\n}\n");
        Add(".gitignore", "*.class\n");
        return file;
    }

    private string MultiModule()
    {
        var libPackage = basePackage + ".lib";
        var appPackage = basePackage + ".app";
        const string Greeter = "Greeter";
        Add($"{MainSources("lib")}/{Folder(libPackage)}/{Greeter}.java", LibraryClass(libPackage, Greeter));
        var main = $"{MainSources("app")}/{Folder(appPackage)}/Main.java";
        Add(main, MainClass(appPackage, sample ? $"{libPackage}.{Greeter}" : null));
        if (junit)
        {
            Add($"{TestSources("lib")}/{Folder(libPackage)}/{Greeter}Test.java", LibraryTest(libPackage, Greeter));
            Add($"{TestSources("app")}/{Folder(appPackage)}/MainTest.java", MainTest(appPackage, sample ? $"{libPackage}.{Greeter}" : null));
        }

        if (moduleInfo)
        {
            Add($"{MainSources("lib")}/module-info.java", $"module {libPackage} {{\n    exports {libPackage};\n}}\n");
            Add($"{MainSources("app")}/module-info.java", $"module {appPackage} {{\n    requires {libPackage};\n}}\n");
        }

        if (build == Maven)
            Add("pom.xml", ParentPom());
        else
            Add(kotlin ? "settings.gradle.kts" : "settings.gradle", Settings(["lib", "app"]));
        BuildFiles("lib", artifactId + "-lib", null, libPackage, dependsOn: null);
        BuildFiles("app", artifactId + "-app", $"{appPackage}.Main", appPackage, dependsOn: "lib");
        GitIgnore();
        return main;
    }

    /// <summary>The main class; with a greeter, it greets through the library's class.</summary>
    private string MainClass(string package, string? greeter)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"package {package};\n\n");
        if (greeter is not null)
            text.Append(CultureInfo.InvariantCulture, $"import {greeter};\n\n");
        text.Append("/** The application's entry point. */\npublic final class Main {\n    private Main() {\n    }\n\n");
        text.Append("    /** Starts the application. */\n    public static void main(String[] args) {\n");
        if (sample)
            text.Append(CultureInfo.InvariantCulture, $"        System.out.println(greeting(\"{JavaNames.JavaString(name)}\"));\n");
        text.Append("    }\n");
        if (sample)
        {
            var body = greeter is null ? "\"Hello from \" + name + \"!\"" : $"{greeter[(greeter.LastIndexOf('.') + 1)..]}.greet(name)";
            text.Append(CultureInfo.InvariantCulture, $"\n    /** Gets the greeting for a name. */\n    static String greeting(String name) {{\n        return {body};\n    }}\n");
        }

        text.Append("}\n");
        return text.ToString();
    }

    private string MainTest(string package, string? greeter)
    {
        var test = sample
            ? greeter is null
                ? "    @Test\n    void greetsByName() {\n        assertEquals(\"Hello from Ada!\", Main.greeting(\"Ada\"));\n    }\n"
                : "    @Test\n    void greetsThroughTheLibrary() {\n        assertEquals(\"Hello, Ada!\", Main.greeting(\"Ada\"));\n    }\n"
            : "    @Test\n    void runs() {\n        Main.main(new String[0]);\n    }\n";
        var imports = sample ? "import static org.junit.jupiter.api.Assertions.assertEquals;\n\n" : "";
        return $"package {package};\n\n{imports}import org.junit.jupiter.api.Test;\n\nclass MainTest {{\n{test}}}\n";
    }

    private string LibraryClass(string package, string type) => sample
        ? $"package {package};\n\n/** Greets people. */\npublic final class {type} {{\n    private {type}() {{\n    }}\n\n" +
          $"    /** Gets a greeting for someone. */\n    public static String greet(String name) {{\n        return \"Hello, \" + name + \"!\";\n    }}\n}}\n"
        : $"package {package};\n\n/** The library's entry point. */\npublic class {type} {{\n}}\n";

    private string LibraryTest(string package, string type)
    {
        var (assertion, test) = sample
            ? ("assertEquals", $"    @Test\n    void greetsByName() {{\n        assertEquals(\"Hello, Ada!\", {type}.greet(\"Ada\"));\n    }}\n")
            : ("assertNotNull", $"    @Test\n    void creates() {{\n        assertNotNull(new {type}());\n    }}\n");
        return $"package {package};\n\nimport static org.junit.jupiter.api.Assertions.{assertion};\n\nimport org.junit.jupiter.api.Test;\n\nclass {type}Test {{\n{test}}}\n";
    }

    private void GitIgnore() => Add(".gitignore", build switch
    {
        Maven => "target/\n",
        Gradle => ".gradle/\nbuild/\n",
        _ => "out/\n",
    });

    private void BuildFiles(string module, string artifact, string? mainClass, string package, string? dependsOn)
    {
        var isModule = module.Length > 0;
        if (build == Maven)
        {
            Add(Join(module, "pom.xml"), Pom(artifact, mainClass, dependsOn, isModule));
        }
        else if (build == Gradle)
        {
            if (!isModule)
                Add(kotlin ? "settings.gradle.kts" : "settings.gradle", Settings([]));
            Add(Join(module, kotlin ? "build.gradle.kts" : "build.gradle"), kotlin
                ? GradleKotlin(mainClass, package, dependsOn)
                : GradleGroovy(mainClass, package, dependsOn));
        }
    }

    private string Settings(IReadOnlyList<string> modules)
    {
        var quote = kotlin ? '"' : '\'';
        var text = $"rootProject.name = {quote}{artifactId}{quote}\n";
        if (modules.Count > 0)
        {
            var list = string.Join(", ", modules.Select(m => $"{quote}{m}{quote}"));
            text += kotlin ? $"\ninclude({list})\n" : $"\ninclude {list}\n";
        }

        return text;
    }

    private string GradleKotlin(string? mainClass, string package, string? dependsOn)
    {
        var text = new StringBuilder();
        text.Append(mainClass is null ? "plugins {\n    `java-library`\n}\n\n" : "plugins {\n    application\n}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"group = \"{groupId}\"\nversion = \"{Version}\"\n\nrepositories {{\n    mavenCentral()\n}}\n");
        if (dependsOn is not null || junit)
        {
            text.Append("\ndependencies {\n");
            if (dependsOn is not null)
                text.Append(CultureInfo.InvariantCulture, $"    implementation(project(\":{dependsOn}\"))\n");
            if (junit)
            {
                text.Append(CultureInfo.InvariantCulture, $"    testImplementation(platform(\"org.junit:junit-bom:{JUnitVersion}\"))\n");
                text.Append("    testImplementation(\"org.junit.jupiter:junit-jupiter\")\n    testRuntimeOnly(\"org.junit.platform:junit-platform-launcher\")\n");
            }

            text.Append("}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"\ntasks.withType<JavaCompile>().configureEach {{\n    options.release = {release}\n}}\n");
        if (mainClass is not null)
        {
            text.Append("\napplication {\n");
            if (moduleInfo)
                text.Append(CultureInfo.InvariantCulture, $"    mainModule = \"{package}\"\n");
            text.Append(CultureInfo.InvariantCulture, $"    mainClass = \"{mainClass}\"\n}}\n");
        }

        if (junit)
            text.Append("\ntasks.test {\n    useJUnitPlatform()\n}\n");
        return text.ToString();
    }

    private string GradleGroovy(string? mainClass, string package, string? dependsOn)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"plugins {{\n    id '{(mainClass is null ? "java-library" : "application")}'\n}}\n\n");
        text.Append(CultureInfo.InvariantCulture, $"group = '{groupId}'\nversion = '{Version}'\n\nrepositories {{\n    mavenCentral()\n}}\n");
        if (dependsOn is not null || junit)
        {
            text.Append("\ndependencies {\n");
            if (dependsOn is not null)
                text.Append(CultureInfo.InvariantCulture, $"    implementation project(':{dependsOn}')\n");
            if (junit)
            {
                text.Append(CultureInfo.InvariantCulture, $"    testImplementation platform('org.junit:junit-bom:{JUnitVersion}')\n");
                text.Append("    testImplementation 'org.junit.jupiter:junit-jupiter'\n    testRuntimeOnly 'org.junit.platform:junit-platform-launcher'\n");
            }

            text.Append("}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"\ntasks.withType(JavaCompile).configureEach {{\n    options.release = {release}\n}}\n");
        if (mainClass is not null)
        {
            text.Append("\napplication {\n");
            if (moduleInfo)
                text.Append(CultureInfo.InvariantCulture, $"    mainModule = '{package}'\n");
            text.Append(CultureInfo.InvariantCulture, $"    mainClass = '{mainClass}'\n}}\n");
        }

        if (junit)
            text.Append("\ntasks.named('test') {\n    useJUnitPlatform()\n}\n");
        return text.ToString();
    }

    private const string PomHeader = """
        <?xml version="1.0" encoding="UTF-8"?>
        <project xmlns="http://maven.apache.org/POM/4.0.0"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:schemaLocation="http://maven.apache.org/POM/4.0.0 https://maven.apache.org/xsd/maven-4.0.0.xsd">
          <modelVersion>4.0.0</modelVersion>


        """;

    private string ParentPom()
    {
        var text = new StringBuilder(PomHeader);
        text.Append(CultureInfo.InvariantCulture, $"""
              <groupId>{groupId}</groupId>
              <artifactId>{JavaNames.Xml(artifactId)}</artifactId>
              <version>{Version}-SNAPSHOT</version>
              <packaging>pom</packaging>
              <name>{JavaNames.Xml(name)}</name>

              <modules>
                <module>lib</module>
                <module>app</module>
              </modules>


            """);
        Properties(text);
        text.Append("  <dependencyManagement>\n    <dependencies>\n");
        text.Append(CultureInfo.InvariantCulture, $"""
                  <dependency>
                    <groupId>{groupId}</groupId>
                    <artifactId>{JavaNames.Xml(artifactId)}-lib</artifactId>
                    <version>{ProjectVersion}</version>
                  </dependency>

            """);
        if (junit)
            text.Append(JUnitBom());
        text.Append("    </dependencies>\n  </dependencyManagement>\n\n  <build>\n    <pluginManagement>\n      <plugins>\n");
        text.Append(Indent(CommonPlugins(), "  "));
        text.Append("      </plugins>\n    </pluginManagement>\n  </build>\n</project>\n");
        return text.ToString();
    }

    private string Pom(string artifact, string? mainClass, string? dependsOn, bool isModule)
    {
        var text = new StringBuilder(PomHeader);
        if (isModule)
        {
            text.Append(CultureInfo.InvariantCulture, $"""
                  <parent>
                    <groupId>{groupId}</groupId>
                    <artifactId>{JavaNames.Xml(artifactId)}</artifactId>
                    <version>{Version}-SNAPSHOT</version>
                  </parent>

                  <artifactId>{JavaNames.Xml(artifact)}</artifactId>


                """);
        }
        else
        {
            text.Append(CultureInfo.InvariantCulture, $"""
                  <groupId>{groupId}</groupId>
                  <artifactId>{JavaNames.Xml(artifact)}</artifactId>
                  <version>{Version}-SNAPSHOT</version>
                  <name>{JavaNames.Xml(name)}</name>


                """);
            Properties(text);
            if (junit)
                text.Append("  <dependencyManagement>\n    <dependencies>\n").Append(JUnitBom()).Append("    </dependencies>\n  </dependencyManagement>\n\n");
        }

        if (dependsOn is not null || junit)
        {
            text.Append("  <dependencies>\n");
            if (dependsOn is not null)
                text.Append(CultureInfo.InvariantCulture, $"    <dependency>\n      <groupId>{groupId}</groupId>\n      <artifactId>{JavaNames.Xml(artifactId)}-{dependsOn}</artifactId>\n    </dependency>\n");
            if (junit)
                text.Append("    <dependency>\n      <groupId>org.junit.jupiter</groupId>\n      <artifactId>junit-jupiter</artifactId>\n      <scope>test</scope>\n    </dependency>\n");
            text.Append("  </dependencies>\n\n");
        }

        var plugins = new StringBuilder();
        if (!isModule)
            plugins.Append(CommonPlugins());
        if (mainClass is not null)
            plugins.Append(MainClassPlugins(mainClass, withVersions: !isModule));
        if (plugins.Length > 0)
            text.Append("  <build>\n    <plugins>\n").Append(plugins).Append("    </plugins>\n  </build>\n");
        else
            text.Length -= 1;
        text.Append("</project>\n");
        return text.ToString();
    }

    private void Properties(StringBuilder text) => text.Append(CultureInfo.InvariantCulture, $"""
          <properties>
            <maven.compiler.release>{release}</maven.compiler.release>
            <project.build.sourceEncoding>UTF-8</project.build.sourceEncoding>
          </properties>


        """);

    private static string JUnitBom() => $"""
              <dependency>
                <groupId>org.junit</groupId>
                <artifactId>junit-bom</artifactId>
                <version>{JUnitVersion}</version>
                <type>pom</type>
                <scope>import</scope>
              </dependency>

        """;

    private static string CommonPlugins() => $"""
              <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-compiler-plugin</artifactId>
                <version>{CompilerPluginVersion}</version>
              </plugin>
              <plugin>
                <groupId>org.apache.maven.plugins</groupId>
                <artifactId>maven-surefire-plugin</artifactId>
                <version>{SurefirePluginVersion}</version>
              </plugin>

        """;

    private static string MainClassPlugins(string mainClass, bool withVersions)
    {
        var jarVersion = withVersions ? $"\n        <version>{JarPluginVersion}</version>" : "";
        var execVersion = $"\n        <version>{ExecPluginVersion}</version>";
        return $"""
                  <plugin>
                    <groupId>org.apache.maven.plugins</groupId>
                    <artifactId>maven-jar-plugin</artifactId>{jarVersion}
                    <configuration>
                      <archive>
                        <manifest>
                          <mainClass>{mainClass}</mainClass>
                        </manifest>
                      </archive>
                    </configuration>
                  </plugin>
                  <plugin>
                    <groupId>org.codehaus.mojo</groupId>
                    <artifactId>exec-maven-plugin</artifactId>{execVersion}
                    <configuration>
                      <mainClass>{mainClass}</mainClass>
                    </configuration>
                  </plugin>

            """;
    }

    private static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));
}
