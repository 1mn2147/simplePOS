namespace Pos.App.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("SIMPLEPOS_HOME");
        RootDirectory = string.IsNullOrWhiteSpace(overrideRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SimplePOS")
            : Path.GetFullPath(overrideRoot);
        DataDirectory = Path.Combine(RootDirectory, "data");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        SettingsPath = Path.Combine(RootDirectory, "settings.json");
        DefaultDatabasePath = Path.Combine(DataDirectory, "products.xlsx");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string SettingsPath { get; }
    public string DefaultDatabasePath { get; }
}
