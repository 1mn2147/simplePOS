using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Pos.Desktop.Tests.TestAppBuilder))]

namespace Pos.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<Application>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
