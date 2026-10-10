namespace Runesmith.Shell.Tests;

internal static class TestFolders
{
    /// <summary>Deletes a test's folder, waiting a little while another process, such as a virus scanner on Windows, still has a file open.</summary>
    public static void Delete(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }
}
