using System.Globalization;
using Runesmith.Hub;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Shell.Hub;

/// <summary>One plugin an install plan changes.</summary>
/// <param name="Change">The version, or the versions it moves between, such as <c>0.3.0 to 0.3.4</c>.</param>
/// <param name="What">What happens to it and why, with the download size, such as <c>Install, needed by Lumen Todo · 412 KB</c>.</param>
public sealed record PlanRow(string Id, string Name, string Change, PluginTier? Tier, string What, OperationKind Kind)
{
    /// <summary>Gets the icon of the version being installed, if any.</summary>
    public HostedImage? Icon { get; init; }
}

/// <summary>A capability a plugin in the plan has.</summary>
/// <param name="From">The name of the plugin it comes from.</param>
/// <param name="IsNew">Whether none of the installed plugins has it yet.</param>
/// <param name="IsHighRisk">Whether it is one of the capabilities shown in the danger color: native code, dynamic code, credentials or starting
/// programs.</param>
public sealed record CapabilityRow(string Id, string Label, string Reason, string From, bool IsNew, bool IsHighRisk);

/// <summary>The warning an unverified plugin in a plan needs, and whether the user acknowledged it.</summary>
public sealed class UnverifiedWarning(string id, string name, string publisher, IReadOnlyList<CapabilityRow> capabilities, string? repository, string? analysis)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Publisher { get; } = publisher;

    public string Text { get; } =
        $"{name} hasn't been reviewed by Runesmith. Its code was checked automatically, but no person has read it. Plugins can access everything you can: your files, your projects and your accounts. Install it only if you trust {publisher}.";

    public string Acknowledgement { get; } = $"I understand that {name} is unverified and I trust its publisher.";

    public IReadOnlyList<CapabilityRow> Capabilities { get; } = capabilities;

    /// <summary>Gets the plugin's source repository.</summary>
    public string? Repository { get; } = repository;

    /// <summary>Gets the build that produced and analysed the package.</summary>
    public string? Analysis { get; } = analysis;

    /// <summary>Gets or sets whether the user ticked the acknowledgement; it starts unticked every time.</summary>
    public bool IsAcknowledged { get; set; }
}

/// <summary>What the install dialog shows for a plan: the plugins it changes, every capability with the new ones marked, and a warning with
/// an acknowledgement for each unverified plugin. Install stays unavailable until every acknowledgement is ticked.</summary>
public sealed class InstallPlanModel
{
    private static readonly HashSet<string> HighRisk = new(StringComparer.Ordinal) { "native", "dynamic-code", "credentials", "process" };

    /// <param name="heldCapabilities">The capabilities the installed plugins have between them.</param>
    public InstallPlanModel(InstallPlan plan, HubClient client, IReadOnlySet<string> heldCapabilities)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(heldCapabilities);
        Plan = plan;
        var catalog = client.Catalog;
        var changes = plan.Changes.ToList();
        var rows = new List<PlanRow>();
        var capabilities = new List<CapabilityRow>();
        var warnings = new List<UnverifiedWarning>();
        foreach (var operation in changes)
        {
            var change = operation.Kind switch
            {
                OperationKind.Remove => operation.From?.ToString() ?? "",
                _ when operation.From is { } from => $"{from} to {operation.To}",
                _ => operation.To?.ToString() ?? "",
            };
            var size = operation.Record?.Package is { } package ? $" · {Size(package.Length)}" : "";
            rows.Add(new PlanRow(operation.PluginId, operation.Name, change, operation.Tier, What(operation, changes) + size, operation.Kind)
            {
                Icon = catalog?.FindPlugin(operation.PluginId)?.Icon.Size64,
            });

            if (operation.Record is not { } record || operation.Kind == OperationKind.Remove)
                continue;

            var own = record.Capabilities.Declared
                .Select(c => new CapabilityRow(c.Id, client.CapabilityLabel(c.Id), c.Reason, operation.Name, !heldCapabilities.Contains(c.Id), HighRisk.Contains(c.Id)))
                .ToList();
            capabilities.AddRange(own);
            if (operation.Tier == PluginTier.Unverified)
            {
                var listing = catalog?.FindPlugin(operation.PluginId);
                var publisher = listing is null ? operation.PluginId : catalog!.FindPublisher(listing.Publisher)?.DisplayName ?? listing.Publisher;
                warnings.Add(new UnverifiedWarning(operation.PluginId, operation.Name, publisher, own, listing?.Repository.Url,
                    record.Build.Run.StartsWith("https://", StringComparison.Ordinal) ? record.Build.Run : null));
            }
        }

