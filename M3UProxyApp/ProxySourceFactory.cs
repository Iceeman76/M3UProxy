using Microsoft.AspNetCore.WebUtilities;

namespace M3UProxyApp;

internal static class ProxySourceFactory
{
    public static IReadOnlyList<ProxySource> Create(ProxyOptions options, IReadOnlyCollection<DownloadJob> downloads)
    {
        if (options.Sources.Count is 0)
        {
            throw new InvalidOperationException("No proxy sources are configured in appsettings.json.");
        }

        var downloadsByName = downloads.ToDictionary(download => download.Name, StringComparer.OrdinalIgnoreCase);
        var sources = options.Sources.Select(source => CreateSource(source, downloadsByName)).ToArray();
        var duplicateCredentials = sources.GroupBy(source => $"{source.Username}\0{source.Password}", StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);

        if (duplicateCredentials is not null)
        {
            throw new InvalidOperationException("Proxy sources must use unique upstream username and password combinations.");
        }

        return sources;
    }

    public static Uri ValidateBaseUri(string value, string settingName)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) is false || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute HTTP URL.");
        }

        if (uri.AbsolutePath is not "/")
        {
            throw new InvalidOperationException($"{settingName} cannot contain a path.");
        }

        return uri;
    }

    private static ProxySource CreateSource(ProxySourceOptions options, IReadOnlyDictionary<string, DownloadJob> downloads)
    {
        if (string.IsNullOrWhiteSpace(options.Name) || string.IsNullOrWhiteSpace(options.PlaylistDownload))
        {
            throw new InvalidOperationException("Every proxy source must contain name and playlistDownload.");
        }

        if (downloads.TryGetValue(options.PlaylistDownload, out var playlist) is false)
        {
            throw new InvalidOperationException($"Proxy source '{options.Name}' references unknown download '{options.PlaylistDownload}'.");
        }

        var playlistUri = new Uri(playlist.Url, UriKind.Absolute);
        var credentials = QueryHelpers.ParseQuery(playlistUri.Query);

        if (credentials.TryGetValue("username", out var username) is false || credentials.TryGetValue("password", out var password) is false)
        {
            throw new InvalidOperationException($"Playlist download '{playlist.Name}' must contain username and password query parameters.");
        }

        var epg = GetEpg(options, downloads);

        return new ProxySource(options.Name, playlist, epg, new Uri(playlistUri.GetLeftPart(UriPartial.Authority)), username.ToString(), password.ToString());
    }

    private static DownloadJob? GetEpg(ProxySourceOptions options, IReadOnlyDictionary<string, DownloadJob> downloads)
    {
        if (string.IsNullOrWhiteSpace(options.EpgDownload))
        {
            return null;
        }

        if (downloads.TryGetValue(options.EpgDownload, out var epg) is false)
        {
            throw new InvalidOperationException($"Proxy source '{options.Name}' references unknown download '{options.EpgDownload}'.");
        }

        return epg;
    }
}
