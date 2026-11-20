using Pos.App.Services;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop;

internal sealed class DesktopSession(AppPaths paths, JsonSettingsService preferences,
    AppSettings settings, ExcelPosRepository repository)
{
    public AppSettings Settings { get; private set; } = settings;
    public ExcelPosRepository Repository { get; private set; } = repository;

    public static async Task<DesktopSession> OpenAsync(AppPaths paths, string? databaseArgument, bool loginEnabled = false)
    {
        var preferences = new JsonSettingsService();
        var settings = await preferences.LoadAsync(paths.SettingsPath);
        var selectedPath = databaseArgument ?? settings.DatabasePath;
        var useDefault = string.IsNullOrWhiteSpace(selectedPath);
        var repository = new ExcelPosRepository(useDefault ? paths.DefaultDatabasePath : selectedPath!,
            settings.OutOfStockPolicy);
        if (!File.Exists(repository.DatabasePath))
        {
            if (!useDefault)
            {
                throw new FileNotFoundException($"지정한 DB를 찾을 수 없습니다: {repository.DatabasePath}");
            }

            await repository.CreateAsync();
        }

        return new DesktopSession(paths, preferences, settings with
        {
            RunAtStartup = loginEnabled,
            DatabasePath = repository.DatabasePath,
            BackupDirectory = string.IsNullOrWhiteSpace(settings.BackupDirectory)
                ? Path.Combine(paths.RootDirectory, "backups")
                : settings.BackupDirectory,
        }, repository);
    }

    public async Task<IPosRepository> ApplyAsync(AppSettings updated, Action<bool> registerAtLogin)
    {
        var next = new ExcelPosRepository(updated.DatabasePath!, updated.OutOfStockPolicy);
        await next.GetProductsAsync();
        var changeLogin = updated.RunAtStartup != Settings.RunAtStartup;
        if (changeLogin)
        {
            registerAtLogin(updated.RunAtStartup);
        }

        try
        {
            await preferences.SaveAsync(paths.SettingsPath, updated);
        }
        catch
        {
            if (changeLogin)
            {
                registerAtLogin(Settings.RunAtStartup);
            }
            throw;
        }

        Settings = updated;
        Repository = next;
        return next;
    }
}
