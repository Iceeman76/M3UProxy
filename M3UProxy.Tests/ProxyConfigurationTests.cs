using DotNetLab.M3UProxyApp;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ProxyConfigurationTests
{
    [Test]
    public void Create_RejectsEmptySourceList()
    {
        var options = new ProxyOptions();

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, []), "No proxy sources");
    }

    [Test]
    public void Create_RejectsMissingSourceName()
    {
        var options = new ProxyOptions { Sources = [new ProxySourceOptions { PlaylistDownload = "Playlist" }] };

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, [ProxyTestFactory.CreatePlaylist()]), "name and playlistDownload");
    }

    [Test]
    public void Create_RejectsUnknownPlaylistDownload()
    {
        var options = CreateOptions("Unknown");

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, [ProxyTestFactory.CreatePlaylist()]), "unknown download 'Unknown'");
    }

    [Test]
    public void Create_RejectsPlaylistWithoutCredentials()
    {
        var options = CreateOptions("Playlist");
        var playlist = new DownloadJob { Name = "Playlist", Url = "http://provider.example/get.php?type=m3u_plus", FilePath = "playlist.m3u" };

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, [playlist]), "username and password");
    }

    [Test]
    public void Create_RejectsUnknownEpgDownload()
    {
        var options = new ProxyOptions { Sources = [new ProxySourceOptions { Name = "Example", PlaylistDownload = "Playlist", EpgDownload = "Unknown EPG" }] };

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, [ProxyTestFactory.CreatePlaylist()]), "unknown download 'Unknown EPG'");
    }

    [Test]
    public void Create_RejectsDuplicateCredentials()
    {
        var options = new ProxyOptions
        {
            Sources =
            [
                new ProxySourceOptions { Name = "First", PlaylistDownload = "First playlist" },
                new ProxySourceOptions { Name = "Second", PlaylistDownload = "Second playlist" }
            ]
        };
        var downloads = new[]
        {
            ProxyTestFactory.CreatePlaylist("First playlist", host: "first.example"),
            ProxyTestFactory.CreatePlaylist("Second playlist", host: "second.example")
        };

        AssertInvalidOperation(() => ProxySourceFactory.Create(options, downloads), "unique upstream username and password");
    }

    [Test]
    public void Create_BindsPlaylistEpgAndCredentials()
    {
        var options = new ProxyOptions { Sources = [new ProxySourceOptions { Name = "Example", PlaylistDownload = "Playlist", EpgDownload = "EPG" }] };
        var playlist = ProxyTestFactory.CreatePlaylist();
        var epg = ProxyTestFactory.CreateEpg();

        var source = ProxySourceFactory.Create(options, [playlist, epg]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(source.Name, Is.EqualTo("Example"));
            Assert.That(source.Playlist, Is.SameAs(playlist));
            Assert.That(source.Epg, Is.SameAs(epg));
            Assert.That(source.UpstreamBaseUri, Is.EqualTo(new Uri("http://provider.example:8080")));
            Assert.That(source.Username, Is.EqualTo("user"));
            Assert.That(source.Password, Is.EqualTo("password"));
        });
    }

    [Test]
    public void ValidateBaseUri_AcceptsHttpRootAddress()
    {
        var result = ProxySourceFactory.ValidateBaseUri("http://127.0.0.1:5050", "setting");

        Assert.That(result, Is.EqualTo(new Uri("http://127.0.0.1:5050")));
    }

    [TestCase("ftp://127.0.0.1:5050")]
    [TestCase("not-a-url")]
    public void ValidateBaseUri_RejectsInvalidAddress(string value)
    {
        Assert.That(() => ProxySourceFactory.ValidateBaseUri(value, "setting"), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ValidateBaseUri_RejectsPath()
    {
        AssertInvalidOperation(() => ProxySourceFactory.ValidateBaseUri("http://127.0.0.1:5050/proxy", "setting"), "cannot contain a path");
    }

    private static ProxyOptions CreateOptions(string playlistDownload) => new() { Sources = [new ProxySourceOptions { Name = "Example", PlaylistDownload = playlistDownload }] };

    private static void AssertInvalidOperation(Action action, string expectedMessage) => Assert.That(action, Throws.TypeOf<InvalidOperationException>().With.Message.Contains(expectedMessage));
}
