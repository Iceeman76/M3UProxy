using DotNetLab.M3UProxyApp;
using NUnit.Framework;

namespace DotNetLab.M3UProxy.Tests;

public sealed class ProxyKeyMonitorTests
{
    [TestCase('q', ConsoleKey.Q, nameof(ProxyCommand.Quit))]
    [TestCase('Q', ConsoleKey.Q, nameof(ProxyCommand.Quit))]
    [TestCase('u', ConsoleKey.U, nameof(ProxyCommand.Update))]
    [TestCase('U', ConsoleKey.U, nameof(ProxyCommand.Update))]
    public void GetCommand_RecognizesCommandKeys(char keyCharacter, ConsoleKey consoleKey, string expected)
    {
        var key = new ConsoleKeyInfo(keyCharacter, consoleKey, false, false, false);

        var result = ProxyKeyMonitor.GetCommand(key);

        Assert.That(result?.ToString(), Is.EqualTo(expected));
    }

    [TestCase('x', ConsoleKey.X)]
    [TestCase('\r', ConsoleKey.Enter)]
    public void GetCommand_IgnoresOtherKeys(char keyCharacter, ConsoleKey consoleKey)
    {
        var key = new ConsoleKeyInfo(keyCharacter, consoleKey, false, false, false);

        var result = ProxyKeyMonitor.GetCommand(key);

        Assert.That(result, Is.Null);
    }
}
