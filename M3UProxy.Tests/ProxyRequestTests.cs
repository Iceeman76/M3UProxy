using M3UProxyApp;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace M3UProxy.Tests;

public sealed class ProxyRequestTests
{
    [Test]
    public void ResolveSource_UsesQueryCredentialsForApiRequest()
    {
        var context = CreateContext("/player_api.php", "?username=user&password=password&action=get_vod_categories");
        var source = ProxyTestFactory.CreateSource();

        var result = ProxySourceResolver.Resolve(context.Request, [source]);

        Assert.That(result, Is.SameAs(source));
    }

    [TestCase("/movie/user/password/123.mkv")]
    [TestCase("/series/user/password/456.mkv")]
    [TestCase("/live/user/password/789.ts")]
    [TestCase("/timeshift/user/password/60/2026-08-17:20-00/789.ts")]
    public void ResolveSource_UsesCredentialsFromTypedStreamPath(string path)
    {
        var context = CreateContext(path);
        var source = ProxyTestFactory.CreateSource();

        var result = ProxySourceResolver.Resolve(context.Request, [source]);

        Assert.That(result, Is.SameAs(source));
    }

    [Test]
    public void ResolveSource_UsesCredentialsFromLegacyStreamPath()
    {
        var context = CreateContext("/user/password/789.ts");
        var source = ProxyTestFactory.CreateSource();

        var result = ProxySourceResolver.Resolve(context.Request, [source]);

        Assert.That(result, Is.SameAs(source));
    }

    [Test]
    public void ResolveSource_ReturnsNullForUnknownCredentials()
    {
        var context = CreateContext("/movie/unknown/wrong/123.mkv");

        var result = ProxySourceResolver.Resolve(context.Request, [ProxyTestFactory.CreateSource()]);

        Assert.That(result, Is.Null);
    }

    [TestCase("/get.php", "", "M3U playlist")]
    [TestCase("/xmltv.php", "", "XMLTV EPG")]
    [TestCase("/player_api.php", "", "Xtream login")]
    [TestCase("/player_api.php", "?action=get_series_categories", "Xtream get_series_categories")]
    [TestCase("/movie/user/password/1.mkv", "", "movie stream")]
    [TestCase("/series/user/password/1.mkv", "", "series stream")]
    [TestCase("/live/user/password/1.ts", "", "live stream")]
    [TestCase("/user/password/1.ts", "", "media stream")]
    public void DescribeRequest_ReturnsCredentialSafeDescription(string path, string query, string expected)
    {
        var context = CreateContext(path, query);

        var result = ProxyRequestDescriptor.Describe(context.Request);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(result, Does.Not.Contain("user"));
            Assert.That(result, Does.Not.Contain("password"));
        });
    }

    [Test]
    public void CreateUpstreamUri_UsesProviderOriginPathAndQuery()
    {
        var context = CreateContext("/player_api.php", "?username=user&password=password&action=get_vod_streams");
        var source = ProxyTestFactory.CreateSource();

        var result = UpstreamRequestFactory.CreateUri(context.Request, source, "/player_api.php");

        Assert.That(result.ToString(), Is.EqualTo("http://provider.example:8080/player_api.php?username=user&password=password&action=get_vod_streams"));
    }

    [Test]
    public void CreateUpstreamUri_UsesConfiguredEpgEndpoint()
    {
        var epg = ProxyTestFactory.CreateEpg(host: "epg.provider.example:9090");
        var playlist = ProxyTestFactory.CreatePlaylist();
        var source = new ProxySource("Example", playlist, epg, new Uri("http://provider.example:8080"), "user", "password");
        var context = CreateContext("/xmltv.php", "?username=user&password=password");

        var result = UpstreamRequestFactory.CreateUri(context.Request, source, "/xmltv.php");

        Assert.That(result.ToString(), Is.EqualTo("http://epg.provider.example:9090/xmltv.php?username=user&password=password"));
    }

    [Test]
    public void DescribeRequest_DoesNotLogUnexpectedActionContent()
    {
        var context = CreateContext("/player_api.php", "?action=get_vod_categories%0Apassword%3Dsecret");

        var result = ProxyRequestDescriptor.Describe(context.Request);

        Assert.That(result, Is.EqualTo("Xtream request"));
    }

    [TestCase("/get.php", true, false)]
    [TestCase("/xmltv.php", true, false)]
    [TestCase("/get.php", false, true)]
    [TestCase("/xmltv.php", false, true)]
    [TestCase("/player_api.php", true, true)]
    [TestCase("/movie/user/password/1.mkv", true, true)]
    [TestCase("/series/user/password/1.mkv", true, true)]
    [TestCase("/live/user/password/1.ts", true, true)]
    public void RequiresUpstream_ClassifiesRequestDestination(string path, bool serveDownloadedFiles, bool expected)
    {
        var context = CreateContext(path);

        var result = ProxyRequestDescriptor.RequiresUpstream(context.Request, serveDownloadedFiles);

        Assert.That(result, Is.EqualTo(expected));
    }

    private static DefaultHttpContext CreateContext(string path, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        return context;
    }
}
