using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Spectre.Console;

namespace M3UProxyApp;

internal static class ProxyRequestLogger
{
    private static readonly object FileSourceKey = new();

    public static void SetFileSource(HttpContext context, string filePath, bool refreshed)
    {
        TimeSpan? age = File.Exists(filePath) ? DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath) : null;
        context.Items[FileSourceKey] = new FileSource(refreshed ? "SERVER_REFRESH" : "CACHE", age);
    }

    public static async Task LogAsync(HttpContext context, RequestDelegate next, IReadOnlyList<ProxySource> sources, bool serveDownloadedFiles)
    {
        var source = ProxySourceResolver.Resolve(context.Request, sources);
        var description = ProxyRequestDescriptor.Describe(context.Request);
        var requiresUpstream = source is not null && ProxyRequestDescriptor.RequiresUpstream(context.Request, serveDownloadedFiles);
        var stopwatch = Stopwatch.StartNew();

        WriteStart(context.Request, description, source, requiresUpstream);

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            WriteCompletion(context, description, source, requiresUpstream, stopwatch.ElapsedMilliseconds);
        }
    }

    private static void WriteStart(HttpRequest request, string description, ProxySource? source, bool requiresUpstream)
    {
        var message = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] --> {GetDestination(requiresUpstream)} {request.Method} {description} source={source?.Name ?? "unknown"}";
        WriteColored(message, requiresUpstream);
    }

    private static void WriteCompletion(HttpContext context, string description, ProxySource? source, bool requiresUpstream, long elapsedMilliseconds)
    {
        var fileDetails = GetFileDetails(context);
        var message = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] <-- {GetDestination(requiresUpstream)} {context.Request.Method} {description} source={source?.Name ?? "unknown"}{fileDetails} status={context.Response.StatusCode} duration_ms={elapsedMilliseconds}";
        WriteColored(message, requiresUpstream);
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

    private static void WriteColored(string message, bool requiresUpstream)
    {
        var escapedMessage = Markup.Escape(message);
        AnsiConsole.MarkupLine(requiresUpstream ? $"[orange1]{escapedMessage}[/]" : $"[cyan]{escapedMessage}[/]");
    }

    private static string GetDestination(bool requiresUpstream) => requiresUpstream ? "UPSTREAM" : "LOCAL";

    private sealed record FileSource(string Source, TimeSpan? Age);
}
