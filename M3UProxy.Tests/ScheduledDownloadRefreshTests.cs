using DotNetLab.M3UProxyApp;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ScheduledDownloadRefreshTests
{
    [Test]
    public async Task StartupRefresh_RunsOnceWhenPeriodicRefreshIsDisabled()
    {
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new ServiceOptions { RefreshIntervalMinutes = 0 };
        using var worker = CreateWorker(_ =>
        {
            refreshed.TrySetResult();

            return Task.CompletedTask;
        }, options);
        await worker.StartAsync(CancellationToken.None);
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Test]
    public async Task DisabledStartupAndSchedule_PerformsNoRefresh()
    {
        var refreshCount = 0;
        var options = new ServiceOptions { RefreshIntervalMinutes = 0, RefreshOnStartup = false };
        using var worker = CreateWorker(_ =>
        {
            Interlocked.Increment(ref refreshCount);

            return Task.CompletedTask;
        }, options);
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        Assert.That(refreshCount, Is.Zero);
    }

    [Test]
    public async Task Schedule_ContinuesAfterFailureWithoutOverlappingRefreshes()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCount = 0;
        var activeCount = 0;
        var overlap = false;
        using var worker = CreateWorker(async token =>
        {
            if (Interlocked.Increment(ref activeCount) > 1)
            {
                overlap = true;
            }

            try
            {
                var count = Interlocked.Increment(ref refreshCount);
                await Task.Delay(30, token);

                if (count is 1)
                {
                    throw new IOException("Temporary provider failure");
                }

                completed.TrySetResult();
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }, new ServiceOptions { RefreshIntervalMinutes = 0.0005 });
        await worker.StartAsync(CancellationToken.None);

        try
        {
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.That(refreshCount, Is.GreaterThanOrEqualTo(2));
        Assert.That(overlap, Is.False);
    }

    [Test]
    public async Task Stop_CancelsActiveRefreshAndWaitsForCleanup()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanedUp = false;
        using var worker = CreateWorker(async token =>
        {
            started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            finally
            {
                cleanedUp = true;
            }
        }, new ServiceOptions());
        await worker.StartAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(cleanedUp, Is.True);
    }

    [Test]
    public async Task Stop_InterruptsLongIntervalWait()
    {
        using var worker = CreateWorker(_ => Task.CompletedTask, new ServiceOptions { RefreshOnStartup = false });
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(worker.ExecuteTask!.IsCompleted, Is.True);
    }

    private static ScheduledDownloadRefresh CreateWorker(Func<CancellationToken, Task> refresh, ServiceOptions options) => new(refresh, options, NullLogger<ScheduledDownloadRefresh>.Instance);
}
