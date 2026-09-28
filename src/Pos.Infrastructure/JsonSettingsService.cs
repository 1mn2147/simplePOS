using System.Text.Json;
using System.Text.Json.Serialization;
using Pos.Core;

namespace Pos.Infrastructure;

public sealed class JsonSettingsService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options;

    public JsonSettingsService()
    {
        _options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task<AppSettings> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(fullPath))
            {
                return new AppSettings();
            }

            try
            {
                await using var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                    stream,
                    _options,
                    cancellationToken).ConfigureAwait(false);

                settings ??= new AppSettings();
                Validate(settings);
                return settings;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The settings file is not valid JSON.", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string path,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                await using (var stream = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 16 * 1024,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        _options,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                await ValidateFileAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                AtomicFileCommit.Replace(temporaryPath, fullPath);
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

    private async Task ValidateFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
            stream,
            _options,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("The temporary settings file is empty.");
        Validate(settings);
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.BackupIntervalDays < 1)
        {
            throw new InvalidDataException("BackupIntervalDays must be at least one.");
        }

        if (settings.BackupRetentionCount < 1)
        {
            throw new InvalidDataException("BackupRetentionCount must be at least one.");
        }

        if (settings.ScannerMaximumInterKeyDelayMilliseconds < 1)
        {
            throw new InvalidDataException("ScannerMaximumInterKeyDelayMilliseconds must be positive.");
        }

        if (settings.ScannerPartialInputTimeoutMilliseconds < 1)
        {
            throw new InvalidDataException("ScannerPartialInputTimeoutMilliseconds must be positive.");
        }

        if (settings.MinimumBarcodeLength < 1)
        {
            throw new InvalidDataException("MinimumBarcodeLength must be at least one.");
        }

        if (string.IsNullOrWhiteSpace(settings.ScannerTerminatorKey))
        {
            throw new InvalidDataException("ScannerTerminatorKey is required.");
        }

        if (!Enum.IsDefined(settings.OutOfStockPolicy))
        {
            throw new InvalidDataException("OutOfStockPolicy is not supported.");
        }
    }
}
