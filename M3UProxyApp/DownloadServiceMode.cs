using DotNetLab.M3UProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public static class DownloadServiceMode
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken, ILoggerFactory loggerFactory)
    {
        var options = configuration.GetSection("Service").Get<ServiceOptions>() ?? new ServiceOptions();
        options.Validate(ApplicationMode.Download);
        var downloads = configuration.GetSection("Downloads").Get<List<DownloadJob>>() ?? [];
        DownloadMode.Validate(downloads);
        var browserOptions = configuration.GetSection("Browser").Get<BrowserOptions>() ?? new BrowserOptions();
        browserOptions.CreateLaunchOptions();
        var refresh = new DownloadRefreshCoordinator(downloads, TimeSpan.Zero, loggerFactory, browserOptions, interactive: false);
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddConfiguration(configuration);
        ServiceHosting.Configure(builder.Services, loggerFactory);
        builder.Services.AddHostedService(_ => new ScheduledDownloadRefresh(refresh.UpdateAsync, options, loggerFactory.CreateLogger<ScheduledDownloadRefresh>()));
        using var host = builder.Build();
        await host.RunAsync(cancellationToken);

        return 0;
    }
}
