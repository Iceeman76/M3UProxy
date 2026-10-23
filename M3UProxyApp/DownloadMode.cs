using System.Diagnostics;
using M3UProxy;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

namespace M3UProxyApp;

internal static class DownloadMode
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var jobs = configuration.GetSection("Downloads").Get<List<DownloadJob>>() ?? [];

        return await RunAsync(jobs, cancellationToken);
    }

    public static async Task<int> RunAsync(IReadOnlyCollection<DownloadJob> jobs, CancellationToken cancellationToken)
    {
        Validate(jobs);
        AnsiConsole.MarkupLine($"Starting [cyan]{jobs.Count}[/] parallel download(s). Press [grey]Ctrl+C[/] to cancel.\n");
        var totalStopwatch = Stopwatch.StartNew();
        var results = await RunDownloadsAsync(jobs, cancellationToken);
        totalStopwatch.Stop();
        var succeeded = results.Count(result => result.Success);
        var failed = results.Length - succeeded;
        WriteResults(results, totalStopwatch.Elapsed, succeeded, failed);

        return failed is 0 ? 0 : 1;
    }

    private static async Task<DownloadResult[]> RunDownloadsAsync(IEnumerable<DownloadJob> jobs, CancellationToken cancellationToken)
    {
        var results = Array.Empty<DownloadResult>();
        var progress = AnsiConsole.Progress().AutoClear(false).HideCompleted(false).Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(), new TransferSpeedColumn(), new RemainingTimeColumn());
        await progress.StartAsync(async context =>
        {
            var tasks = jobs.Select(job => RunDownloadAsync(job, CreateProgressTask(context, job), cancellationToken));
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

    private static async Task<DownloadResult> RunDownloadAsync(DownloadJob job, ProgressTask progressTask, CancellationToken cancellationToken)
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
            var downloader = new WebContentDownloader();
            await downloader.DownloadAsync(job.Url, job.FilePath, cancellationToken, progress);
            stopwatch.Stop();
            var bytes = new FileInfo(job.FilePath).Length;
            progressTask.MaxValue = Math.Max(bytes, 1);
            progressTask.IsIndeterminate = false;
            progressTask.Value = progressTask.MaxValue;

            return new DownloadResult(job, true, bytes, stopwatch.Elapsed, null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            progressTask.Description = $"{Markup.Escape(job.Name)} [red](failed)[/]";
            progressTask.StopTask();

            return new DownloadResult(job, false, null, stopwatch.Elapsed, exception.Message);
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