        Rows = rows;
        Capabilities = [.. capabilities.OrderByDescending(c => c.IsNew).ThenBy(c => c.From, StringComparer.CurrentCultureIgnoreCase)];
        Warnings = warnings;

        var requested = changes.Where(op => op.Reason == OperationReason.Requested).ToList();
        Title = requested switch
        {
            [{ Kind: OperationKind.Remove } only] => $"Remove {only.Name}",
            [{ Kind: OperationKind.Update } only] => $"Update {only.Name}",
            [{ Kind: OperationKind.Downgrade } only] => $"Change {only.Name} to {only.To}",
            [var only] => $"Install {only.Name}",
            _ when changes.All(op => op.Kind == OperationKind.Update) => "Update plugins",
            _ => "Change plugins",
        };
        Summary = changes.Count == 1 ? "This changes 1 plugin." : $"This changes {changes.Count} plugins.";
        if (changes.Count == 1 && changes[0].Record is { Review.State: ReviewState.Reviewed } reviewed)
        {
            Review = reviewed.Review.At is { } at
                ? $"Reviewed by Runesmith on {at.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture)}."
                : "Reviewed by Runesmith.";
        }
    }

    public InstallPlan Plan { get; }

    /// <summary>Gets the dialog's title, such as "Install Lumen Todo".</summary>
    public string Title { get; }

    public string Summary { get; }

    /// <summary>Gets the review note of a plan that changes a single reviewed plugin.</summary>
    public string? Review { get; }

    public IReadOnlyList<PlanRow> Rows { get; }

    /// <summary>Gets every capability across the plan, the ones new to the user first.</summary>
    public IReadOnlyList<CapabilityRow> Capabilities { get; }

    public IReadOnlyList<UnverifiedWarning> Warnings { get; }

    public bool HasChanges => Rows.Count > 0;

    /// <summary>Gets whether Install may be pressed: the plan changes something and every unverified plugin is acknowledged.</summary>
    public bool CanInstall => HasChanges && Warnings.All(w => w.IsAcknowledged);

    /// <summary>Gets the text of the button that applies the plan.</summary>
    public string ActionText => Rows.All(r => r.Kind == OperationKind.Remove) ? "Remove" : Rows.All(r => r.Kind == OperationKind.Update) ? "Update" : "Install";

    /// <summary>Says how large a download is, such as "412 KB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0 / 1024.0:0.0} MB"),
    };

    private static string What(PlanOperation operation, IReadOnlyList<PlanOperation> plan)
    {
        var needing = plan.Where(other => other.Record?.Dependencies.Any(d => d.Id == operation.PluginId) == true).OrderBy(other => other.Reason != OperationReason.Requested).ToList();
        var need = needing.Count == 0 ? null : needing[0];
        var range = need?.Record?.Dependencies.First(d => d.Id == operation.PluginId).Range;
        return operation.Kind switch
        {
            OperationKind.Install => need is null || operation.Reason == OperationReason.Requested ? "Install" : $"Install, needed by {need.Name}",
            OperationKind.Update => need is null || operation.Reason == OperationReason.Requested ? "Update" : $"Update, {need.Name} needs {range}",
            OperationKind.Downgrade => need is null ? "Downgrade" : $"Downgrade, {need.Name} needs {range}",
            OperationKind.Remove => operation.Reason switch
            {
                OperationReason.NoLongerNeeded => "Remove, nothing needs it anymore",
                OperationReason.Unavailable => "Remove, no longer available",
                _ => "Remove",
            },
            _ => "Keep",
        };
    }
}
