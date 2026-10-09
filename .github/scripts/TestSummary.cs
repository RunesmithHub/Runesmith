// Summarizes the TRX reports in a folder for a GitHub Actions job: a table per test project in the job summary, and an error annotation
// at the failing line for each failed test. Usage: dotnet run .github/scripts/TestSummary.cs -- <folder>
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

XNamespace trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
var folder = args.Length > 0 ? args[0] : "TestResults";
var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? Directory.GetCurrentDirectory();
var invariant = CultureInfo.InvariantCulture;
var projects = new List<Project>();
var failures = new List<Failure>();

if (Directory.Exists(folder))
{
    foreach (var file in Directory.EnumerateFiles(folder, "*.trx", SearchOption.AllDirectories))
    {
        var run = XDocument.Load(file).Root!;
        var results = run.Descendants(trx + "UnitTestResult").ToList();
        if (results.Count == 0)
            continue;
        var storage = run.Descendants(trx + "UnitTest").Select(t => (string?)t.Attribute("storage")).FirstOrDefault(s => s is not null);
        var name = Path.GetFileNameWithoutExtension(storage ?? file);
        var times = run.Element(trx + "Times");
        var duration = times is null ? TimeSpan.Zero
            : DateTimeOffset.Parse((string)times.Attribute("finish")!, invariant) - DateTimeOffset.Parse((string)times.Attribute("start")!, invariant);
        var failed = results.Where(r => (string?)r.Attribute("outcome") is "Failed" or "Error" or "Timeout" or "Aborted").ToList();
        var passed = results.Count(r => (string?)r.Attribute("outcome") == "Passed");
        projects.Add(new Project(name, passed, failed.Count, results.Count - passed - failed.Count, duration));
        foreach (var result in failed)
        {
            var error = result.Descendants(trx + "ErrorInfo").FirstOrDefault();
            failures.Add(new Failure(name, (string)result.Attribute("testName")!, (string?)error?.Element(trx + "Message") ?? "",
                (string?)error?.Element(trx + "StackTrace") ?? ""));
        }
    }
}

var summary = new StringBuilder();
summary.AppendLine("### Tests").AppendLine();
if (projects.Count == 0)
{
    summary.AppendLine("No test results were found.");
}
else
{
    summary.AppendLine("| Project | Passed | Failed | Skipped | Time |").AppendLine("| --- | ---: | ---: | ---: | ---: |");
    foreach (var project in projects.OrderBy(p => p.Name, StringComparer.Ordinal))
        summary.AppendLine(invariant, $"| {project.Name} | {project.Passed} | {project.Failed} | {project.Skipped} | {project.Duration.TotalSeconds:0.0} s |");
    summary.AppendLine(invariant,
        $"| **Total** | **{projects.Sum(p => p.Passed)}** | **{projects.Sum(p => p.Failed)}** | **{projects.Sum(p => p.Skipped)}** | |");
}

foreach (var failure in failures)
{
    summary.AppendLine().AppendLine(invariant, $"<details><summary>❌ {WebUtility.HtmlEncode($"{failure.Project}: {failure.Test}")}</summary>").AppendLine();
    summary.AppendLine("````text").AppendLine(failure.Message.Trim()).AppendLine(failure.StackTrace.TrimEnd()).AppendLine("````").AppendLine();
    summary.AppendLine("</details>");

    var place = SourceLine(failure.StackTrace, workspace) is (var file, var line) ? $"file={Escape(file, property: true)},line={line}," : "";
    Console.WriteLine($"::error {place}title={Escape(failure.Test, property: true)}::{Escape(failure.Message.Trim(), property: false)}");
}

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } path)
    File.AppendAllText(path, summary.ToString());
else
    Console.Write(summary);

// The first stack frame in the repository, relative to it, so the annotation shows on that line of the pull request.
static (string File, string Line)? SourceLine(string stackTrace, string workspace)
{
    foreach (Match frame in Regex.Matches(stackTrace, @" in (?<file>.+?):line (?<line>\d+)"))
    {
        var file = Path.GetRelativePath(workspace, frame.Groups["file"].Value);
        if (!file.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(file))
            return (file.Replace('\\', '/'), frame.Groups["line"].Value);
    }

    return null;
}

static string Escape(string value, bool property)
{
    value = value.Replace("%", "%25", StringComparison.Ordinal).Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);
    return property ? value.Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal) : value;
}

internal sealed record Project(string Name, int Passed, int Failed, int Skipped, TimeSpan Duration);

internal sealed record Failure(string Project, string Test, string Message, string StackTrace);
