using System.Diagnostics;
using System.Xml.Linq;
using Pos.Desktop.Services;

namespace Pos.Desktop.Tests;

public sealed class MacPlatformServiceTests : IDisposable
{
    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "SimplePOS.MacPlatform.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SingleInstanceGuard_BlocksSecondOwnerAndCanBeReacquiredAfterRelease()
    {
        if (!OperatingSystem.IsMacOS()) return;

        using var first = new MacSingleInstanceGuard("com.simplepos.test", _temporaryDirectory);
        using var second = new MacSingleInstanceGuard("com.simplepos.test", _temporaryDirectory);

        Assert.True(first.IsPrimaryInstance);
        Assert.False(second.IsPrimaryInstance);
        Assert.True(File.Exists(first.LockPath));

        first.Dispose();
        using var replacement = new MacSingleInstanceGuard("com.simplepos.test", _temporaryDirectory);
        Assert.True(replacement.IsPrimaryInstance);
    }

    [Fact]
    public void SingleInstanceGuard_DoesNotTreatInvalidStorageAsAnotherInstance()
    {
        if (!OperatingSystem.IsMacOS()) return;

        Directory.CreateDirectory(_temporaryDirectory);
        var fileInsteadOfDirectory = Path.Combine(_temporaryDirectory, "not-a-directory");
        File.WriteAllText(fileInsteadOfDirectory, "data");

        Assert.ThrowsAny<IOException>(() =>
            new MacSingleInstanceGuard("com.simplepos.test", fileInsteadOfDirectory));
    }

    [Fact]
    public void LoginItem_RoundTripsEscapedCommandAndOnlyTouchesInjectedDirectory()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var launchAgents = Path.Combine(_temporaryDirectory, "Launch Agents");
        var executable = Path.Combine(_temporaryDirectory, "Simple POS & Test");
        Directory.CreateDirectory(_temporaryDirectory);
        File.WriteAllText(executable, "test executable");
        var command = new MacLaunchCommand(
            executable,
            ["--database", "/tmp/상품 & 재고.xlsx"],
            new Dictionary<string, string> { ["SIMPLEPOS_HOME"] = "/tmp/Simple POS & Test" });
        var service = new MacLoginItemService("com.simplepos.test", launchAgents);

        service.SetRegistered(true, command);

        Assert.True(service.IsRegistered(command));
        Assert.False(service.IsRegistered(command with { Arguments = ["--different"] }));
        Assert.Single(Directory.EnumerateFiles(launchAgents));
        var document = XDocument.Load(service.PlistPath);
        Assert.Contains(document.Descendants("string"), value => value.Value == "/tmp/상품 & 재고.xlsx");
        Assert.Contains(document.Descendants("string"), value => value.Value == "/tmp/Simple POS & Test");
        using (var validation = Process.Start(new ProcessStartInfo
               {
                   FileName = "/usr/bin/plutil",
                   ArgumentList = { "-lint", service.PlistPath },
                   RedirectStandardOutput = true,
                   RedirectStandardError = true,
                   UseShellExecute = false,
               })!)
        {
            validation.WaitForExit();
            Assert.True(validation.ExitCode == 0, validation.StandardError.ReadToEnd());
        }

        service.SetRegistered(false, command);
        Assert.False(File.Exists(service.PlistPath));
    }

    [Fact]
    public void LoginItem_RejectsMissingExecutableWithoutPublishingPlist()
    {
        if (!OperatingSystem.IsMacOS()) return;

        var service = new MacLoginItemService("com.simplepos.test", _temporaryDirectory);
        var command = new MacLaunchCommand(Path.Combine(_temporaryDirectory, "missing"), []);

        Assert.Throws<FileNotFoundException>(() => service.SetRegistered(true, command));
        Assert.False(File.Exists(service.PlistPath));
    }

    [Fact]
    public void ScanSuccessSound_UsesAfplayWithoutShellOrAudibleTestSideEffect()
    {
        if (!OperatingSystem.IsMacOS()) return;

        Directory.CreateDirectory(_temporaryDirectory);
        var sound = Path.Combine(_temporaryDirectory, "success sound.aiff");
        File.WriteAllBytes(sound, [0]);
        ProcessStartInfo? captured = null;
        using var player = new MacScanSuccessSound(sound, startInfo =>
        {
            captured = startInfo;
            return null;
        });

        player.Play();

        Assert.NotNull(captured);
        Assert.Equal("/usr/bin/afplay", captured.FileName);
        Assert.False(captured.UseShellExecute);
        Assert.Equal([sound], captured.ArgumentList);
    }

    [Fact]
    public void ScanSuccessSound_DoesNotAccumulateOverlappingPlayerProcesses()
    {
        if (!OperatingSystem.IsMacOS()) return;

        Directory.CreateDirectory(_temporaryDirectory);
        var sound = Path.Combine(_temporaryDirectory, "success.aiff");
        File.WriteAllBytes(sound, [0]);
        var starts = 0;
        using var player = new MacScanSuccessSound(sound, _ =>
        {
            starts++;
            return Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sleep",
                ArgumentList = { "30" },
                UseShellExecute = false,
            });
        });

        player.Play();
        player.Play();

        Assert.Equal(1, starts);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
