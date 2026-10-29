using System.Text;
using DotNetLab.M3UProxyApp;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ProxyPlaylistFileTests
{
    [Test]
    public async Task WritePlaylistAsync_StreamsUtf8PlaylistWithRewrittenUrls()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"m3u-proxy-{Guid.NewGuid():N}.m3u");
        var content = "#EXTM3U\n#EXTINF:-1,Ångström\nhttp://provider.example:8080/movie/user/password/123.mkv\n";
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false));

        try
        {
            var context = CreateContext(HttpMethods.Get);
            var source = ProxyTestFactory.CreateSource(playlistPath: filePath);

            await LocalContentResponseWriter.WritePlaylistAsync(context, source, new Uri("http://127.0.0.1:5050"));
            var result = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(context.Response.ContentType, Is.EqualTo("application/x-mpegURL; charset=utf-8"));
                Assert.That(result, Does.Contain("Ångström"));
                Assert.That(result, Does.Contain("http://127.0.0.1:5050/movie/user/password/123.mkv"));
                Assert.That(result, Does.Not.Contain("http://provider.example:8080/movie/"));
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task WritePlaylistAsync_ReturnsHeadersWithoutBodyForHead()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"m3u-proxy-{Guid.NewGuid():N}.m3u");
        await File.WriteAllTextAsync(filePath, "#EXTM3U\n");

        try
        {
            var context = CreateContext(HttpMethods.Head);

            await LocalContentResponseWriter.WritePlaylistAsync(context, ProxyTestFactory.CreateSource(playlistPath: filePath), new Uri("http://127.0.0.1:5050"));

            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(context.Response.ContentType, Is.EqualTo("application/x-mpegURL; charset=utf-8"));
                Assert.That(context.Response.Body.Length, Is.Zero);
            });
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public void WritePlaylistAsync_RejectsMissingFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"missing-m3u-proxy-{Guid.NewGuid():N}.m3u");
        var context = CreateContext(HttpMethods.Get);

        Assert.That(() => LocalContentResponseWriter.WritePlaylistAsync(context, ProxyTestFactory.CreateSource(playlistPath: filePath), new Uri("http://127.0.0.1:5050")), Throws.TypeOf<FileNotFoundException>().With.Message.Contains("Run download mode first"));
    }

    private static DefaultHttpContext CreateContext(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        return context;
    }
}
