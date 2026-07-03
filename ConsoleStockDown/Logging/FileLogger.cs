using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ConsoleStockDown.Logging;

internal sealed class FileLogger : ILogger
{
    private static readonly ConcurrentDictionary<string, object> LockMap = new();
    private readonly string _name;
    private readonly string _baseFilePath;

    public FileLogger(string name, string baseFilePath)
    {
        _name = name;
        _baseFilePath = baseFilePath;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var now = DateTime.Now;
        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception is null)
            return;

        var filePath = ResolveDailyFilePath(now);
        var logRecord = $"[{now:yyyy-MM-dd HH:mm:ss.fff}][{logLevel}][{_name}] {message}";
        if (exception is not null)
        {
            logRecord += Environment.NewLine + exception;
        }

        var lockObject = LockMap.GetOrAdd(filePath, _ => new object());
        lock (lockObject)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
            File.AppendAllText(filePath, logRecord + Environment.NewLine);
        }
    }

    private string ResolveDailyFilePath(DateTime date)
    {
        var directory = Path.GetDirectoryName(_baseFilePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_baseFilePath);
        var extension = Path.GetExtension(_baseFilePath);
        var datedFileName = $"{fileNameWithoutExtension}-{date:yyyy-MM-dd}{extension}";

        return string.IsNullOrEmpty(directory)
            ? datedFileName
            : Path.Combine(directory, datedFileName);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
