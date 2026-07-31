using System.IO;
using Microsoft.Extensions.Logging;

namespace GtaRpAssistant.App;

public sealed class RollingFileLoggerProvider(string directory, long maxBytes = 2 * 1024 * 1024, int maxFiles = 5) : ILoggerProvider
{
    private readonly object _gate = new();
    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(categoryName, Write);
    public void Dispose() { }

    private void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "assistant.log");
                if (File.Exists(path) && new FileInfo(path).Length >= maxBytes) Rotate(path);
                var safe = message.Replace('\r', ' ').Replace('\n', ' ');
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O}\t{level}\t{category}\t{eventId.Id}\t{safe}{Environment.NewLine}");
            }
        }
        catch (IOException ex) { System.Diagnostics.Debug.WriteLine($"Logger I/O failure: {ex.GetType().Name}"); }
        catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine($"Logger access failure: {ex.GetType().Name}"); }
    }

    private void Rotate(string path)
    {
        var oldest = $"{path}.{maxFiles - 1}";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = maxFiles - 2; index >= 1; index--)
        {
            var source = $"{path}.{index}";
            if (File.Exists(source)) File.Move(source, $"{path}.{index + 1}", true);
        }
        File.Move(path, $"{path}.1", true);
    }

    private sealed class RollingFileLogger(string category, Action<LogLevel, string, EventId, string, Exception?> write) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) write(logLevel, category, eventId, formatter(state, exception), exception);
        }
    }
}

public sealed class SessionEventLogger(Microsoft.Extensions.Logging.ILogger<SessionEventLogger> logger) : GtaRpAssistant.Core.ISessionEventSink
{
    public void Write(GtaRpAssistant.Core.SessionEvent sessionEvent) => logger.LogInformation("{EventName}; state={State}; detail={Detail}", sessionEvent.Name, sessionEvent.State, sessionEvent.Detail ?? "none");
}
