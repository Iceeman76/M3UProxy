using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

internal sealed class LogEntryLogger(string category, LogRedactor redactor, Action<string> write) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel is not LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel) is false)
        {
            return;
        }

        var message = formatter(state, exception);
        var error = exception is null ? string.Empty : $" exception={exception.GetType().Name}: {exception.Message}";
        write($"{DateTimeOffset.UtcNow:O} [{logLevel}] {redactor.Redact(category)} {redactor.Redact(message + error)}");
    }
}
