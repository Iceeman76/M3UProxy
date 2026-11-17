using System.Runtime.InteropServices;
using DotNetLab.M3UProxy;
using DotNetLab.M3UProxyApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;

IConfigurationRoot configuration;

try
{
    configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: false).Build();
}
catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or FormatException)
{
    Console.Error.WriteLine($"Could not load configuration ({exception.GetType().Name}). Check appsettings.json.");

    return 1;
}

var redactor = new LogRedactor(configuration);
ILoggerFactory loggerFactory;

try
{
    loggerFactory = ApplicationLogging.Create(configuration);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Could not initialize logging: {redactor.Redact(exception.Message)}");

    return 1;
}

using var logging = loggerFactory;
var logger = logging.CreateLogger("DotNetLab.M3UProxyApp.Application");
AnsiConsole.Write(new FigletText("M3U Proxy").Color(Color.Cyan1));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
using var termination = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    cancellation.Cancel();
});

try
{
    var mode = ApplicationModeSelector.Select(args);
    logger.LogInformation("Application starting in {Mode} mode", mode);
    var browserOptions = configuration.GetSection("Browser").Get<BrowserOptions>() ?? new BrowserOptions();
    var usesExecutablePath = string.IsNullOrWhiteSpace(browserOptions.ExecutablePath) is false;
    logger.LogInformation("Browser configured channel={Channel} executable_path={ExecutablePath} selection={Selection}", browserOptions.Channel.Trim().ToLowerInvariant(), usesExecutablePath ? browserOptions.ExecutablePath : "automatic", usesExecutablePath ? "executable_path" : "channel");

    return mode switch
    {
        ApplicationMode.Download => await DownloadMode.RunAsync(configuration, cancellation.Token, logging),
        ApplicationMode.Proxy => await ProxyMode.RunAsync(configuration, cancellation.Token, logging),
        _ => 1
    };
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    logger.LogInformation("Application cancellation requested");

    return 0;
}
catch (Exception exception)
{
    logger.LogError(exception, "Application failed");
    Console.Error.WriteLine(redactor.Redact(exception.Message));

    return 1;
}
finally
{
    logger.LogInformation("Application stopped");
}
