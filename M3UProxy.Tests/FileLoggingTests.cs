using System.Net;
using System.Net.Sockets;
using DotNetLab.M3UProxyApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class FileLoggingTests
{
    private string directory = string.Empty;
    private string logPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"m3uproxy-logging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        logPath = Path.Combine(directory, "application.log");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public void Factory_AppliesIndependentProviderAndCategoryLevelsAndClosesFile()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Error",
            ["Logging:File:LogLevel:Default"] = "Debug",
            ["Logging:File:LogLevel:Microsoft"] = "None"
        });

        using (var factory = ApplicationLogging.Create(configuration))
        {
            factory.CreateLogger("Application").LogDebug("Visible debug message");
            factory.CreateLogger("Microsoft.Hosting").LogCritical("Hidden framework message");
        }

        using var file = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(file);
        var contents = reader.ReadToEnd();
        Assert.That(contents, Does.Contain("[Debug] Application Visible debug message"));
        Assert.That(contents, Does.Not.Contain("Hidden framework message"));
        Assert.That(DateTimeOffset.TryParse(contents.Split(' ')[0], out _), Is.True);
    }

    [Test]
    public void Factory_DisabledFileLoggingDoesNotCreateDirectory()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?> { ["Logging:File:Enabled"] = "false", ["Logging:File:Path"] = Path.Combine(directory, "missing", "log.txt") });
        using var factory = ApplicationLogging.Create(configuration);
        factory.CreateLogger("Application").LogError("No file");
        Assert.That(Directory.GetFiles(directory), Is.Empty);
        Assert.That(Directory.GetDirectories(directory), Is.Empty);
    }

    [Test]
    public void Factory_ReportsUnwritableLocationDuringStartup()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?> { ["Logging:File:Path"] = directory });
        Assert.That(() => ApplicationLogging.Create(configuration), Throws.Exception);
    }

    [Test]
    public void Logger_RedactsMessagesExceptionsCategoriesAndConfiguredSecrets()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?> { ["Downloads:0:Url"] = "https://provider.example/get.php?username=alice&password=hidden%2Bpassword&token=mytoken" });

        using (var factory = ApplicationLogging.Create(configuration))
        {
            factory.CreateLogger("alice").LogError(new IOException("Failed https://provider.example/movie/alice/hidden+password/1.ts?token=unexpected"), "Values {Password} {Encoded} {Token} {Header} {Json}", "hidden+password", "hidden%2Bpassword", "mytoken", "Bearer dynamic-secret", "{\"password\":\"json-secret\"}");
        }

        var contents = File.ReadAllText(logPath);

        foreach (var secret in new[] { "alice", "hidden+password", "hidden%2Bpassword", "mytoken", "unexpected", "dynamic-secret", "json-secret" })
        {
            Assert.That(contents, Does.Not.Contain(secret));
        }

        Assert.That(contents, Does.Contain("IOException"));
        Assert.That(contents, Does.Contain("[redacted]"));
    }

    [TestCase("/live/stranger/unknown-password/1.ts", "unknown-password")]
    [TestCase("/get.php?username=stranger&password=unknown-password&token=unknown-token", "unknown-token")]
    [TestCase("Authorization: Bearer unknown-token", "unknown-token")]
    [TestCase("api_key=unknown-token", "unknown-token")]
    [TestCase("{\"token\": \"unknown-token\"}", "unknown-token")]
    [TestCase("password=\"unknown token with spaces\"", "with spaces")]
    [TestCase("Cookie: session=first; another=unknown-token", "unknown-token")]
    [TestCase("client_secret=unknown-token", "unknown-token")]
    public void Redactor_RemovesUnconfiguredSecrets(string message, string secret)
    {
        var redactor = new LogRedactor(new ConfigurationBuilder().Build());
        Assert.That(redactor.Redact(message), Does.Not.Contain(secret));
    }

    [Test]
    public void Logger_EscapesLineBreaks()
    {
        using (var factory = ApplicationLogging.Create(CreateConfiguration()))
        {
            factory.CreateLogger("Application").LogInformation("First\r\nForged entry");
        }

        Assert.That(File.ReadAllLines(logPath), Has.Length.EqualTo(1));
        Assert.That(File.ReadAllText(logPath), Does.Contain("First\\r\\nForged entry"));
    }

    [Test]
    [NonParallelizable]
    public void Factory_FiltersConsoleAndFileIndependently()
    {
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);

        try
        {
            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["Logging:Console:Enabled"] = "true",
                ["Logging:Console:LogLevel:Default"] = "Warning",
                ["Logging:File:LogLevel:Default"] = "Debug"
            });

            using (var factory = ApplicationLogging.Create(configuration))
            {
                var logger = factory.CreateLogger("Application");
                logger.LogDebug("File only");
                logger.LogWarning("Both destinations");
            }

            Assert.That(output.ToString(), Does.Not.Contain("File only"));
            Assert.That(output.ToString(), Does.Contain("Both destinations"));
            Assert.That(File.ReadAllText(logPath), Does.Contain("File only").And.Contain("Both destinations"));
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    [Test]
    public void Provider_RotatesBySizeAndRetainsOnlyConfiguredFiles()
    {
        var unrelated = logPath + ".archive-keep-me";
        File.WriteAllText(unrelated, "keep");
        var options = new FileLogOptions { Path = logPath, MaxFileSizeBytes = 300, RetainedFileCount = 3 };

        using (var provider = new FileLogProvider(options, new LogRedactor(CreateConfiguration())))
        {
            var logger = provider.CreateLogger("Application");

            for (var index = 0; index < 20; index++)
            {
                logger.LogInformation("Entry {Index}: {Message}", index, new string('a', 100));
            }
        }

        var files = Directory.GetFiles(directory).Where(path => path.Equals(unrelated, StringComparison.Ordinal) is false).ToArray();
        Assert.That(files, Has.Length.EqualTo(3));
        Assert.That(files.All(path => new FileInfo(path).Length <= 300), Is.True);
        Assert.That(File.ReadAllText(logPath), Does.Contain("Entry 19"));
        Assert.That(File.ReadAllText(unrelated), Is.EqualTo("keep"));
    }

    [Test]
    [NonParallelizable]
    public async Task ProxyMode_UsesSharedLoggingForRequestsAndKeepsFactoryAliveAfterShutdown()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Logging:File:LogLevel:Microsoft"] = "Debug",
            ["Downloads:0:Name"] = "Playlist",
            ["Downloads:0:Url"] = "http://provider.example/get.php?username=known-user&password=known-password",
            ["Downloads:0:FilePath"] = Path.Combine(directory, "playlist.m3u"),
            ["Proxy:ListenUrl"] = $"http://127.0.0.1:{port}",
            ["Proxy:Sources:0:Name"] = "Example",
            ["Proxy:Sources:0:PlaylistDownload"] = "Playlist"
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using (var factory = ApplicationLogging.Create(configuration))
        {
            var proxyTask = ProxyMode.RunAsync(configuration, cancellation.Token, factory);

            try
            {
                using var client = new HttpClient();
                HttpResponseMessage? response = null;

                while (response is null)
                {
                    try
                    {
                        response = await client.GetAsync($"http://127.0.0.1:{port}/get.php?username=unknown-user&password=unknown-password&token=unknown-token", cancellation.Token);
                    }
                    catch (HttpRequestException) when (proxyTask.IsCompleted is false)
                    {
                        await Task.Delay(20, cancellation.Token);
                    }
                }

                using (response)
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                }
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
            }

            factory.CreateLogger("Application").LogInformation("Application stopped after proxy shutdown");
        }

        var contents = File.ReadAllText(logPath);
        Assert.That(contents, Does.Contain("Proxy started with 1 sources"));
        Assert.That(contents, Does.Contain("status=401"));
        Assert.That(contents, Does.Contain("Application stopped after proxy shutdown"));

        foreach (var secret in new[] { "known-user", "known-password", "unknown-user", "unknown-password", "unknown-token" })
        {
            Assert.That(contents, Does.Not.Contain(secret));
        }
    }

    [Test]
    public void Provider_RotatesExistingFileFromPreviousDay()
    {
        File.WriteAllText(logPath, "Previous day");
        File.SetLastWriteTimeUtc(logPath, DateTime.UtcNow.AddDays(-1));

        using (var provider = new FileLogProvider(new FileLogOptions { Path = logPath }, new LogRedactor(CreateConfiguration())))
        {
            provider.CreateLogger("Application").LogInformation("New day");
        }

        Assert.That(File.ReadAllText(logPath), Does.Not.Contain("Previous day"));
        Assert.That(File.ReadAllText(Directory.GetFiles(directory, "*.archive-*").Single()), Is.EqualTo("Previous day"));
    }

    [Test]
    public void Provider_LimitsOversizedEntriesAndSerializesConcurrentWriters()
    {
        using (var provider = new FileLogProvider(new FileLogOptions { Path = logPath, MaxFileSizeBytes = 1024 * 1024 }, new LogRedactor(CreateConfiguration())))
        {
            var logger = provider.CreateLogger("Application");
            logger.LogInformation("{Oversized}", new string('a', 1024 * 1024));
            Parallel.For(0, 100, index => logger.LogInformation("Concurrent entry {Index}", index));
        }

        var lines = File.ReadAllLines(logPath);
        Assert.That(lines, Has.Length.EqualTo(101));
        Assert.That(lines[0], Does.Contain("entry omitted"));
        Assert.That(lines.Skip(1).All(line => line.Contains("[Information] Application Concurrent entry ", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    [Platform("Win")]
    public void Provider_ReportsRotationFailureOnceAndStopsWriting()
    {
        var failures = new List<string>();
        using var provider = new FileLogProvider(new FileLogOptions { Path = logPath, MaxFileSizeBytes = 256 }, new LogRedactor(CreateConfiguration()), failures.Add);
        var logger = provider.CreateLogger("Application");
        logger.LogInformation("First {Message}", new string('a', 100));
        using var lockedFile = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        logger.LogInformation("Second {Message}", new string('b', 100));
        logger.LogInformation("Third");
        Assert.That(failures, Has.Count.EqualTo(1));
        Assert.That(failures[0], Does.Contain("File logging failed and has been disabled"));
    }

    [TestCase(255, 1)]
    [TestCase(256, 0)]
    public void Provider_RejectsUnboundedSettings(long size, int retainedCount)
    {
        Assert.That(() => new FileLogProvider(new FileLogOptions { Path = logPath, MaxFileSizeBytes = size, RetainedFileCount = retainedCount }, new LogRedactor(CreateConfiguration())), Throws.TypeOf<InvalidOperationException>());
    }

    private IConfiguration CreateConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Logging:Console:Enabled"] = "false",
            ["Logging:File:Enabled"] = "true",
            ["Logging:File:Path"] = logPath,
            ["Logging:LogLevel:Default"] = "Information"
        };

        foreach (var setting in overrides ?? [])
        {
            settings[setting.Key] = setting.Value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }
}
