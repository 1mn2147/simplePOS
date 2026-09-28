using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Pos.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistrationService
{
    private readonly string _shortcutPath;

    public StartupRegistrationService(string appName = "SimplePOS", string? startupDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);

        var directory = startupDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Startup, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The Windows Startup folder could not be resolved.");
        }

        _shortcutPath = Path.Combine(directory, $"{SanitizeFileName(appName)}.lnk");
    }

    public bool IsRegistered()
    {
        EnsureWindows();
        if (!File.Exists(_shortcutPath))
        {
            return false;
        }

        try
        {
            var targetPath = ReadShortcutTarget(_shortcutPath);
            return !string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath);
        }
        catch
        {
            return false;
        }
    }

    public void SetRegistered(
        bool registered,
        string executablePath)
    {
        EnsureWindows();

        if (!registered)
        {
            if (File.Exists(_shortcutPath))
            {
                File.Delete(_shortcutPath);
            }

            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var targetPath = Path.GetFullPath(executablePath);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException("The startup executable does not exist.", targetPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_shortcutPath)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(_shortcutPath)!,
            $".{Path.GetFileNameWithoutExtension(_shortcutPath)}.{Guid.NewGuid():N}.tmp.lnk");

        try
        {
            CreateShortcut(temporaryPath, targetPath);
            AtomicFileCommit.Replace(temporaryPath, _shortcutPath);
        }
        catch
        {
            AtomicFileCommit.TryDelete(temporaryPath);
            throw;
        }
    }

    private static string? ReadShortcutTarget(string shortcutPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows Script Host could not be started.");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            var shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("The startup shortcut could not be opened.");
            return shortcutType.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null) as string;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Windows Script Host could not be started.");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);

            var shortcutType = shortcut?.GetType()
                ?? throw new InvalidOperationException("The startup shortcut could not be created.");
            shortcutType.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [targetPath]);
            shortcutType.InvokeMember(
                "WorkingDirectory",
                System.Reflection.BindingFlags.SetProperty,
                binder: null,
                target: shortcut,
                args: [Path.GetDirectoryName(targetPath)!]);
            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Startup registration is only supported on Windows.");
        }
    }
}
