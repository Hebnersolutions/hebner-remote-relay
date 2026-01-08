using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Hebner.Agent.Service;

public sealed class IpcLogger : ILogger
{
    private readonly string _logFilePath;

    public IpcLogger()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HebnerRemoteSupport", "logs");
        Directory.CreateDirectory(logDir);
        _logFilePath = Path.Combine(logDir, "ipc.log");
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{logLevel}] {message}";

        try
        {
            File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
        }
        catch
        {
            // Ignore logging errors
        }
    }
}

public static class IpcLoggerExtensions
{
    public static ILogger CreateIpcLogger(this ILoggerFactory factory)
    {
        return new IpcLogger();
    }
}
