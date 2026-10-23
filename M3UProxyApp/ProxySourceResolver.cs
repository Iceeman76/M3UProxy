using Microsoft.AspNetCore.Http;

namespace M3UProxyApp;

internal static class ProxySourceResolver
{
    public static ProxySource? Resolve(HttpRequest request, IReadOnlyList<ProxySource> sources)
    {
        var username = request.Query["username"].FirstOrDefault();
        var password = request.Query["password"].FirstOrDefault();

        if (username is null || password is null)
        {
            (username, password) = GetPathCredentials(request.Path.Value ?? string.Empty);
        }

        return sources.FirstOrDefault(source => source.Username.Equals(username, StringComparison.Ordinal) && source.Password.Equals(password, StringComparison.Ordinal));
    }

    private static (string? Username, string? Password) GetPathCredentials(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var credentialIndex = segments.Length > 0 && segments[0].ToLowerInvariant() is "movie" or "series" or "live" or "timeshift" ? 1 : 0;

        if (segments.Length <= credentialIndex + 1)
        {
            return (null, null);
        }

        return (Uri.UnescapeDataString(segments[credentialIndex]), Uri.UnescapeDataString(segments[credentialIndex + 1]));
    }
}
