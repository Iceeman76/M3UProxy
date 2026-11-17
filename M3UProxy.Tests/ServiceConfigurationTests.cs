using System.Text.Json;
using DotNetLab.M3UProxyApp;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ServiceConfigurationTests
{
    [Test]
    public void Arguments_ServiceDefaultsToProxyWithoutPrompt()
    {
        var arguments = ApplicationArguments.Parse(["--service"]);
        Assert.That(arguments.Service, Is.True);
        Assert.That(arguments.Mode, Is.EqualTo(ApplicationMode.Proxy));
    }

    [Test]
    public void Arguments_DetectsNativeServiceWithoutFlag()
    {
        var arguments = ApplicationArguments.Parse([], serviceDetected: true);
        Assert.That(arguments.Service, Is.True);
        Assert.That(arguments.Mode, Is.EqualTo(ApplicationMode.Proxy));
    }

    [Test]
    public void Arguments_PreservesInteractiveMenuWhenNoModeIsSpecified()
    {
        var arguments = ApplicationArguments.Parse([]);
        Assert.That(arguments.Service, Is.False);
        Assert.That(arguments.Mode, Is.Null);
    }

    [TestCase("--proxy", ApplicationMode.Proxy)]
    [TestCase("proxy", ApplicationMode.Proxy)]
    [TestCase("--download", ApplicationMode.Download)]
    [TestCase("DOWNLOAD", ApplicationMode.Download)]
    public void Arguments_SelectsModeAndConfigurationIndependentlyOfFlagOrder(string mode, ApplicationMode expectedMode)
    {
        var configurationPath = Path.Combine(Path.GetTempPath(), "config with spaces", "service.json");
        var arguments = ApplicationArguments.Parse(["--config", configurationPath, mode, "--service"]);
        Assert.That(arguments.Mode, Is.EqualTo(expectedMode));
        Assert.That(arguments.ConfigurationPath, Is.EqualTo(configurationPath));
    }

    [TestCase("--config")]
    [TestCase("--unknown")]
    public void Arguments_RejectsInvalidFlag(string flag)
    {
        Assert.That(() => ApplicationArguments.Parse([flag]), Throws.ArgumentException);
    }

    [Test]
    public void Arguments_RejectsConflictingModes()
    {
        Assert.That(() => ApplicationArguments.Parse(["--proxy", "--download"]), Throws.ArgumentException);
    }

    [Test]
    public void Arguments_RejectsMissingConfigurationValue()
    {
        Assert.That(() => ApplicationArguments.Parse(["--config", "--service"]), Throws.ArgumentException);
    }

    [Test]
    public void Configuration_ResolvesAllFilePathsAgainstSelectedConfigurationDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"m3uproxy-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "service.json");
        var content = JsonSerializer.Serialize(new
        {
            Browser = new { ExecutablePath = Path.Combine("browser", "chrome") },
            Logging = new { File = new { Path = Path.Combine("logs", "proxy.log") } },
            Downloads = new[] { new { FilePath = Path.Combine("playlists", "provider.m3u") } }
        });
        File.WriteAllText(path, content);

        try
        {
            var configuration = ApplicationConfiguration.Load(path);
            using var configurationLifetime = (IDisposable)configuration;
            Assert.That(configuration["Browser:ExecutablePath"], Is.EqualTo(Path.Combine(directory, "browser", "chrome")));
            Assert.That(configuration["Logging:File:Path"], Is.EqualTo(Path.Combine(directory, "logs", "proxy.log")));
            Assert.That(configuration["Downloads:0:FilePath"], Is.EqualTo(Path.Combine(directory, "playlists", "provider.m3u")));
            Assert.That(File.ReadAllText(path), Is.EqualTo(content));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(71583)]
    [TestCase(double.Epsilon)]
    public void ServiceOptions_RejectsInvalidRefreshInterval(double interval)
    {
        var options = new ServiceOptions { RefreshIntervalMinutes = interval };
        Assert.That(() => options.Validate(ApplicationMode.Proxy), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ServiceOptions_AllowsDisablingScheduledRefreshInProxyMode()
    {
        Assert.That(() => new ServiceOptions { RefreshIntervalMinutes = 0 }.Validate(ApplicationMode.Proxy), Throws.Nothing);
    }

    [Test]
    public void ServiceOptions_RequiresScheduleForDownloadOnlyService()
    {
        Assert.That(() => new ServiceOptions { RefreshIntervalMinutes = 0 }.Validate(ApplicationMode.Download), Throws.TypeOf<InvalidOperationException>());
    }
}
