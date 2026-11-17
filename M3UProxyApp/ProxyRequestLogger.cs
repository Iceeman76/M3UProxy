using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

internal static class ProxyRequestLogger
{
    private static readonly object FileSourceKey = new();

    public static void SetFileSource(HttpContext context, string filePath, bool refreshed)
    {
        TimeSpan? age = File.Exists(filePath) ? DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath) : null;
        context.Items[FileSourceKey] = new FileSource(refreshed ? "SERVER_REFRESH" : "CACHE", age);
    }

    public static async Task LogAsync(HttpContext context, RequestDelegate next, IReadOnlyList<ProxySource> sources, bool serveDownloadedFiles, ILogger logger)
    {
        var source = ProxySourceResolver.Resolve(context.Request, sources);
        var description = ProxyRequestDescriptor.Describe(context.Request);
        var requiresUpstream = source is not null && ProxyRequestDescriptor.RequiresUpstream(context.Request, serveDownloadedFiles);
        var stopwatch = Stopwatch.StartNew();

        var failed = false;
        logger.LogDebug("--> {Destination} {Method} {Description} source={Source}", GetDestination(requiresUpstream), context.Request.Method, description, source?.Name ?? "unknown");

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            failed = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            WriteCompletion(context, description, source, requiresUpstream, stopwatch.ElapsedMilliseconds, failed, logger);
        }
    }

    private static void WriteCompletion(HttpContext context, string description, ProxySource? source, bool requiresUpstream, long elapsedMilliseconds, bool failed, ILogger logger)
    {
        var fileDetails = GetFileDetails(context);
        var level = failed ? LogLevel.Error : context.Response.StatusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ when context.RequestAborted.IsCancellationRequested => LogLevel.Debug,
            _ => LogLevel.Information
        };
        var outcome = failed ? "failed" : context.RequestAborted.IsCancellationRequested ? "aborted" : "completed";
        logger.Log(level, "<-- {Destination} {Method} {Description} source={Source}{FileDetails} status={StatusCode} outcome={Outcome} duration_ms={ElapsedMilliseconds}", GetDestination(requiresUpstream), context.Request.Method, description, source?.Name ?? "unknown", fileDetails, context.Response.StatusCode, outcome, elapsedMilliseconds);
    }

    private static string GetFileDetails(HttpContext context)
    {
        if (context.Items.TryGetValue(FileSourceKey, out var value) is false || value is not FileSource fileSource)
        {
            return string.Empty;
        }

        var age = fileSource.Age is { } fileAge ? FormatAge(fileAge) : "missing";

        return $" file_source={fileSource.Source} file_age={age}";
    }

    internal static string FormatAge(TimeSpan age)
    {
        var normalizedAge = age < TimeSpan.Zero ? TimeSpan.Zero : age;

        if (normalizedAge.TotalDays >= 1)
        {
            return $"{(int)normalizedAge.TotalDays}d{normalizedAge.Hours}h";
        }

        if (normalizedAge.TotalHours >= 1)
        {
            return $"{(int)normalizedAge.TotalHours}h{normalizedAge.Minutes}m";
        }

        if (normalizedAge.TotalMinutes >= 1)
        {
            return $"{(int)normalizedAge.TotalMinutes}m{normalizedAge.Seconds}s";
        }

        return $"{(int)normalizedAge.TotalSeconds}s";
    }

    private static string GetDestination(bool requiresUpstream) => requiresUpstream ? "UPSTREAM" : "LOCAL";

    private sealed record FileSource(string Source, TimeSpan? Age);
}
