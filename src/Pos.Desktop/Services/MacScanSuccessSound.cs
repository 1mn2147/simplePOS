using System.Diagnostics;

namespace Pos.Desktop.Services;

public sealed class MacScanSuccessSound : IDisposable
{
    public const string DefaultSoundPath = "/System/Library/Sounds/Glass.aiff";

    private readonly Func<ProcessStartInfo, Process?> _startProcess;
    private readonly object _sync = new();
    private Process? _playingProcess;

    public MacScanSuccessSound(
        string soundPath = DefaultSoundPath,
        Func<ProcessStartInfo, Process?>? startProcess = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundPath);
        EnsureMacOS();
        SoundPath = Path.GetFullPath(soundPath);
        _startProcess = startProcess ?? Process.Start;
    }

    public string SoundPath { get; }

    public void Play()
    {
        if (!File.Exists(SoundPath))
        {
            return;
        }

        lock (_sync)
        {
            if (_playingProcess is not null && !_playingProcess.HasExited)
            {
                return;
            }

            _playingProcess?.Dispose();
            _playingProcess = _startProcess(CreateStartInfo(SoundPath));
        }
    }

    public static ProcessStartInfo CreateStartInfo(string soundPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(soundPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/afplay",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(Path.GetFullPath(soundPath));
        return startInfo;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_playingProcess is not null)
            {
                try
                {
                    if (!_playingProcess.HasExited)
                    {
                        _playingProcess.Kill();
                        _playingProcess.WaitForExit();
                    }
                }
                catch (InvalidOperationException)
                {
                    // The short system sound may exit between the status check and cleanup.
                }
                _playingProcess.Dispose();
            }
            _playingProcess = null;
        }
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("스캔 성공음은 macOS에서만 지원됩니다.");
        }
    }
}
