using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Pos.App.Services;
using Pos.Core;
using Pos.Desktop.Services;
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
            using var instance = OperatingSystem.IsMacOS()
                ? new MacSingleInstanceGuard("SimplePOS", paths.RootDirectory)
                : null;
            if (instance is { IsPrimaryInstance: false })
            {
                Console.Error.WriteLine("SimplePOS가 이미 실행 중입니다.");
                return 1;
            }

            var loginEnabled = OperatingSystem.IsMacOS() &&
                new MacLoginItemService().IsRegistered(MacLaunchCommand.ForCurrentProcess());
            var session = DesktopSession.OpenAsync(paths, args.FirstOrDefault(), loginEnabled).GetAwaiter().GetResult();
            return AppBuilder.Configure(() => new DesktopApp(session))
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

internal sealed class DesktopApp(DesktopSession session) : Application
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
            var backup = new BackupService();
            var sound = OperatingSystem.IsMacOS() ? new MacScanSuccessSound() : null;
            var scheduler = new DesktopBackupScheduler(backup, message => Dispatcher.UIThread.Post(() =>
                (desktop.MainWindow as SalesWindow)?.SetBackupStatus(message)));
            var window = new SalesWindow(session.Repository, session.Settings, ApplySettingsAsync,
                () => sound?.Play(), backup)
            {
                WindowState = session.Settings.StartFullScreen ? WindowState.Maximized : WindowState.Normal,
            };
            desktop.MainWindow = window;
            window.Opened += (_, _) => scheduler.Update(session.Settings);
            desktop.Exit += (_, _) =>
            {
                scheduler.Dispose();
                sound?.Dispose();
            };

            async Task<IPosRepository> ApplySettingsAsync(AppSettings updated)
            {
                var repository = await session.ApplyAsync(updated, registered =>
                    new MacLoginItemService().SetRegistered(registered, MacLaunchCommand.ForCurrentProcess()));
                scheduler.Update(updated);
                if (desktop.MainWindow is { } mainWindow)
                {
                    mainWindow.WindowState = updated.StartFullScreen ? WindowState.Maximized : WindowState.Normal;
                }
                return repository;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
