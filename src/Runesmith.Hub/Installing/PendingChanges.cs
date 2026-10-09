using Runesmith.Hub.State;

namespace Runesmith.Hub.Installing;

/// <summary>Puts committed installs, updates and removals into effect, before plugins load.</summary>
internal static class PendingChanges
{
    /// <summary>Moves the prepared folders into the plugins folder and removes the removed ones.</summary>
    /// <param name="problems">Collects what could not be done; those changes stay pending for the next start.</param>
    public static HubState Apply(HubPaths paths, HubState state, ICollection<string> problems)
    {
        if (state.Pending is not { } pending)
        {
            Folders.TryDelete(paths.Staging);
            return state;
        }

        var trash = Directory.CreateDirectory(Path.Combine(paths.Staging, ".trash-" + Guid.NewGuid().ToString("N"))).FullName;
        var leftInstalled = new List<PendingFolder>();
        foreach (var folder in pending.Installed)
        {
            var staged = Path.Combine(paths.Staging, folder.Transaction, folder.Id);
            var target = paths.PluginFolder(folder.Id);
            try
            {
                if (!Directory.Exists(staged))
                    continue;
                if (Directory.Exists(target))
                    Directory.Move(target, Path.Combine(trash, folder.Id));
                Directory.Move(staged, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{folder.Id} {folder.Version} could not be put in place: {exception.Message}");
                leftInstalled.Add(folder);
            }
        }

        var leftRemoved = new List<string>();
        foreach (var id in pending.Removed)
        {
            try
            {
                if (Directory.Exists(paths.PluginFolder(id)))
                    Directory.Move(paths.PluginFolder(id), Path.Combine(trash, id + "-removed"));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{id} could not be removed: {exception.Message}");
                leftRemoved.Add(id);
            }
        }

        var left = leftInstalled.Count == 0 && leftRemoved.Count == 0 ? null : new PendingChange(leftInstalled, leftRemoved);
        if (left is null)
        {
            Folders.TryDelete(paths.Staging);
        }
        else
        {
            Folders.TryDelete(trash);
        }

        return state with { Pending = left };
    }
}
