using Microsoft.Extensions.Configuration;
using M3UProxyApp;
using Spectre.Console;

IConfigurationRoot configuration;

try
{
    configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false, reloadOnChange: false).Build();
}
catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or FormatException)
{
    AnsiConsole.MarkupLine($"[red]Could not load configuration:[/] {Markup.Escape(exception.Message)}");

    return 1;
}

AnsiConsole.Write(new FigletText("M3U Proxy").Color(Color.Cyan1));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return ApplicationModeSelector.Select(args) switch
    {
        ApplicationMode.Download => await DownloadMode.RunAsync(configuration, cancellation.Token),
        ApplicationMode.Proxy => await ProxyMode.RunAsync(configuration, cancellation.Token),
        _ => 1
    };
}
catch (OperationCanceledException)
{
    AnsiConsole.MarkupLine("\n[yellow]Stopped.[/]");

    return 0;
}
catch (Exception exception)
{
    AnsiConsole.MarkupLine($"[red]{Markup.Escape(exception.Message)}[/]");

    return 1;
}
