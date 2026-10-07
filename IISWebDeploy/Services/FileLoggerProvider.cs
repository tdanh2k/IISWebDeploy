using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace IISWebDeploy.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _retainedFiles;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeLock = new();
    private bool _disposed;

    public FileLoggerProvider(string path, long maxBytes, int retainedFiles)
    {
        _path = path;
        _maxBytes = Math.Clamp(maxBytes, 1024, 1024L * 1024 * 1024);
        _retainedFiles = Math.Clamp(retainedFiles, 1, 20);
    }

    public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));
    public void Dispose() => _disposed = true;

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        if (_disposed) return;
        try
        {
            var entry = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, level = level.ToString(), category, eventId = eventId.Id, message, exception = exception?.ToString() });
            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length + System.Text.Encoding.UTF8.GetByteCount(entry) + 1 > _maxBytes) Rotate();
                using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream);
                writer.WriteLine(entry);
            }
        }
        catch { /* Logging must never fail the host or request. */ }
    }

    private void Rotate()
    {
        for (var i = _retainedFiles; i >= 1; i--)
        {
            var source = i == 1 ? _path : $"{_path}.{i - 1}";
            var target = $"{_path}.{i}";
            if (!File.Exists(source)) continue;
            if (i == _retainedFiles) File.Delete(source);
            else File.Move(source, target, true);
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) provider.Write(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}

public static class FileLoggerChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iiswebdeploy-log-check-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "app.log");
        try
        {
            using var provider = new FileLoggerProvider(path, 1024 * 1024, 2);
            var logger = provider.CreateLogger("check");
            Parallel.For(0, 100, i => logger.LogInformation("entry {EntryId}", i));
            var lines = File.ReadAllLines(path);
            if (lines.Length != 100 || lines.Any(line => !line.Contains("entry", StringComparison.Ordinal) || !line.Contains("timestamp", StringComparison.Ordinal))) throw new InvalidOperationException("File logger concurrency self-check failed.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddConfiguredFileLogger(this ILoggingBuilder logging, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!configuration.GetValue("Logging:File:Enabled", true)) return logging;
        var relativePath = configuration["Logging:File:Path"] ?? "logs/application.log";
        if (Path.IsPathRooted(relativePath) || relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part == "..")) return logging;
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, relativePath));
            logging.AddProvider(new FileLoggerProvider(fullPath, configuration.GetValue("Logging:File:MaxBytes", 5 * 1024 * 1024), configuration.GetValue("Logging:File:RetainedFiles", 3)));
        }
        catch { /* Invalid/unwritable logging configuration must not prevent startup. */ }
        return logging;
    }
}
