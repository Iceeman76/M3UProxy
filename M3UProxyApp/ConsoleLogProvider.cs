using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

[ProviderAlias("Console")]
internal sealed class ConsoleLogProvider(LogRedactor redactor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogEntryLogger(categoryName, redactor, Console.WriteLine);

    public void Dispose() { }
}
