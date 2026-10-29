using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class M3UContentRewriterTests
{
    [Test]
    public void Rewrite_ReplacesUpstreamStreamOrigin()
    {
        var content = "#EXTM3U\n#EXTINF:-1,Movie\nhttp://provider.example:8080/movie/user/password/123.mkv\n";

        var result = M3UContentRewriter.Rewrite(content, new Uri("http://provider.example:8080"), new Uri("http://127.0.0.1:5050"));

        Assert.That(result, Does.Contain("http://127.0.0.1:5050/movie/user/password/123.mkv"));
    }

    [Test]
    public void Rewrite_MatchesOriginCaseInsensitively()
    {
        var content = "HTTP://PROVIDER.EXAMPLE:8080/series/user/password/456.mkv";

        var result = M3UContentRewriter.Rewrite(content, new Uri("http://provider.example:8080"), new Uri("http://127.0.0.1:5050"));

        Assert.That(result, Is.EqualTo("http://127.0.0.1:5050/series/user/password/456.mkv"));
    }

    [Test]
    public void Rewrite_PreservesExternalArtworkOrigin()
    {
        var content = "#EXTINF:-1 tvg-logo=\"https://image.example/poster.jpg\",Movie";

        var result = M3UContentRewriter.Rewrite(content, new Uri("http://provider.example:8080"), new Uri("http://127.0.0.1:5050"));

        Assert.That(result, Is.EqualTo(content));
    }

    [Test]
    public void Rewrite_PreservesUnicodeMetadata()
    {
        var content = "#EXTINF:-1,Ångström – Pokémon\nhttp://provider.example:8080/movie/user/password/123.mkv";

        var result = M3UContentRewriter.Rewrite(content, new Uri("http://provider.example:8080"), new Uri("http://127.0.0.1:5050"));

        Assert.That(result, Does.StartWith("#EXTINF:-1,Ångström – Pokémon"));
    }

    [Test]
    public void Rewrite_ReplacesEveryProviderOrigin()
    {
        var content = "http://provider.example:8080/movie/1\nhttp://provider.example:8080/series/2";

        var result = M3UContentRewriter.Rewrite(content, new Uri("http://provider.example:8080"), new Uri("http://127.0.0.1:5050"));

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("provider.example"));
            Assert.That(result.Split("127.0.0.1", StringSplitOptions.None), Has.Length.EqualTo(3));
        });
    }
}
