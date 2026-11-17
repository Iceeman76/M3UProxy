using System.Diagnostics;
using DotNetLab.M3UProxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;

namespace DotNetLab.M3UProxyApp;

internal static class DownloadMode
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken, ILoggerFactory loggerFactory)
    {
        var jobs = configuration.GetSection("Downloads").Get<List<DownloadJob>>() ?? [];
        var browserOptions = configuration.GetSection("Browser").Get<BrowserOptions>() ?? new BrowserOptions();

        return await RunAsync(jobs, cancellationToken, loggerFactory, browserOptions);
    }

    public static async Task<int> RunAsync(IReadOnlyCollection<DownloadJob> jobs, CancellationToken cancellationToken, ILoggerFactory? loggerFactory = null, BrowserOptions? browserOptions = null)
    {
        var logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("DotNetLab.M3UProxyApp.Download");
        Validate(jobs);
        logger.LogInformation("Download refresh starting jobs={JobCount}", jobs.Count);
        AnsiConsole.MarkupLine($"Starting [cyan]{jobs.Count}[/] parallel download(s). Press [grey]Ctrl+C[/] to cancel.\n");
        var totalStopwatch = Stopwatch.StartNew();
        var results = await RunDownloadsAsync(jobs, cancellationToken, logger, browserOptions);
        totalStopwatch.Stop();
        var succeeded = results.Count(result => result.Success);
        var failed = results.Length - succeeded;
        WriteResults(results, totalStopwatch.Elapsed, succeeded, failed);
        logger.LogInformation("Download refresh completed succeeded={Succeeded} failed={Failed} duration_ms={ElapsedMilliseconds}", succeeded, failed, totalStopwatch.ElapsedMilliseconds);

        return failed is 0 ? 0 : 1;
    }

    private static async Task<DownloadResult[]> RunDownloadsAsync(IEnumerable<DownloadJob> jobs, CancellationToken cancellationToken, ILogger logger, BrowserOptions? browserOptions)
    {
        var results = Array.Empty<DownloadResult>();
        var progress = AnsiConsole.Progress().AutoClear(false).HideCompleted(false).Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(), new TransferSpeedColumn(), new RemainingTimeColumn());
        await progress.StartAsync(async context =>
        {
            var tasks = jobs.Select(job => RunDownloadAsync(job, CreateProgressTask(context, job), cancellationToken, logger, browserOptions));
            results = await Task.WhenAll(tasks);
        });

        return results;
    }

    private static ProgressTask CreateProgressTask(ProgressContext context, DownloadJob job)
    {
        var progressTask = context.AddTask(Markup.Escape(job.Name), new ProgressTaskSettings { AutoStart = true, MaxValue = double.MaxValue });
        progressTask.IsIndeterminate = true;

        return progressTask;
    }

    private static void WriteResults(IEnumerable<DownloadResult> results, TimeSpan totalDuration, int succeeded, int failed)
    {
        AnsiConsole.WriteLine();
        var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey).AddColumn("Job").AddColumn("Status").AddColumn(new TableColumn("Size").RightAligned()).AddColumn(new TableColumn("Duration").RightAligned()).AddColumn("Details");

        foreach (var result in results)
        {
            table.AddRow(Markup.Escape(result.Job.Name), result.Success ? "[green]Completed[/]" : "[red]Failed[/]", result.Bytes is { } bytes ? Markup.Escape(FormatBytes(bytes)) : "-", Markup.Escape(FormatDuration(result.Duration)), Markup.Escape(result.Error ?? result.Job.FilePath));
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\nCompleted in [cyan]{Markup.Escape(FormatDuration(totalDuration))}[/]. [green]{succeeded} succeeded[/], [red]{failed} failed[/].");
    }

    private static void Validate(IReadOnlyCollection<DownloadJob> jobs)
    {
        if (jobs.Count is 0)
        {
            throw new InvalidOperationException("No downloads are configured in appsettings.json.");
        }

        if (jobs.Any(job => string.IsNullOrWhiteSpace(job.Name) || string.IsNullOrWhiteSpace(job.Url) || string.IsNullOrWhiteSpace(job.FilePath)))
        {
            throw new InvalidOperationException("Every download must contain name, url, and filePath.");
        }

        var duplicateTarget = jobs.GroupBy(job => Path.GetFullPath(job.FilePath), StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);

        if (duplicateTarget is not null)
        {
            throw new InvalidOperationException($"Multiple downloads use the same target file: {duplicateTarget.Key}");
        }
    }

    private static async Task<DownloadResult> RunDownloadAsync(DownloadJob job, ProgressTask progressTask, CancellationToken cancellationToken, ILogger logger, BrowserOptions? browserOptions)
    {
        var stopwatch = Stopwatch.StartNew();
        var progress = new InlineProgress<DownloadProgress>(value =>
        {
            if (value.TotalBytes is > 0)
            {
                progressTask.MaxValue = value.TotalBytes.Value;
                progressTask.IsIndeterminate = false;
            }

            progressTask.Value = value.BytesReceived;
        });

        try
        {
            var downloader = new WebContentDownloader(browserOptions, logger);
            await downloader.DownloadAsync(job.Url, job.FilePath, cancellationToken, progress);
            stopwatch.Stop();
            var bytes = new FileInfo(job.FilePath).Length;
            progressTask.MaxValue = Math.Max(bytes, 1);
            progressTask.IsIndeterminate = false;
            progressTask.Value = progressTask.MaxValue;
            logger.LogInformation("Download completed job={Job} bytes={Bytes} duration_ms={ElapsedMilliseconds}", job.Name, bytes, stopwatch.ElapsedMilliseconds);

            return new DownloadResult(job, true, bytes, stopwatch.Elapsed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Download cancelled job={Job}", job.Name);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Download failed job={Job}", job.Name);
            stopwatch.Stop();
            progressTask.Description = $"{Markup.Escape(job.Name)} [red](failed)[/]";
            progressTask.StopTask();

            return new DownloadResult(job, false, null, stopwatch.Elapsed, $"{exception.GetType().Name}; see logs for details");
        }
    }

    private static string FormatBytes(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");

    private sealed record DownloadResult(DownloadJob Job, bool Success, long? Bytes, TimeSpan Duration, string? Error);

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
