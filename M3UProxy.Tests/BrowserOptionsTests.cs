using DotNetLab.M3UProxy;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using PuppeteerSharp.BrowserData;

namespace DotNetLab.M3UProxy.Tests;

public sealed class BrowserOptionsTests
{
    [Test]
    public void Defaults_SelectInstalledStableChrome()
    {
        var options = new BrowserOptions().CreateLaunchOptions();
        Assert.That(options.Channel, Is.EqualTo(ChromeReleaseChannel.Stable));
        Assert.That(options.ExecutablePath, Is.Null);
        Assert.That(options.Headless, Is.True);
    }

    [TestCase("chrome", ChromeReleaseChannel.Stable)]
    [TestCase("CHROME-BETA", ChromeReleaseChannel.Beta)]
    [TestCase(" chrome-dev ", ChromeReleaseChannel.Dev)]
    [TestCase("chrome-canary", ChromeReleaseChannel.Canary)]
    public void ChromeChannels_SelectMatchingRelease(string channel, ChromeReleaseChannel expected)
    {
        var options = new BrowserOptions { Channel = channel }.CreateLaunchOptions();
        Assert.That(options.Channel, Is.EqualTo(expected));
    }

    [TestCase("firefox")]
    [TestCase("unknown")]
    [TestCase("")]
    public void UnsupportedChannels_ReportConfigurationError(string channel)
    {
        Assert.That(() => new BrowserOptions { Channel = channel }.CreateLaunchOptions(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Browser:Channel"));
    }

    [Test]
    public void ExecutablePath_OverridesChannel()
    {
        var executablePath = Path.GetTempFileName();

        try
        {
            var options = new BrowserOptions { Channel = "custom-browser", ExecutablePath = executablePath }.CreateLaunchOptions();
            Assert.That(options.ExecutablePath, Is.EqualTo(executablePath));
            Assert.That(options.Channel, Is.Null);
        }
        finally
        {
            File.Delete(executablePath);
        }
    }

    [Test]
    public void RelativeExecutablePath_ResolvesAgainstApplicationDirectory()
    {
        var relativePath = Path.GetRelativePath(AppContext.BaseDirectory, typeof(BrowserOptions).Assembly.Location);
        var options = new BrowserOptions { ExecutablePath = relativePath }.CreateLaunchOptions();
        Assert.That(options.ExecutablePath, Is.EqualTo(typeof(BrowserOptions).Assembly.Location));
    }

    [Test]
    public void MissingExecutable_ReportsConfigurationError()
    {
        var executablePath = Path.Combine(Path.GetTempPath(), $"missing-browser-{Guid.NewGuid():N}");
        Assert.That(() => new BrowserOptions { ExecutablePath = executablePath }.CreateLaunchOptions(), Throws.TypeOf<InvalidOperationException>().With.Message.Contains("Browser:ExecutablePath"));
    }

    [Test]
    public void Configuration_BindsChannelAndExecutablePath()
    {
        var settings = new Dictionary<string, string?> { ["Browser:Channel"] = "msedge", ["Browser:ExecutablePath"] = "/custom/browser" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = configuration.GetSection("Browser").Get<BrowserOptions>();
        Assert.That(options, Is.EqualTo(new BrowserOptions { Channel = "msedge", ExecutablePath = "/custom/browser" }));
    }
}
