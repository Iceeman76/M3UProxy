using DotNetLab.M3UProxy;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public sealed class DownloadRefreshCoordinator(IReadOnlyCollection<DownloadJob> jobs, TimeSpan maxAge, ILoggerFactory? loggerFactory = null, BrowserOptions? browserOptions = null, bool interactive = true)
{
    private readonly SemaphoreSlim updateLock = new(1, 1);

    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        await updateLock.WaitAsync(cancellationToken);

        try
        {
            await DownloadMode.RunAsync(jobs, cancellationToken, loggerFactory, browserOptions, interactive);
        }
        finally
        {
            updateLock.Release();
        }
    }

    public async Task<bool> UpdateIfStaleAsync(CancellationToken cancellationToken)
    {
        if (NeedsUpdate(DateTime.UtcNow) is false)
        {
            return false;
        }

        await updateLock.WaitAsync(cancellationToken);

        try
        {
            if (NeedsUpdate(DateTime.UtcNow))
            {
                await DownloadMode.RunAsync(jobs, cancellationToken, loggerFactory, browserOptions, interactive);

                return true;
            }

            return false;
        }
        finally
        {
            updateLock.Release();
        }
    }

    public bool NeedsUpdate(DateTime utcNow) => jobs.Any(job => File.Exists(job.FilePath) is false || utcNow - File.GetLastWriteTimeUtc(job.FilePath) >= maxAge);
}
