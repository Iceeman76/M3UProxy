using Microsoft.AspNetCore.Http;

namespace DotNetLab.M3UProxyApp;

internal static class UpstreamRequestFactory
{
    public static Uri CreateUri(HttpRequest request, ProxySource source, string path)
    {
        if (path.Equals("/xmltv.php", StringComparison.OrdinalIgnoreCase) && source.Epg is not null)
        {
            return ReplaceQuery(new Uri(source.Epg.Url), request.QueryString.Value);
        }

        return new UriBuilder(source.UpstreamBaseUri)
        {
            Path = path,
            Query = request.QueryString.Value?.TrimStart('?') ?? string.Empty
        }.Uri;
    }

    public static IReadOnlyDictionary<string, string> CreateHeaders(HttpRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (request.Headers.TryGetValue("Range", out var range))
        {
            headers["Range"] = range.ToString();
        }

        return headers;
    }

    private static Uri ReplaceQuery(Uri uri, string? query) => new UriBuilder(uri) { Query = query?.TrimStart('?') ?? string.Empty }.Uri;
}
