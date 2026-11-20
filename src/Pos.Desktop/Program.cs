using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Pos.App.Services;
using Pos.Core;
using Pos.Infrastructure;

namespace Pos.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 1)
        {
            Console.Error.WriteLine("사용법: Pos.Desktop [SimplePOS DB.xlsx]");
            return 2;
        }

        try
        {
            var paths = new AppPaths();
            var settings = new JsonSettingsService().LoadAsync(paths.SettingsPath).GetAwaiter().GetResult();
            var selectedPath = args.FirstOrDefault() ?? settings.DatabasePath;
            var useDefault = string.IsNullOrWhiteSpace(selectedPath);
            var database = new ExcelPosRepository(useDefault ? paths.DefaultDatabasePath : selectedPath!,
                settings.OutOfStockPolicy);
            if (!File.Exists(database.DatabasePath))
            {
                if (!useDefault)
                {
                    Console.Error.WriteLine($"지정한 DB를 찾을 수 없습니다: {database.DatabasePath}");
                    return 1;
                }
                database.CreateAsync().GetAwaiter().GetResult();
            }

            return AppBuilder.Configure(() => new DesktopApp(database, settings))
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"SimplePOS를 시작할 수 없습니다: {error.Message}");
            return 1;
        }
    }
}

internal sealed class DesktopApp(IPosRepository repository, AppSettings settings) : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SalesWindow(repository, settings.OutOfStockPolicy)
            {
                WindowState = settings.StartFullScreen ? WindowState.Maximized : WindowState.Normal,
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
