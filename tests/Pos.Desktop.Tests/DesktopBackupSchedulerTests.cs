using Pos.Core;
using Pos.Desktop.Services;
using Pos.Infrastructure;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using System.Diagnostics;

namespace Pos.Desktop.Tests;

public sealed class DesktopBackupSchedulerTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.Desktop.Backup.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DueScheduleCreatesValidatedBackupAndReportsStatus()
    {
        var databasePath = await CreateDatabaseAsync("data");
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups");
        var statuses = new List<string>();
        using var scheduler = new DesktopBackupScheduler(new BackupService(), status =>
        {
            lock (statuses) statuses.Add(status);
        });

        scheduler.Update(CreateSettings(databasePath, backupDirectory, TimeSpan.Zero));

        var backup = await WaitForBackupAsync(backupDirectory);
        Assert.True(await new BackupService().ValidateAsync(backup));
        lock (statuses)
        {
            Assert.Contains("백업 중", statuses);
            Assert.Contains(statuses, status => status.StartsWith("정상 · 최근", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task RecentBackupHonorsIntervalAndDoesNotCreateAnotherCopy()
    {
        var databasePath = await CreateDatabaseAsync("data");
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups");
        await new BackupService().CreateAsync(databasePath, backupDirectory, retentionCount: 5);
        var status = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new DesktopBackupScheduler(new BackupService(), value => status.TrySetResult(value));

        scheduler.Update(CreateSettings(
            databasePath,
            backupDirectory,
            TimeSpan.Zero,
            intervalDays: 7));

        Assert.StartsWith("정상 · 최근", await status.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(Directory.EnumerateFiles(backupDirectory, "상품DB_*.xlsx"));
    }

    [Fact]
    public async Task SettingsChangeUsesOnlyTheUpdatedBackupDirectory()
    {
        var databasePath = await CreateDatabaseAsync("data");
        var firstDirectory = Path.Combine(_temporaryDirectory, "old-backups");
        var secondDirectory = Path.Combine(_temporaryDirectory, "new-backups");
        using var scheduler = new DesktopBackupScheduler(new BackupService(), _ => { });
        scheduler.Update(CreateSettings(
            Path.Combine(_temporaryDirectory, "missing.xlsx"),
            firstDirectory,
            TimeSpan.Zero));
        scheduler.Update(CreateSettings(databasePath, secondDirectory, TimeSpan.Zero));

        await WaitForBackupAsync(secondDirectory);
        Assert.False(Directory.Exists(firstDirectory));
        Assert.Single(Directory.EnumerateFiles(secondDirectory, "상품DB_*.xlsx"));
    }

    [Fact]
    public async Task DisposeStopsLaterUpdatesAndSameDirectoryIsRejected()
    {
        var dataDirectory = Path.Combine(_temporaryDirectory, "data");
        var databasePath = await CreateDatabaseAsync("data");
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new DesktopBackupScheduler(new BackupService(), status =>
        {
            if (status == "백업 실패") failure.TrySetResult(status);
        });

        scheduler.Update(CreateSettings(databasePath, dataDirectory, TimeSpan.Zero));
        Assert.Equal("백업 실패", await failure.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(Directory.EnumerateFiles(dataDirectory, "상품DB_*.xlsx"));

        scheduler.Dispose();
        var afterDisposeDirectory = Path.Combine(_temporaryDirectory, "after-dispose");
        scheduler.Update(CreateSettings(databasePath, afterDisposeDirectory, TimeSpan.Zero));
        await Task.Delay(100);
        Assert.False(Directory.Exists(afterDisposeDirectory));
    }

    [AvaloniaFact]
    public async Task DisposeFromUiThreadCancelsAndDrainsActiveBackupBeforeReturning()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess());
        var databasePath = await CreateDatabaseAsync("drain-data");
        var backupDirectory = Path.Combine(_temporaryDirectory, "drain-backups");
        using var backupStarted = new ManualResetEventSlim();
        using var allowBackupToContinue = new ManualResetEventSlim();
        var scheduler = new DesktopBackupScheduler(new BackupService(), status =>
        {
            if (status == "백업 중")
            {
                backupStarted.Set();
                allowBackupToContinue.Wait();
            }
        });
        scheduler.Update(CreateSettings(databasePath, backupDirectory, TimeSpan.Zero));
        Assert.True(await Task.Run(() => backupStarted.Wait(TimeSpan.FromSeconds(5))));
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            allowBackupToContinue.Set();
        });

        var stopwatch = Stopwatch.StartNew();
        scheduler.Dispose();
        stopwatch.Stop();
        await release;

        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100));
        Assert.Empty(Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory, "*.tmp", SearchOption.TopDirectoryOnly)
            : []);
        var fileCount = Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory).Count()
            : 0;
        await Task.Delay(100);
        Assert.Equal(fileCount, Directory.Exists(backupDirectory)
            ? Directory.EnumerateFiles(backupDirectory).Count()
            : 0);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<string> CreateDatabaseAsync(string directoryName)
    {
        var directory = Path.Combine(_temporaryDirectory, directoryName);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "products.xlsx");
        await new ExcelPosRepository(path).CreateAsync();
        return path;
    }

    private static AppSettings CreateSettings(
        string databasePath,
        string backupDirectory,
        TimeSpan backupTime,
        int intervalDays = 1) => new()
    {
        DatabasePath = databasePath,
        BackupDirectory = backupDirectory,
        BackupIntervalDays = intervalDays,
        BackupTime = TimeOnly.FromTimeSpan(backupTime),
        BackupRetentionCount = 5,
    };

    private static async Task<string> WaitForBackupAsync(string directory)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (Directory.Exists(directory))
            {
                var backup = Directory.EnumerateFiles(directory, "상품DB_*.xlsx").SingleOrDefault();
                if (backup is not null)
                {
                    return backup;
                }
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("예약 백업이 생성되지 않았습니다.");
    }
}
