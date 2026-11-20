using System.Globalization;
using System.Text.RegularExpressions;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Services;

public sealed class DesktopBackupScheduler : IDisposable
{
    private static readonly Regex BackupNamePattern = new(
        "^상품DB_(?<timestamp>[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6})(?:_[0-9]{2})?\\.xlsx$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly BackupService _backupService;
    private readonly Action<string> _report;
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private readonly HashSet<Task> _activeRuns = [];
    private readonly List<CancellationTokenSource> _retiredSettingsLifetimes = [];
    private readonly Task _timerTask;
    private CancellationTokenSource? _settingsLifetime;
    private AppSettings? _settings;
    private bool _disposed;

    public DesktopBackupScheduler(BackupService service, Action<string> report)
    {
        _backupService = service ?? throw new ArgumentNullException(nameof(service));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _timerTask = RunTimerAsync(_lifetime.Token);
    }

    public void Update(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Task run;
        CancellationTokenSource runLifetime;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _settings = settings;
            if (_settingsLifetime is not null)
            {
                _settingsLifetime.Cancel();
                _retiredSettingsLifetimes.Add(_settingsLifetime);
            }
            runLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _settingsLifetime = runLifetime;
            var token = runLifetime.Token;
            run = Task.Run(() => RunIfDueAsync(settings, token), CancellationToken.None);
            _activeRuns.Add(run);
        }

        _ = run.ContinueWith(
            completed =>
            {
                lock (_sync)
                {
                    _activeRuns.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Dispose()
    {
        CancellationTokenSource[] settingsLifetimes;
        Task[] activeRuns;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _settings = null;
            var settingsLifetime = _settingsLifetime;
            _settingsLifetime = null;
            settingsLifetime?.Cancel();
            _lifetime.Cancel();
            activeRuns = _activeRuns.ToArray();
            settingsLifetimes = settingsLifetime is null
                ? _retiredSettingsLifetimes.ToArray()
                : [.. _retiredSettingsLifetimes, settingsLifetime];
            _retiredSettingsLifetimes.Clear();
        }

        try
        {
            Task.WhenAll([_timerTask, .. activeRuns]).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _runGate.Wait();
        _runGate.Release();
        foreach (var settingsLifetime in settingsLifetimes)
        {
            settingsLifetime.Dispose();
        }
        _lifetime.Dispose();
        _runGate.Dispose();
    }

    private async Task RunTimerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                AppSettings? settings;
                CancellationToken settingsToken;
                lock (_sync)
                {
                    settings = _settings;
                    settingsToken = _settingsLifetime?.Token ?? cancellationToken;
                }

                if (settings is not null)
                {
                    await RunIfDueAsync(settings, settingsToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunIfDueAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var databasePath = settings.DatabasePath;
        var backupDirectory = settings.BackupDirectory;
        if (string.IsNullOrWhiteSpace(databasePath) ||
            string.IsNullOrWhiteSpace(backupDirectory) ||
            !File.Exists(databasePath))
        {
            return;
        }

        try
        {
            await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.Now;
            var latest = GetLatestBackupTime(backupDirectory);
            var next = GetNextScheduledTime(now, latest, settings.BackupIntervalDays, settings.BackupTime);
            if (now < next)
            {
                Report(latest.HasValue
                    ? $"정상 · 최근 {latest.Value:MM.dd HH:mm}"
                    : $"예약 · 다음 {next:MM.dd HH:mm}");
                return;
            }

            Report("백업 중");
            var result = await _backupService.CreateAsync(
                databasePath,
                backupDirectory,
                settings.BackupRetentionCount,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Report($"정상 · 최근 {result.CreatedAt:MM.dd HH:mm}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Report("백업 실패");
        }
        finally
        {
            _runGate.Release();
        }
    }

    private void Report(string status)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
        }

        try
        {
            _report(status);
        }
        catch
        {
            // Status reporting must not stop scheduled backups.
        }
    }

    private static DateTimeOffset GetNextScheduledTime(
        DateTimeOffset now,
        DateTimeOffset? latest,
        int intervalDays,
        TimeOnly backupTime)
    {
        var days = Math.Max(1, intervalDays);
        var scheduledDate = latest?.LocalDateTime.Date.AddDays(days) ?? now.Date;
        var local = scheduledDate.Add(backupTime.ToTimeSpan());
        return new DateTimeOffset(local, now.Offset);
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
            var match = BackupNamePattern.Match(Path.GetFileName(path));
            if (!match.Success || !DateTime.TryParseExact(
                    match.Groups["timestamp"].Value,
                    "yyyy-MM-dd_HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                continue;
            }

            var timestamp = new DateTimeOffset(parsed);
            if (!latest.HasValue || timestamp > latest.Value)
            {
                latest = timestamp;
            }
        }

        return latest;
    }
}
