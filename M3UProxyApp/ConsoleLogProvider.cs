using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotNetLab.M3UProxyApp;

[ProviderAlias("Console")]
internal sealed class ConsoleLogProvider(LogRedactor redactor) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new LogEntryLogger(categoryName, redactor, AnsiConsole.WriteLine);

    public void Dispose() { }
}
