namespace Pos.Infrastructure;

internal static class AtomicFileCommit
{
    public static void Replace(string temporaryPath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (File.Exists(destinationPath))
        {
            File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, destinationPath);
        }
    }

    public static void MoveNew(string temporaryPath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        File.Move(temporaryPath, destinationPath);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. The original operation's exception is more useful.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup. The original operation's exception is more useful.
        }
    }
}
