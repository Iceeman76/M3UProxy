using DotNetLab.M3UProxyApp;

namespace DotNetLab.M3UProxy.Tests;

internal static class ProxyTestFactory
{
    public static DownloadJob CreatePlaylist(string name = "Playlist", string username = "user", string password = "password", string filePath = "playlist.m3u", string host = "provider.example:8080") => new()
    {
        Name = name,
        Url = $"http://{host}/get.php?username={username}&password={password}&type=m3u_plus&output=ts",
        FilePath = filePath
    };

    public static DownloadJob CreateEpg(string name = "EPG", string username = "user", string password = "password", string filePath = "epg.xmltv", string host = "provider.example:8080") => new()
    {
        Name = name,
        Url = $"http://{host}/xmltv.php?username={username}&password={password}",
        FilePath = filePath
    };

    public static ProxySource CreateSource(string username = "user", string password = "password", string playlistPath = "playlist.m3u", string? epgPath = "epg.xmltv")
    {
        var playlist = CreatePlaylist(username: username, password: password, filePath: playlistPath);
        var epg = epgPath is null ? null : CreateEpg(username: username, password: password, filePath: epgPath);

        return new ProxySource("Example", playlist, epg, new Uri("http://provider.example:8080"), username, password);
    }
}
