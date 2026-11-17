using DotNetLab.M3UProxyApp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ProxyRequestLoggerTests
{
    [TestCase(200, LogLevel.Information)]
    [TestCase(206, LogLevel.Information)]
    [TestCase(302, LogLevel.Information)]
    [TestCase(401, LogLevel.Warning)]
    [TestCase(404, LogLevel.Warning)]
    [TestCase(429, LogLevel.Warning)]
    [TestCase(500, LogLevel.Error)]
    [TestCase(502, LogLevel.Error)]
    public async Task LogAsync_UsesResponseSeverityWithoutExposingCredentials(int statusCode, LogLevel expectedLevel)
    {
        var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(provider));
        var context = CreateContext();

        await ProxyRequestLogger.LogAsync(context, response =>
        {
            response.Response.StatusCode = statusCode;

            return Task.CompletedTask;
        }, [ProxyTestFactory.CreateSource()], true, factory.CreateLogger("Requests"));

        Assert.That(provider.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Debug, expectedLevel }));
        Assert.That(provider.Entries.All(entry => entry.Message.Contains("UPSTREAM")), Is.True);
        Assert.That(provider.Entries.All(entry => entry.Message.Contains("password") is false && entry.Message.Contains("/movie/") is false), Is.True);
    }

    [TestCase("Information", 200, 1)]
    [TestCase("Warning", 200, 0)]
    [TestCase("Warning", 401, 1)]
    [TestCase("Error", 401, 0)]
    [TestCase("Error", 502, 1)]
    [TestCase("None", 502, 0)]
    public async Task LogAsync_RespectsConfiguredMinimumLevel(string minimumLevel, int statusCode, int expectedCount)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = minimumLevel }).Build();
        var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddConfiguration(configuration.GetSection("Logging")).AddProvider(provider));
        var context = CreateContext();
        context.Response.StatusCode = statusCode;

        await ProxyRequestLogger.LogAsync(context, _ => Task.CompletedTask, [ProxyTestFactory.CreateSource()], true, factory.CreateLogger("Requests"));

        Assert.That(provider.Entries, Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public async Task LogAsync_LogsUnhandledExceptionAsErrorAndRethrowsWithoutExposingItsMessage()
    {
        var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.AddProvider(provider));
        var exception = new InvalidOperationException("http://provider.example/movie/user/password/123.mkv");

        await Assert.ThrowsAsync<InvalidOperationException>(() => ProxyRequestLogger.LogAsync(CreateContext(), _ => throw exception, [ProxyTestFactory.CreateSource()], true, factory.CreateLogger("Requests")));
        Assert.That(provider.Entries.Single().Level, Is.EqualTo(LogLevel.Error));
        Assert.That(provider.Entries.Single().Message, Does.Contain("outcome=failed").And.Not.Contain(exception.Message));
    }

    [Test]
    public async Task LogAsync_LogsClientCancellationAtDebugLevel()
    {
        var provider = new RecordingLoggerProvider();
        using var factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(provider));
        using var cancellation = new CancellationTokenSource();
        var context = CreateContext();
        context.RequestAborted = cancellation.Token;
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => ProxyRequestLogger.LogAsync(context, _ => throw new OperationCanceledException(cancellation.Token), [ProxyTestFactory.CreateSource()], true, factory.CreateLogger("Requests")));
        Assert.That(provider.Entries.Last().Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(provider.Entries.Last().Message, Does.Contain("outcome=aborted"));
    }

    [TestCase(0, 0, 0, 42, "42s")]
    [TestCase(0, 0, 12, 34, "12m34s")]
    [TestCase(0, 5, 12, 34, "5h12m")]
    [TestCase(3, 5, 12, 34, "3d5h")]
    public void FormatAge_UsesCompactReadableUnits(int days, int hours, int minutes, int seconds, string expected)
    {
        var age = new TimeSpan(days, hours, minutes, seconds);

        var result = ProxyRequestLogger.FormatAge(age);

        Assert.That(result, Is.EqualTo(expected));
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/movie/user/password/123.mkv";

        return context;
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Entries);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(List<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
