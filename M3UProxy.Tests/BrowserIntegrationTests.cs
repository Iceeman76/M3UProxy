using System.Net;
using System.Net.Sockets;
using System.Text;
using DotNetLab.M3UProxy;
using DotNetLab.M3UProxyApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

[Category("BrowserIntegration")]
[NonParallelizable]
public sealed class BrowserIntegrationTests
{
    private static readonly byte[] BinaryContent = Enumerable.Range(0, 200_000).Select(index => (byte)(index % 256)).ToArray();
    private WebApplication application = null!;
    private ChromeContentClient client = null!;
    private string baseUrl = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        if (Environment.GetEnvironmentVariable("M3UPROXY_RUN_BROWSER_TESTS") is not "1")
        {
            Assert.Ignore("Set M3UPROXY_RUN_BROWSER_TESTS=1 to run tests that require an installed Chrome browser.");
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        application = builder.Build();
        application.MapGet("/binary", async context =>
        {
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = BinaryContent.Length;
            await context.Response.Body.WriteAsync(BinaryContent, context.RequestAborted);
        });
        application.MapGet("/redirect", context =>
        {
            context.Response.Redirect("/binary");

            return Task.CompletedTask;
        });
        application.MapGet("/range", async context =>
        {
            if (context.Request.Headers.Range.ToString() is not "bytes=10-19")
            {
                context.Response.StatusCode = 400;

                return;
            }

            context.Response.StatusCode = 206;
            context.Response.Headers.ContentRange = "bytes 10-19/200000";
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.ContentLength = 10;
            await context.Response.Body.WriteAsync(BinaryContent.AsMemory(10, 10), context.RequestAborted);
        });
        application.MapGet("/headers", context => context.Response.WriteAsync($"{context.Request.Headers.AcceptLanguage}|{context.Request.Headers["X-Test"]}"));
        application.MapGet("/set-cookie", context =>
        {
            context.Response.Cookies.Append("test-cookie", "private-context");

            return context.Response.WriteAsync("Cookie set");
        });
        application.MapGet("/cookies", context => context.Response.WriteAsync(context.Request.Headers.Cookie.ToString()));
        application.MapGet("/slow", async context =>
        {
            context.Response.ContentType = "application/octet-stream";
            await context.Response.Body.WriteAsync(BinaryContent.AsMemory(0, 65536), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        application.MapGet("/slow-headers", context => Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted));
        application.MapGet("/unavailable", async context =>
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Temporarily unavailable");
        });
        application.MapGet("/get.php", context => context.Response.WriteAsync("#EXTM3U\n#EXTINF:-1,Example\n" + baseUrl + "/movie/user/password/1.ts\n"));
        application.MapGet("/xmltv.php", context => context.Response.WriteAsync("<tv></tv>"));
        application.MapGet("/movie/user/password/1.ts", async context =>
        {
            context.Response.ContentType = "video/mp2t";

            if (context.Request.Headers.Range.ToString() is "bytes=10-19")
            {
                context.Response.StatusCode = 206;
                context.Response.Headers.ContentRange = "bytes 10-19/200000";
                context.Response.Headers.AcceptRanges = "bytes";
                context.Response.ContentLength = 10;
                await context.Response.Body.WriteAsync(BinaryContent.AsMemory(10, 10), context.RequestAborted);

                return;
            }

            context.Response.ContentLength = BinaryContent.Length;
            await context.Response.Body.WriteAsync(BinaryContent, context.RequestAborted);
        });
        await application.StartAsync();
        baseUrl = application.Urls.Single();
        client = new ChromeContentClient();
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        if (application is not null)
        {
            await application.DisposeAsync();
        }
    }

    [Test]
    public async Task OpenAsync_StreamsBinaryContentAcrossMultipleChunks()
    {
        await using var response = await client.OpenAsync(baseUrl + "/binary");
        var content = await response.ReadAllBytesAsync();
        Assert.That(response.StatusCode, Is.EqualTo(200));
        Assert.That(response.Headers["content-length"], Is.EqualTo(BinaryContent.Length.ToString()));
        Assert.That(content, Is.EqualTo(BinaryContent));
    }

    [Test]
    public async Task OpenAsync_FollowsRedirectAndStreamsFinalResponse()
    {
        await using var response = await client.OpenAsync(baseUrl + "/redirect");
        Assert.That(response.StatusCode, Is.EqualTo(200));
        Assert.That(await response.ReadAllBytesAsync(), Is.EqualTo(BinaryContent));
    }

    [Test]
    public async Task OpenAsync_ForwardsRangeAndPreservesPartialResponseHeaders()
    {
        await using var response = await client.OpenAsync(baseUrl + "/range", new Dictionary<string, string> { ["Range"] = "bytes=10-19" });
        Assert.That(response.StatusCode, Is.EqualTo(206));
        Assert.That(response.Headers["content-range"], Is.EqualTo("bytes 10-19/200000"));
        Assert.That(await response.ReadAllBytesAsync(), Is.EqualTo(BinaryContent[10..20]));
    }

    [Test]
    public async Task OpenAsync_AppliesLocaleAndCustomHeaders()
    {
        await using var response = await client.OpenAsync(baseUrl + "/headers", new Dictionary<string, string> { ["X-Test"] = "forwarded" });
        Assert.That(Encoding.UTF8.GetString(await response.ReadAllBytesAsync()), Is.EqualTo("sv-SE|forwarded"));
    }

    [Test]
    public async Task OpenAsync_IsolatesCookiesBetweenRequests()
    {
        await using (var response = await client.OpenAsync(baseUrl + "/set-cookie"))
        {
            await response.ReadAllBytesAsync();
        }

        await using var nextResponse = await client.OpenAsync(baseUrl + "/cookies");
        Assert.That(await nextResponse.ReadAllBytesAsync(), Is.Empty);
    }

    [Test]
    public async Task ReadChunkAsync_CanCancelBlockedReadAndReuseBrowser()
    {
        await using (var response = await client.OpenAsync(baseUrl + "/slow"))
        {
            Assert.That(await response.ReadChunkAsync(), Has.Length.EqualTo(65536));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            Assert.That(async () => await response.ReadChunkAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        }

        await using var nextResponse = await client.OpenAsync(baseUrl + "/binary");
        Assert.That(await nextResponse.ReadAllBytesAsync(), Is.EqualTo(BinaryContent));
    }

    [Test]
    public async Task OpenAsync_PreservesErrorStatusAndBody()
    {
        await using var response = await client.OpenAsync(baseUrl + "/unavailable");
        Assert.That(response.StatusCode, Is.EqualTo(503));
        Assert.That(Encoding.UTF8.GetString(await response.ReadAllBytesAsync()), Is.EqualTo("Temporarily unavailable"));
    }

    [Test]
    public async Task OpenAsync_CancelsWhileWaitingForResponseHeaders()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        Assert.That(async () => await client.OpenAsync(baseUrl + "/slow-headers", cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
        await using var response = await client.OpenAsync(baseUrl + "/binary");
        Assert.That(await response.ReadAllBytesAsync(), Is.EqualTo(BinaryContent));
    }

    [Test]
    [Platform("Win")]
    public async Task EdgeChannel_StreamsUsingInstalledMicrosoftEdge()
    {
        await using var edgeClient = new ChromeContentClient(new BrowserOptions { Channel = "msedge" });
        await using var response = await edgeClient.OpenAsync(baseUrl + "/binary");
        Assert.That(await response.ReadAllBytesAsync(), Is.EqualTo(BinaryContent));
    }

    [Test]
    public async Task Downloader_UsesExplicitExecutableAndCompletesFile()
    {
        var executablePath = FindChromeExecutablePath();
        var targetPath = Path.Combine(Path.GetTempPath(), $"puppeteer-download-{Guid.NewGuid():N}.bin");

        try
        {
            var downloader = new WebContentDownloader(new BrowserOptions { Channel = "custom", ExecutablePath = executablePath });
            await downloader.DownloadAsync(baseUrl + "/binary", targetPath);
            Assert.That(await File.ReadAllBytesAsync(targetPath), Is.EqualTo(BinaryContent));
            Assert.That(File.Exists(targetPath + ".download"), Is.False);
        }
        finally
        {
            File.Delete(targetPath);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BrowserStartup_LogsSelectionAndResolvedExecutableOnce(bool useExecutablePath)
    {
        var executablePath = FindChromeExecutablePath();
        var logPath = Path.Combine(Path.GetTempPath(), $"browser-startup-{Guid.NewGuid():N}.log");
        var settings = new Dictionary<string, string?> { ["Logging:Console:Enabled"] = "false", ["Logging:File:Enabled"] = "true", ["Logging:File:Path"] = logPath };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = useExecutablePath ? new BrowserOptions { Channel = "custom", ExecutablePath = executablePath } : new BrowserOptions();

        try
        {
            using (var logging = ApplicationLogging.Create(configuration))
            {
                await using var loggedClient = new ChromeContentClient(options, logging.CreateLogger<ChromeContentClient>());

                for (var requestIndex = 0; requestIndex < 2; requestIndex++)
                {
                    await using var response = await loggedClient.OpenAsync(baseUrl + "/binary");
                    await response.ReadAllBytesAsync();
                }
            }

            var entries = await File.ReadAllLinesAsync(logPath);
            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0], Does.Contain("[Information]"));
            Assert.That(entries[0], Does.Contain($"Browser started channel={options.Channel}"));
            Assert.That(entries[0], Does.Contain($"executable_path={executablePath}"));
            Assert.That(entries[0], Does.Contain(useExecutablePath ? "selection=executable_path" : "selection=channel"));
        }
        finally
        {
            File.Delete(logPath);
        }
    }

    [Test]
    public async Task ProxyMode_UsesConfiguredExecutableForUpstreamStreamsAndAutomaticRefresh()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        var proxyUrl = $"http://127.0.0.1:{port}";
        var directory = Path.Combine(Path.GetTempPath(), $"puppeteer-proxy-{Guid.NewGuid():N}");
        var playlistPath = Path.Combine(directory, "playlist.m3u");
        var epgPath = Path.Combine(directory, "epg.xmltv");
        var settings = new Dictionary<string, string?>
        {
            ["Browser:Channel"] = "custom",
            ["Browser:ExecutablePath"] = FindChromeExecutablePath(),
            ["Downloads:0:Name"] = "Playlist",
            ["Downloads:0:Url"] = baseUrl + "/get.php?username=user&password=password",
            ["Downloads:0:FilePath"] = playlistPath,
            ["Downloads:1:Name"] = "EPG",
            ["Downloads:1:Url"] = baseUrl + "/xmltv.php?username=user&password=password",
            ["Downloads:1:FilePath"] = epgPath,
            ["Proxy:ListenUrl"] = proxyUrl,
            ["Proxy:PublicBaseUrl"] = proxyUrl,
            ["Proxy:Sources:0:Name"] = "Local provider",
            ["Proxy:Sources:0:PlaylistDownload"] = "Playlist",
            ["Proxy:Sources:0:EpgDownload"] = "EPG"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var logging = LoggerFactory.Create(builder => builder.ClearProviders());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var proxyTask = ProxyMode.RunAsync(configuration, cancellation.Token, logging);

        try
        {
            using var httpClient = new HttpClient();
            HttpResponseMessage? playlistResponse = null;

            while (playlistResponse is null)
            {
                try
                {
                    playlistResponse = await httpClient.GetAsync(proxyUrl + "/get.php?username=user&password=password", cancellation.Token);
                }
                catch (HttpRequestException) when (proxyTask.IsCompleted is false)
                {
                    await Task.Delay(20, cancellation.Token);
                }
            }

            using (playlistResponse)
            {
                Assert.That(playlistResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await playlistResponse.Content.ReadAsStringAsync(cancellation.Token), Does.Contain(proxyUrl + "/movie/user/password/1.ts"));
                Assert.That(await File.ReadAllTextAsync(epgPath, cancellation.Token), Is.EqualTo("<tv></tv>"));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, proxyUrl + "/movie/user/password/1.ts");
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(10, 19);
            using var streamResponse = await httpClient.SendAsync(request, cancellation.Token);
            Assert.That(streamResponse.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(streamResponse.Content.Headers.ContentRange!.ToString(), Is.EqualTo("bytes 10-19/200000"));
            Assert.That(await streamResponse.Content.ReadAsByteArrayAsync(cancellation.Token), Is.EqualTo(BinaryContent[10..20]));
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await proxyTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static string FindChromeExecutablePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable"
        };

        return candidates.First(File.Exists);
    }
}
