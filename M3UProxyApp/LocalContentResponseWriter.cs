using System.Text;
using M3UProxy;
using Microsoft.AspNetCore.Http;

namespace M3UProxyApp;

internal static class LocalContentResponseWriter
{
    public static async Task WritePlaylistAsync(HttpContext context, ProxySource source, Uri publicBaseUri)
    {
        if (File.Exists(source.Playlist.FilePath) is false)
        {
            throw new FileNotFoundException($"Downloaded playlist '{source.Playlist.FilePath}' was not found. Run download mode first.");
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/x-mpegURL; charset=utf-8";

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await using var file = new FileStream(source.Playlist.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var reader = new StreamReader(file, Encoding.UTF8, true, 81920, true);
        await using var writer = new StreamWriter(context.Response.Body, new UTF8Encoding(false), 81920, true);

        while (await reader.ReadLineAsync(context.RequestAborted) is { } line)
        {
            var rewrittenLine = M3UContentRewriter.Rewrite(line, source.UpstreamBaseUri, publicBaseUri);
            await writer.WriteLineAsync(rewrittenLine.AsMemory(), context.RequestAborted);
        }

        await writer.FlushAsync(context.RequestAborted);
    }

    public static async Task WriteEpgAsync(HttpContext context, ProxySource source)
    {
        if (source.Epg is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;

            return;
        }

        if (File.Exists(source.Epg.FilePath) is false)
        {
            throw new FileNotFoundException($"Downloaded EPG '{source.Epg.FilePath}' was not found. Run download mode first.");
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.ContentLength = new FileInfo(source.Epg.FilePath).Length;

        if (HttpMethods.IsHead(context.Request.Method) is false)
        {
            await context.Response.SendFileAsync(source.Epg.FilePath, context.RequestAborted);
        }
    }
}
