namespace DotNetLab.M3UProxyApp;

internal static class PasswordRedactor
{
    public static string Redact(string url)
    {
        const string marker = "password=";
        var start = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return url;
        }

        start += marker.Length;
        var end = url.IndexOf('&', start);

        return end < 0 ? $"{url[..start]}<same as upstream>" : $"{url[..start]}<same as upstream>{url[end..]}";
    }
}
