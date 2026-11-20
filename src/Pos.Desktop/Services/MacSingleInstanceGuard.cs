using System.Security.Cryptography;
using System.Text;

namespace Pos.Desktop.Services;

public sealed class MacSingleInstanceGuard : IDisposable
{
    private FileStream? _lockStream;

    public MacSingleInstanceGuard(string applicationIdentifier, string? lockDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationIdentifier);
        EnsureMacOS();

        var directory = lockDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimplePOS");
        Directory.CreateDirectory(directory);

        var identifierHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(applicationIdentifier)))[..16];
        LockPath = Path.Combine(directory, $"instance-{identifierHash}.lock");

        try
        {
            _lockStream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            IsPrimaryInstance = true;
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) == 35)
        {
            // On Darwin a conflicting flock reports EWOULDBLOCK.
            IsPrimaryInstance = false;
        }
    }

    public bool IsPrimaryInstance { get; }

    public string LockPath { get; }

    public void Dispose()
    {
        _lockStream?.Dispose();
        _lockStream = null;
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("이 단일 실행 잠금은 macOS에서만 지원됩니다.");
        }
    }
}
