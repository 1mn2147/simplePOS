using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace Pos.Infrastructure;

public sealed record BackupResult(
    string Path,
    string Sha256,
    long Length,
    DateTimeOffset CreatedAt);

public sealed class BackupService
{
    private const string BackupPrefix = "상품DB_";
    private static readonly Regex BackupNamePattern = new(
        "^상품DB_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}(?:_[0-9]{2})?\\.xlsx$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;

    public BackupService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BackupResult> CreateAsync(
        string databasePath,
        string backupDirectory,
        int retentionCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectory);

        if (retentionCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionCount), "Retention count must be at least one.");
        }

        var sourcePath = Path.GetFullPath(databasePath);
        var destinationDirectory = Path.GetFullPath(backupDirectory);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The database workbook does not exist.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only .xlsx database workbooks can be backed up.");
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("The database path has no parent directory.");
        if (PathsEqual(sourceDirectory, destinationDirectory))
        {
            throw new InvalidOperationException("The backup directory must be different from the database directory.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(destinationDirectory);

            var timestamp = _timeProvider.GetLocalNow();
            var destinationPath = GetAvailableDestinationPath(destinationDirectory, timestamp);
            var temporaryPath = Path.Combine(
                destinationDirectory,
                $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                var sourceHashBefore = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
                await CopyToNewFileAsync(sourcePath, temporaryPath, cancellationToken).ConfigureAwait(false);

                var temporaryHash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
                var sourceHashAfter = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
                if (!sourceHashBefore.Equals(sourceHashAfter, StringComparison.Ordinal)
                    || !temporaryHash.Equals(sourceHashAfter, StringComparison.Ordinal))
                {
                    throw new IOException("The database changed while the backup was being created.");
                }

                if (new FileInfo(sourcePath).Length != new FileInfo(temporaryPath).Length)
                {
                    throw new IOException("The backup copy size does not match the database.");
                }

                if (!await ValidateAsync(temporaryPath, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidDataException("The backup copy could not be reopened as an .xlsx workbook.");
                }

                File.Move(temporaryPath, destinationPath);

                await EnforceRetentionAsync(destinationDirectory, retentionCount, cancellationToken).ConfigureAwait(false);

                var info = new FileInfo(destinationPath);
                return new BackupResult(destinationPath, temporaryHash, info.Length, timestamp);
            }
            catch
            {
                AtomicFileCommit.TryDelete(temporaryPath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ValidateAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            cancellationToken.ThrowIfCancellationRequested();
            using var workbook = new XLWorkbook(stream);
            return workbook.Worksheets.Count > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task CopyToNewFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string GetAvailableDestinationPath(string directory, DateTimeOffset timestamp)
    {
        var stem = $"{BackupPrefix}{timestamp:yyyy-MM-dd_HHmmss}";
        for (var suffix = 0; suffix <= 99; suffix++)
        {
            var fileName = suffix == 0 ? $"{stem}.xlsx" : $"{stem}_{suffix:00}.xlsx";
            var candidate = Path.Combine(directory, fileName);
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("No available backup file name remained for this second.");
    }

    private async Task EnforceRetentionAsync(
        string directory,
        int retentionCount,
        CancellationToken cancellationToken)
    {
        var candidates = new DirectoryInfo(directory)
            .EnumerateFiles("*.xlsx", SearchOption.TopDirectoryOnly)
            .Where(file => BackupNamePattern.IsMatch(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();

        var managedBackups = new List<FileInfo>();
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await HasSimplePosMetadataAsync(candidate.FullName, cancellationToken).ConfigureAwait(false))
            {
                managedBackups.Add(candidate);
            }
        }

        foreach (var backup in managedBackups.Skip(retentionCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            backup.Delete();
        }
    }

    private static async Task<bool> HasSimplePosMetadataAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            cancellationToken.ThrowIfCancellationRequested();
            using var workbook = new XLWorkbook(stream);
            if (!workbook.TryGetWorksheet("Meta", out var meta))
            {
                return false;
            }

            var lastRow = meta.LastRowUsed()?.RowNumber() ?? 0;
            for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
            {
                if (string.Equals(meta.Cell(rowNumber, 1).GetFormattedString().Trim(), "DatabaseKind", StringComparison.Ordinal)
                    && string.Equals(meta.Cell(rowNumber, 2).GetFormattedString().Trim(), "SimplePOS", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }
}
