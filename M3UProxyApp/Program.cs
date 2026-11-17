using System.Runtime.InteropServices;
using DotNetLab.M3UProxy;
using DotNetLab.M3UProxyApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;

IConfigurationRoot configuration;
ApplicationArguments arguments;

try
{
    arguments = ApplicationArguments.Parse(args, ServiceHosting.IsService);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);

    return 1;
}

try
{
    configuration = ApplicationConfiguration.Load(arguments.ConfigurationPath);
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException)
{
    Console.Error.WriteLine($"Could not load configuration ({exception.GetType().Name}). Check the selected configuration file and its permissions.");

    return 1;
}

var redactor = new LogRedactor(configuration);
using var loadedConfiguration = (IDisposable)configuration;
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
    var interactive = arguments.Service is false && Environment.UserInteractive && Console.IsInputRedirected is false;

    if (interactive)
    {
        AnsiConsole.Write(new FigletText("M3U Proxy").Color(Color.Cyan1));
    }

    var mode = arguments.Mode ?? (interactive ? ApplicationModeSelector.Select([]) : throw new InvalidOperationException("Choose --proxy, --download, or --service when no interactive terminal is available."));
    logger.LogInformation("Application starting in {Mode} mode service={Service} configuration={ConfigurationPath}", mode, arguments.Service, arguments.ConfigurationPath);
    var browserOptions = configuration.GetSection("Browser").Get<BrowserOptions>() ?? new BrowserOptions();
    var usesExecutablePath = string.IsNullOrWhiteSpace(browserOptions.ExecutablePath) is false;
    logger.LogInformation("Browser configured channel={Channel} executable_path={ExecutablePath} selection={Selection}", browserOptions.Channel.Trim().ToLowerInvariant(), usesExecutablePath ? browserOptions.ExecutablePath : "automatic", usesExecutablePath ? "executable_path" : "channel");

    return mode switch
    {
        ApplicationMode.Download when arguments.Service => await DownloadServiceMode.RunAsync(configuration, cancellation.Token, logging),
        ApplicationMode.Download => await DownloadMode.RunAsync(configuration, cancellation.Token, logging, interactive),
        ApplicationMode.Proxy => await ProxyMode.RunAsync(configuration, cancellation.Token, logging, interactive, arguments.Service),
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
