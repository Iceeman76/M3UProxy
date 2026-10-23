using M3UProxy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace M3UProxyApp;

internal static class ProxyMode
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var downloads = configuration.GetSection("Downloads").Get<List<DownloadJob>>() ?? [];
        var options = configuration.GetSection("Proxy").Get<ProxyOptions>() ?? new ProxyOptions();
        var sources = ProxySourceFactory.Create(options, downloads);
        var publicBaseUri = ProxySourceFactory.ValidateBaseUri(options.PublicBaseUrl, "Proxy:PublicBaseUrl");
        ProxySourceFactory.ValidateBaseUri(options.ListenUrl, "Proxy:ListenUrl");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(options.ListenUrl);
        await using var application = builder.Build();
        await using var chromeClient = new ChromeContentClient();
        var downloadRefresh = new DownloadRefreshCoordinator(downloads, TimeSpan.FromMinutes(options.DownloadMaxAgeMinutes));
        var requestHandler = new ProxyRequestHandler(sources, publicBaseUri, options.ServeDownloadedFiles, chromeClient, downloadRefresh);
        application.Use((context, next) => ProxyRequestLogger.LogAsync(context, next, sources, options.ServeDownloadedFiles));
        application.MapMethods("/{**path}", [HttpMethods.Get, HttpMethods.Head], requestHandler.HandleAsync);
        WriteStartupInformation(options, sources, publicBaseUri);
        await application.StartAsync(cancellationToken);
        await WaitForExitAsync(application, downloadRefresh, cancellationToken);

        return 0;
    }

    private static async Task WaitForExitAsync(WebApplication application, DownloadRefreshCoordinator downloadRefresh, CancellationToken cancellationToken)
    {
        var shutdownTask = application.WaitForShutdownAsync(cancellationToken);

        while (shutdownTask.IsCompleted is false)
        {
            var commandTask = ProxyKeyMonitor.WaitAsync(cancellationToken);
            var completedTask = await Task.WhenAny(shutdownTask, commandTask);

            if (ReferenceEquals(completedTask, shutdownTask))
            {
                break;
            }

            switch (await commandTask)
            {
                case ProxyCommand.Update:
                    AnsiConsole.MarkupLine("\n[yellow]Updating downloaded files...[/]");
                    await downloadRefresh.UpdateAsync(cancellationToken);
                    AnsiConsole.MarkupLine("\n[green]Proxy is ready.[/] Press [yellow]U[/] to update files. Press [yellow]Q[/] to exit the application.");
                    break;
                case ProxyCommand.Quit:
                    AnsiConsole.MarkupLine("\n[yellow]Stopping proxy...[/]");
                    await application.StopAsync(CancellationToken.None);
                    break;
            }
        }

        await shutdownTask;
    }

    private static void WriteStartupInformation(ProxyOptions options, IEnumerable<ProxySource> sources, Uri publicBaseUri)
    {
        AnsiConsole.MarkupLine($"Proxy listening on [cyan]{Markup.Escape(options.ListenUrl)}[/]. Press [yellow]U[/] to update files. Press [yellow]Q[/] or [grey]Ctrl+C[/] to exit the application.\n");
        AnsiConsole.MarkupLine("Log colors: [cyan]LOCAL[/] stays inside the proxy, [orange1]UPSTREAM[/] contacts the provider.\n");

        if (options.ServeDownloadedFiles)
        {
            AnsiConsole.MarkupLine($"Downloaded files update automatically on M3U and XMLTV requests when they are at least [cyan]{options.DownloadMaxAgeMinutes} minutes[/] old.\n");
            AnsiConsole.MarkupLine("File responses show [cyan]file_source=CACHE[/] or [orange1]file_source=SERVER_REFRESH[/] together with the file age.\n");
        }

        foreach (var source in sources)
        {
            var playlistUrl = new Uri(publicBaseUri, $"get.php?username={Uri.EscapeDataString(source.Username)}&password={Uri.EscapeDataString(source.Password)}&type=m3u_plus&output=ts");
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(source.Name)}[/]");
            AnsiConsole.MarkupLine($"  M3U: {Markup.Escape(PasswordRedactor.Redact(playlistUrl.ToString()))}");
            AnsiConsole.MarkupLine($"  Xtream server: {Markup.Escape(publicBaseUri.GetLeftPart(UriPartial.Authority))}");
            AnsiConsole.MarkupLine($"  Username: {Markup.Escape(source.Username)}");
            AnsiConsole.MarkupLine("  Password: [grey]<same as upstream playlist>[/]\n");
        }
    }
}
