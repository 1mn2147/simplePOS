using Pos.App.Services;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop.Tests;

public sealed class DesktopSessionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("SimplePOS.Session.").FullName;

    [Fact]
    public async Task SavingSettingsSwitchesRepositoryAndReopensSelectedDatabase()
    {
        var paths = new AppPaths(_directory);
        var session = await DesktopSession.OpenAsync(paths, null);
        var second = Path.Combine(paths.DataDirectory, "다른 매장.xlsx");
        var repo = new ExcelPosRepository(second);
        await repo.CreateAsync();
        await repo.SaveProductAsync(new Product { Name = "보리차", Price = 1500 });
        var updated = session.Settings with { DatabasePath = second, ScannerSuccessSoundEnabled = false };

        var active = await session.ApplyAsync(updated, _ => throw new Exception("로그인 설정이 변경되지 않았습니다."));
        Assert.Equal("보리차", Assert.Single(await active.GetProductsAsync()).Name);
        var reopened = await DesktopSession.OpenAsync(paths, null);
        Assert.Equal(second, reopened.Repository.DatabasePath);
        Assert.False(reopened.Settings.ScannerSuccessSoundEnabled);
    }

    [Fact]
    public async Task SettingsWriteFailureRollsBackLoginAndKeepsCurrentSession()
    {
        var paths = new AppPaths(_directory);
        var session = await DesktopSession.OpenAsync(paths, null);
        var current = session.Settings;
        Directory.CreateDirectory(paths.SettingsPath);
        var changes = new List<bool>();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            session.ApplyAsync(current with { RunAtStartup = true }, changes.Add));

        Assert.Equal(new[] { true, false }, changes);
        Assert.Equal(current, session.Settings);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
