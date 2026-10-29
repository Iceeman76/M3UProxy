namespace DotNetLab.M3UProxyApp;

internal sealed class DownloadRefreshCoordinator(IReadOnlyCollection<DownloadJob> jobs, TimeSpan maxAge)
{
    private readonly SemaphoreSlim updateLock = new(1, 1);

    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        await updateLock.WaitAsync(cancellationToken);

        try
        {
            await DownloadMode.RunAsync(jobs, cancellationToken);
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
                await DownloadMode.RunAsync(jobs, cancellationToken);

                return true;
            }

            return false;
        }
        finally
        {
            updateLock.Release();
        }
    }

    internal bool NeedsUpdate(DateTime utcNow) => jobs.Any(job => File.Exists(job.FilePath) is false || utcNow - File.GetLastWriteTimeUtc(job.FilePath) >= maxAge);
}
