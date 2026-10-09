using System.Text.Json;

namespace Runesmith.Plugins.Gitea;

/// <summary>Keeps the list of accounts, without their tokens, in a file in Runesmith's state folder, or only in memory without a file.</summary>
/// <param name="file">The file, or null to keep the list in memory.</param>
internal sealed class AccountStore(string? file)
{
    private List<StoredAccount> memory = [];

    /// <summary>Reads the accounts; a missing or damaged file reads as none.</summary>
    public IReadOnlyList<StoredAccount> Load()
    {
        if (file is null)
            return memory;

        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), GiteaJson.Default.ListStoredAccount) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes the accounts, replacing the file at once so a crash never leaves half of it.</summary>
    public void Save(IEnumerable<StoredAccount> accounts)
    {
        var list = accounts.ToList();
        if (file is null)
        {
            memory = list;
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = $"{file}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(list, GiteaJson.Default.ListStoredAccount));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
