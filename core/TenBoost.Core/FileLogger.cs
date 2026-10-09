using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TenBoost.Core;

/// <summary>Лог в файл с ротацией по размеру; этот файл пользователь отдаёт в поддержку.</summary>
public sealed class FileLoggerProvider(string directory, string fileName, long maxBytes = 1024 * 1024, int keep = 3) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, Logger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new Logger(this, name.Split('.')[^1]));

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, fileName);
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
                {
                    Rotate(path);
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Rotate(string path)
    {
        for (var i = keep - 1; i >= 1; i--)
        {
            var older = $"{path}.{i}";
            if (File.Exists(older))
            {
                File.Move(older, $"{path}.{i + 1}", overwrite: true);
            }
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }

    private sealed class Logger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {logLevel,-11} {category}: {formatter(state, exception)}";
            owner.Write(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
