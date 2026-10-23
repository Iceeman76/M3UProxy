using System.Text;
using M3UProxy;
using Microsoft.AspNetCore.Http;

namespace M3UProxyApp;

internal sealed class ProxyRequestHandler(IReadOnlyList<ProxySource> sources, Uri publicBaseUri, bool serveDownloadedFiles, ChromeContentClient chromeClient, DownloadRefreshCoordinator downloadRefresh)
{
    private static readonly string[] ForwardedResponseHeaders = ["accept-ranges", "cache-control", "content-disposition", "content-range", "content-type", "etag", "last-modified"];

    public async Task HandleAsync(HttpContext context)
    {
        var source = ProxySourceResolver.Resolve(context.Request, sources);

        if (source is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unknown username or password.", context.RequestAborted);

            return;
        }

        try
        {
            await RouteAsync(context, source);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await WriteErrorAsync(context, exception);
        }
    }

    private async Task RouteAsync(HttpContext context, ProxySource source)
    {
        var path = context.Request.Path.Value ?? "/";
        var upstreamUri = UpstreamRequestFactory.CreateUri(context.Request, source, path);

        if (path.Equals("/get.php", StringComparison.OrdinalIgnoreCase))
        {
            await WritePlaylistAsync(context, source, upstreamUri);

            return;
        }

        if (path.Equals("/xmltv.php", StringComparison.OrdinalIgnoreCase) && serveDownloadedFiles)
        {
            if (HttpMethods.IsGet(context.Request.Method) && source.Epg is not null)
            {
                var refreshed = await downloadRefresh.UpdateIfStaleAsync(context.RequestAborted);
                ProxyRequestLogger.SetFileSource(context, source.Epg.FilePath, refreshed);
            }

            await LocalContentResponseWriter.WriteEpgAsync(context, source);

            return;
        }

        if (path.Equals("/player_api.php", StringComparison.OrdinalIgnoreCase))
        {
            var rewriteLogin = string.IsNullOrWhiteSpace(context.Request.Query["action"]);
            await ProxyTextAsync(context, upstreamUri, source.UpstreamBaseUri, false, rewriteLogin);

            return;
        }

        await ProxyStreamAsync(context, upstreamUri);
    }

    private async Task WritePlaylistAsync(HttpContext context, ProxySource source, Uri upstreamUri)
    {
        if (serveDownloadedFiles)
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                var refreshed = await downloadRefresh.UpdateIfStaleAsync(context.RequestAborted);
                ProxyRequestLogger.SetFileSource(context, source.Playlist.FilePath, refreshed);
            }

            await LocalContentResponseWriter.WritePlaylistAsync(context, source, publicBaseUri);

            return;
        }

        await ProxyTextAsync(context, upstreamUri, source.UpstreamBaseUri, true, false);
    }

    private async Task ProxyTextAsync(HttpContext context, Uri upstreamUri, Uri upstreamBaseUri, bool rewriteM3U, bool rewriteLogin)
    {
        await using var upstreamResponse = await chromeClient.OpenAsync(upstreamUri.ToString(), UpstreamRequestFactory.CreateHeaders(context.Request), context.RequestAborted);
        var bytes = await upstreamResponse.ReadAllBytesAsync(context.RequestAborted);
        var content = M3UContentRewriter.Rewrite(Encoding.UTF8.GetString(bytes), upstreamBaseUri, publicBaseUri);

        if (rewriteLogin && upstreamResponse.StatusCode is >= 200 and < 300)
        {
            content = XtreamResponseRewriter.RewriteLogin(content, publicBaseUri);
        }

        context.Response.StatusCode = upstreamResponse.StatusCode;
        context.Response.ContentType = rewriteM3U ? "application/x-mpegURL; charset=utf-8" : GetContentType(upstreamResponse);

        if (HttpMethods.IsHead(context.Request.Method) is false)
        {
            await context.Response.WriteAsync(content, Encoding.UTF8, context.RequestAborted);
        }
    }

    private async Task ProxyStreamAsync(HttpContext context, Uri upstreamUri)
    {
        await using var upstreamResponse = await chromeClient.OpenAsync(upstreamUri.ToString(), UpstreamRequestFactory.CreateHeaders(context.Request), context.RequestAborted);
        context.Response.StatusCode = upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, context.Response);

        if (HttpMethods.IsHead(context.Request.Method) is false)
        {
            await upstreamResponse.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
    }

    private static void CopyResponseHeaders(ChromeContentResponse upstreamResponse, HttpResponse response)
    {
        foreach (var headerName in ForwardedResponseHeaders)
        {
            if (upstreamResponse.Headers.TryGetValue(headerName, out var value))
            {
                response.Headers[headerName] = value;
            }
        }

        if (upstreamResponse.Headers.TryGetValue("content-length", out var contentLength) && long.TryParse(contentLength, out var length))
        {
            response.ContentLength = length;
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, Exception exception)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();

            return;
        }

        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsync($"Upstream request failed: {exception.Message}", context.RequestAborted);
    }

    private static string GetContentType(ChromeContentResponse response) => response.Headers.TryGetValue("content-type", out var contentType) ? contentType : "application/json; charset=utf-8";
}
