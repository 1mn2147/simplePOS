using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pos.App.Services;
using Pos.App.ViewModels;
using Pos.Infrastructure;

namespace Pos.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private BackupScheduler? _backupScheduler;
    private ShellViewModel? _shell;
    private FileLogger? _logger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new SingleInstanceGuard("SimplePOS.SingleInstance");
        if (!_singleInstance.IsPrimaryInstance)
        {
            MessageBox.Show("Simple POS가 이미 실행 중입니다.", "Simple POS", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        _logger = new FileLogger(paths.LogDirectory);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            var settingsService = new JsonSettingsService();
            var backupService = new BackupService();
            _shell = new ShellViewModel(
                paths,
                settingsService,
                backupService,
                new StartupRegistrationService(),
                new DialogService());
            await _shell.InitializeAsync();

            var previewArgumentIndex = Array.FindIndex(
                e.Args,
                argument => string.Equals(argument, "--render-preview", StringComparison.OrdinalIgnoreCase));
            if (previewArgumentIndex >= 0)
            {
                var outputPath = previewArgumentIndex + 1 < e.Args.Length
                    ? Path.GetFullPath(e.Args[previewArgumentIndex + 1])
                    : Path.Combine(paths.RootDirectory, "preview.png");
                var previewWidth = ReadIntegerArgument(e.Args, "--preview-width", 1280);
                var previewHeight = ReadIntegerArgument(e.Args, "--preview-height", 800);
                var previewPage = ReadStringArgument(e.Args, "--preview-page", "main");
                await _shell.PreparePreviewDataAsync();
                await _shell.SelectPreviewPageAsync(previewPage);
                await RenderPreviewAsync(_shell, outputPath, previewWidth, previewHeight);
                await _logger.WriteAsync("INFO", $"UI preview rendered: {outputPath}");
                Shutdown();
                return;
            }

            if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
            {
                await _logger.WriteAsync("INFO", "Application smoke test completed.");
                Shutdown();
                return;
            }

            _backupScheduler = new BackupScheduler(backupService, _logger);
            _backupScheduler.StatusChanged += (_, status) => _shell.SetBackupStatus(status);
            _shell.SettingsChanged += (_, settings) => _backupScheduler.Update(settings);
            _backupScheduler.Start(_shell.Settings);

            var window = new MainWindow { DataContext = _shell };
            if (_shell.Settings.StartFullScreen)
            {
                window.WindowState = WindowState.Maximized;
            }

            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            await _logger.WriteAsync("FATAL", "Application startup failed.", exception);
            MessageBox.Show(
                $"Simple POS를 시작하지 못했습니다.\n\n{exception.Message}",
                "시작 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _backupScheduler?.Dispose();
        _shell?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_logger is not null)
        {
            await _logger.WriteAsync("ERROR", "Unhandled UI exception.", e.Exception);
        }

        MessageBox.Show(
            $"작업을 완료하지 못했습니다.\n\n{e.Exception.Message}",
            "Simple POS 오류",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static async Task RenderPreviewAsync(
        ShellViewModel shell,
        string outputPath,
        int previewWidth,
        int previewHeight)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException("미리보기 출력 폴더를 확인할 수 없습니다."));
        var window = new MainWindow
        {
            DataContext = shell,
            Width = Math.Max(900, previewWidth),
            Height = Math.Max(640, previewHeight),
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        await Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();

        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        window.Close();
    }

    private static int ReadIntegerArgument(string[] arguments, string name, int defaultValue)
    {
        var index = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length && int.TryParse(arguments[index + 1], out var value)
            ? value
            : defaultValue;
    }

    private static string ReadStringArgument(string[] arguments, string name, string defaultValue)
    {
        var index = Array.FindIndex(
            arguments,
            argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : defaultValue;
    }
}
