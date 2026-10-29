using System.Diagnostics;
using Microsoft.Playwright;

namespace DotNetLab.M3UProxy;

public sealed class WebContentDownloader
{
    private const int MaxAttempts = 3;

    public async Task DownloadAsync(string url, string filePath, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var targetPath = Path.GetFullPath(filePath);
        var targetDirectory = Path.GetDirectoryName(targetPath) ?? throw new InvalidOperationException("The target path does not contain a directory.");
        Directory.CreateDirectory(targetDirectory);
        var temporaryPath = targetPath + ".download";
        await using var client = new ChromeContentClient();

        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await DownloadAsync(client, url, temporaryPath, cancellationToken, progress);
                    File.Move(temporaryPath, targetPath, overwrite: true);

                    return;
                }
                catch (Exception exception) when (attempt < MaxAttempts && exception is PlaywrightException or IOException or HttpRequestException)
                {
                    File.Delete(temporaryPath);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static async Task DownloadAsync(ChromeContentClient client, string url, string temporaryPath, CancellationToken cancellationToken, IProgress<DownloadProgress>? progress)
    {
        await using var response = await client.OpenAsync(url, cancellationToken: cancellationToken);

        if (response.StatusCode is < 200 or >= 300)
        {
            throw new HttpRequestException($"The server returned HTTP {response.StatusCode}.");
        }

        var totalBytes = GetContentLength(response);
        var bytesReceived = 0L;
        var stopwatch = Stopwatch.StartNew();
        var lastProgressReport = TimeSpan.Zero;
        await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        while (true)
        {
            var bytes = await response.ReadChunkAsync(cancellationToken);

            if (bytes.Length is 0)
            {
                break;
            }

            await destination.WriteAsync(bytes, cancellationToken);
            bytesReceived += bytes.Length;

            if (progress is not null && stopwatch.Elapsed - lastProgressReport >= TimeSpan.FromSeconds(1))
            {
                progress.Report(new DownloadProgress(bytesReceived, totalBytes, bytesReceived / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001)));
                lastProgressReport = stopwatch.Elapsed;
            }
        }

        progress?.Report(new DownloadProgress(bytesReceived, totalBytes, bytesReceived / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001)));
    }

    private static long? GetContentLength(ChromeContentResponse response) => response.Headers.TryGetValue("content-length", out var contentLength) && long.TryParse(contentLength, out var length) ? length : null;
}

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double BytesPerSecond);
