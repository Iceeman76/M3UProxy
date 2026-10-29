using DotNetLab.M3UProxyApp;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ProxyRequestLoggerTests
{
    [TestCase(0, 0, 0, 42, "42s")]
    [TestCase(0, 0, 12, 34, "12m34s")]
    [TestCase(0, 5, 12, 34, "5h12m")]
    [TestCase(3, 5, 12, 34, "3d5h")]
    public void FormatAge_UsesCompactReadableUnits(int days, int hours, int minutes, int seconds, string expected)
    {
        var age = new TimeSpan(days, hours, minutes, seconds);

        var result = ProxyRequestLogger.FormatAge(age);

        Assert.That(result, Is.EqualTo(expected));
    }
}
