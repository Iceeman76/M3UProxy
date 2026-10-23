namespace M3UProxyApp;

internal sealed record DownloadJob
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
}

internal sealed record ProxyOptions
{
    public string ListenUrl { get; init; } = "http://127.0.0.1:5050";
    public string PublicBaseUrl { get; init; } = "http://127.0.0.1:5050";
    public bool ServeDownloadedFiles { get; init; } = true;
    public int DownloadMaxAgeMinutes { get; init; } = 60;
    public List<ProxySourceOptions> Sources { get; init; } = [];
}

internal sealed record ProxySourceOptions
{
    public string Name { get; init; } = string.Empty;
    public string PlaylistDownload { get; init; } = string.Empty;
    public string? EpgDownload { get; init; }
}

internal sealed record ProxySource(string Name, DownloadJob Playlist, DownloadJob? Epg, Uri UpstreamBaseUri, string Username, string Password);
