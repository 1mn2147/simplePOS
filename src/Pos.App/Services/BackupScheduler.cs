using System.Globalization;
using System.Windows.Threading;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.App.Services;

public sealed class BackupScheduler : IDisposable
{
    private readonly BackupService _backupService;
    private readonly FileLogger _logger;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings? _settings;

    public BackupScheduler(BackupService backupService, FileLogger logger)
    {
        _backupService = backupService;
        _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += async (_, _) => await RunIfDueAsync();
    }

    public event EventHandler<string>? StatusChanged;

    public void Start(AppSettings settings)
    {
        _settings = settings;
        _timer.Start();
        _ = RunIfDueAsync();
    }

    public void Update(AppSettings settings)
    {
        _settings = settings;
        _ = RunIfDueAsync();
    }

    public async Task RunIfDueAsync()
    {
        var settings = _settings;
        var databasePath = settings?.DatabasePath;
        var backupDirectory = settings?.BackupDirectory;
        if (settings is null || databasePath is null || backupDirectory is null || !File.Exists(databasePath))
        {
            return;
        }

        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var scheduled = new DateTimeOffset(
                now.Year, now.Month, now.Day,
                settings.BackupTime.Hour, settings.BackupTime.Minute, 0,
                now.Offset);
            if (now < scheduled)
            {
                scheduled = scheduled.AddDays(-settings.BackupIntervalDays);
            }

            var latest = GetLatestBackupTime(backupDirectory);
            if (latest.HasValue && latest.Value >= scheduled)
            {
                StatusChanged?.Invoke(this, $"정상 · 최근 {latest.Value:MM.dd HH:mm}");
                return;
            }

            StatusChanged?.Invoke(this, "백업 중");
            var result = await _backupService.CreateAsync(
                databasePath,
                backupDirectory,
                settings.BackupRetentionCount);
            StatusChanged?.Invoke(this, $"정상 · 최근 {result.CreatedAt:MM.dd HH:mm}");
            await _logger.WriteAsync("INFO", $"Scheduled backup created: {result.Path}");
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke(this, "백업 실패");
            await _logger.WriteAsync("ERROR", "Scheduled backup failed.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _gate.Dispose();
    }

    private static DateTimeOffset? GetLatestBackupTime(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        DateTimeOffset? latest = null;
        foreach (var path in Directory.EnumerateFiles(directory, "상품DB_*.xlsx", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var timestamp = name.Length >= 22 ? name.Substring(5, 17) : string.Empty;
            if (!DateTime.TryParseExact(
                    timestamp,
                    "yyyy-MM-dd_HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                continue;
            }

            var value = new DateTimeOffset(parsed);
            if (!latest.HasValue || value > latest.Value)
            {
                latest = value;
            }
        }

        return latest;
    }
}
