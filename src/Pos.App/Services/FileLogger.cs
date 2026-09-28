using System.Text;

namespace Pos.App.Services;

public sealed class FileLogger(string logDirectory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task WriteAsync(string level, string message, Exception? exception = null)
    {
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, $"simplepos-{DateTime.Now:yyyy-MM-dd}.log");
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(path, line.AppendLine().ToString(), Encoding.UTF8);
        }
        finally
        {
            _gate.Release();
        }
    }
}
