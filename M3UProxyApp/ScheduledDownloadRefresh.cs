using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public sealed class ScheduledDownloadRefresh(Func<CancellationToken, Task> refresh, ServiceOptions options, ILogger<ScheduledDownloadRefresh> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.RefreshOnStartup)
        {
            await RefreshAsync(stoppingToken);
        }

        if (options.RefreshIntervalMinutes is 0)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(options.RefreshIntervalMinutes);
        logger.LogInformation("Scheduled download refresh enabled interval_minutes={IntervalMinutes}", options.RefreshIntervalMinutes);

        while (stoppingToken.IsCancellationRequested is false)
        {
            await Task.Delay(interval, stoppingToken);
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation("Scheduled download refresh starting");
            await refresh(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled download refresh failed; existing downloaded files are retained");
        }
    }
}
