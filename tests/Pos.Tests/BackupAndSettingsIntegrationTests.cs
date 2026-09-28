using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Tests;

public sealed class BackupAndSettingsIntegrationTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Backup_UsesDatedUniqueNameAndReopensSuccessfully()
    {
        var dataDirectory = Path.Combine(_temporaryDirectory, "data");
        var backupDirectory = Path.Combine(_temporaryDirectory, "backups");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "products.xlsx");
        await new ExcelPosRepository(databasePath).CreateAsync();
        var service = new BackupService();

        var first = await service.CreateAsync(databasePath, backupDirectory, retentionCount: 3);
        var second = await service.CreateAsync(databasePath, backupDirectory, retentionCount: 3);

        Assert.Matches("^상품DB_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{6}(?:_[0-9]{2})?\\.xlsx$", Path.GetFileName(first.Path));
        Assert.NotEqual(first.Path, second.Path);
        Assert.True(await service.ValidateAsync(first.Path));
        Assert.Equal(first.Sha256, second.Sha256);
    }

    [Theory]
    [InlineData(OutOfStockPolicy.WarnAndAllow)]
    [InlineData(OutOfStockPolicy.Allow)]
    public async Task Settings_RoundTripPreservesOperationalPolicy(OutOfStockPolicy outOfStockPolicy)
    {
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, "settings.json");
        var service = new JsonSettingsService();
        var expected = new AppSettings
        {
            DatabasePath = "C:\\data\\products.xlsx",
            BackupDirectory = "D:\\backup",
            BackupIntervalDays = 7,
            BackupTime = new TimeOnly(2, 30),
            BackupRetentionCount = 14,
            OutOfStockPolicy = outOfStockPolicy,
            Theme = "Dark",
        };

        await service.SaveAsync(path, expected);
        var actual = await service.LoadAsync(path);

        Assert.Equal(expected, actual);
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
}
