using System.Composition;
using System.Diagnostics;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Tasks;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Services;

/// <summary>Builds the open folder with the first provider that can, and reports its problems and outcome.</summary>
[Export(typeof(IBuildService))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class BuildService(
    [ImportMany] IEnumerable<Lazy<IBuildProvider>> providers,
    IWorkspace workspace,
    EditorService editors,
    IOutputService output,
    IDiagnosticService diagnostics,
    IBackgroundTasks tasks,
    INotificationService notifications) : IBuildService, IDisposable
{
    private const string Source = "build";

    private CancellationTokenSource? running;
    private IBackgroundTask? task;

    public bool IsBuilding => running is not null;

    public bool CanBuild => workspace.RootPath is { } root && FindProvider(root) is not null;

    public event EventHandler? StateChanged;

    public async Task<BuildResult?> BuildAsync()
    {
        if (running is not null || workspace.RootPath is not { } root || FindProvider(root) is not { } provider)
            return null;

        await editors.SaveAllAsync();
        running = new CancellationTokenSource();
        var token = running.Token;
        var channel = output.GetChannel("Build");
        channel.Clear();
        channel.Show();
        diagnostics.Clear(Source);
        var found = new Dictionary<string, List<Diagnostic>>(StringComparer.Ordinal);
        task = tasks.Start("Building", Cancel);
        task.Report($"with {provider.Name}");
        StateChanged?.Invoke(this, EventArgs.Empty);

        void Report(Diagnostic diagnostic)
        {
            lock (found)
            {
                if (!found.TryGetValue(diagnostic.FilePath, out var list))
                    found[diagnostic.FilePath] = list = [];
                list.Add(diagnostic);
                diagnostics.Set(Source, diagnostic.FilePath, [.. list]);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        BuildResult result;
        try
        {
            result = await Task.Run(() => provider.BuildAsync(new BuildContext(root, channel, Report), token), token);
        }
        catch (OperationCanceledException)
        {
            channel.AppendLine("The build was canceled.");
            result = new BuildResult(false, 0, 0, stopwatch.Elapsed);
            Finish(result, canceled: true);
            return result;
        }
        catch (Exception exception)
        {
            channel.AppendLine($"The build could not run: {exception.Message}");
            result = new BuildResult(false, 1, 0, stopwatch.Elapsed);
            Finish(result, canceled: false);
            return result;
        }

        Finish(result, canceled: false);
        return result;
    }

    public void Cancel() => running?.Cancel();

    public void Dispose()
    {
        running?.Cancel();
        running?.Dispose();
        running = null;
    }

    private void Finish(BuildResult result, bool canceled)
    {
        running?.Dispose();
        running = null;
        var seconds = result.Duration.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        task?.Dispose();
        task = null;

        if (!canceled)
        {
            var kind = result.Succeeded ? (result.Warnings > 0 ? NotificationKind.Warning : NotificationKind.Success) : NotificationKind.Error;
            var detail = result.Succeeded && result.Warnings == 0 ? $"Finished in {seconds} s." : $"{Count(result.Errors, "error")} and {Count(result.Warnings, "warning")}.";
            notifications.Notify(kind, result.Succeeded ? "Build succeeded" : "Build failed", detail);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private IBuildProvider? FindProvider(string root)
    {
        foreach (var provider in providers)
        {
            try
            {
                if (provider.Value.CanBuild(root))
                    return provider.Value;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
