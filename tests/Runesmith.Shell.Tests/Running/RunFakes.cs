using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Tests.Running;

internal sealed class FakeWorkspace(string? root) : IWorkspace
{
    public string? RootPath { get; private set; } = root;

    public string? Name => RootPath is null ? null : Path.GetFileName(RootPath);

    public IReadOnlyList<string> Solutions => [];

    public event EventHandler? Changed;

    public Task OpenAsync(string folderPath)
    {
        RootPath = folderPath;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Close()
    {
        RootPath = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Contains(string path) => RootPath is not null && path.StartsWith(RootPath, StringComparison.Ordinal);

    public Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(RootPath is null ? [] : Directory.GetFiles(RootPath, "*", SearchOption.AllDirectories));
}

/// <summary>A configuration type whose detection, launch plan and build the test decides.</summary>
internal sealed class FakeType(string id = "fake") : IRunConfigurationType
{
    public string Id => id;

    public string Name => "Fake";

    public string Icon => "play";

    public string Description => "A fake type.";

    public OptionSet Options { get; init; } = new([new Option("command", "Command", OptionKind.Text)]);

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    public IReadOnlyList<RunConfiguration> Detected { get; set; } = [];

    public Func<RunConfiguration, BuildContext, BuildResult?> Build { get; set; } = (_, _) => null;

    public List<string> Builds { get; } = [];

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Detected);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken) =>
        Task.FromResult(RunService.ShellCommand(configuration.Values.Get("command") ?? "echo ran", context.RootPath));

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken)
    {
        lock (Builds)
            Builds.Add(configuration.Name);
        return Task.FromResult(Build(configuration, context));
    }
}

internal sealed class FakeBuildService : IBuildService
{
    public int Builds { get; private set; }

    public bool IsBuilding => false;

    public bool CanBuild { get; set; }

    public BuildResult? Result { get; set; } = new(true, 0, 0, TimeSpan.Zero);

    public event EventHandler? StateChanged;

    public Task<BuildResult?> BuildAsync()
    {
        Builds++;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(Result);
    }

    public void Cancel()
    {
    }
}

internal sealed class FakeOutput : IOutputService, IOutputChannel
{
    public List<string> Lines { get; } = [];

    public string Name => "fake";

    public IOutputChannel GetChannel(string name) => this;

    public void Append(string text)
    {
        lock (Lines)
            Lines.Add(text);
    }

    public void AppendLine(string line) => Append(line);

    public void Clear()
    {
    }

    public void Show()
    {
    }
}

internal sealed class FakeDiagnostics : IDiagnosticService
{
    public IReadOnlyList<Diagnostic> All => [];

    public event EventHandler<IReadOnlyCollection<string>>? Changed;

    public void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics) => Changed?.Invoke(this, [filePath]);

    public void Clear(string source)
    {
    }

    public IReadOnlyList<Diagnostic> Get(string filePath) => [];
}

internal sealed class FakeNotifications : INotificationService
{
    public bool Confirm { get; set; } = true;

    public List<string> Notified { get; } = [];

    public int Confirmations { get; private set; }

    public void Notify(NotificationKind kind, string title, string? message = null, string? actionText = null, Action? action = null) => Notified.Add(title);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false)
    {
        Confirmations++;
        return Task.FromResult(Confirm);
    }

    public Task<int?> ChooseAsync(string title, string message, IReadOnlyList<string> choices) => Task.FromResult<int?>(null);
}
