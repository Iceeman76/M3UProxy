using Microsoft.AspNetCore.Http;

namespace DotNetLab.M3UProxyApp;

internal static class ProxyRequestDescriptor
{
    public static string Describe(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;

        if (path.Equals("/get.php", StringComparison.OrdinalIgnoreCase))
        {
            return "M3U playlist";
        }

        if (path.Equals("/xmltv.php", StringComparison.OrdinalIgnoreCase))
        {
            return "XMLTV EPG";
        }

        if (path.Equals("/player_api.php", StringComparison.OrdinalIgnoreCase))
        {
            return DescribeXtreamRequest(request);
        }

        return DescribeMediaRequest(path);
    }

    public static bool RequiresUpstream(HttpRequest request, bool serveDownloadedFiles)
    {
        var path = request.Path.Value ?? string.Empty;

        if (path.Equals("/get.php", StringComparison.OrdinalIgnoreCase) || path.Equals("/xmltv.php", StringComparison.OrdinalIgnoreCase))
        {
            return serveDownloadedFiles is false;
        }

        return true;
    }

    private static string DescribeXtreamRequest(HttpRequest request)
    {
        var action = request.Query["action"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(action))
        {
            return "Xtream login";
        }

        return action.All(character => char.IsLetterOrDigit(character) || character is '_' or '-') ? $"Xtream {action}" : "Xtream request";
    }

    private static string DescribeMediaRequest(string path)
    {
        var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return firstSegment?.ToLowerInvariant() switch
        {
            "movie" => "movie stream",
            "series" => "series stream",
            "live" => "live stream",
            "timeshift" => "timeshift stream",
            _ => "media stream"
        };
    }
}
