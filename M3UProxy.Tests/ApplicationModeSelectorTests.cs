using M3UProxyApp;
using NUnit.Framework;

namespace M3UProxy.Tests;

public sealed class ApplicationModeSelectorTests
{
    [TestCase("proxy", nameof(ApplicationMode.Proxy))]
    [TestCase("--proxy", nameof(ApplicationMode.Proxy))]
    [TestCase("download", nameof(ApplicationMode.Download))]
    [TestCase("--download", nameof(ApplicationMode.Download))]
    public void Select_WithModeArgument_ReturnsExpectedMode(string argument, string expectedMode)
    {
        var mode = ApplicationModeSelector.Select([argument]);

        Assert.That(mode.ToString(), Is.EqualTo(expectedMode));
    }

    [Test]
    public void Select_WithUppercaseArgument_ReturnsExpectedMode()
    {
        var mode = ApplicationModeSelector.Select(["--PROXY"]);

        Assert.That(mode, Is.EqualTo(ApplicationMode.Proxy));
    }

    [Test]
    public void Select_WithUnknownArgument_ThrowsArgumentException()
    {
        Assert.That(() => ApplicationModeSelector.Select(["--unknown"]), Throws.ArgumentException.With.Message.EqualTo("Use '--download' or '--proxy' as the command-line argument."));
    }
}
