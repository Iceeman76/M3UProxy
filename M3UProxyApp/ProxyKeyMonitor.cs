namespace DotNetLab.M3UProxyApp;

public static class ProxyKeyMonitor
{
    public static async Task<ProxyCommand> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Console.IsInputRedirected is false && Console.KeyAvailable && GetCommand(Console.ReadKey(intercept: true)) is { } command)
            {
                return command;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    public static ProxyCommand? GetCommand(ConsoleKeyInfo key) => key.Key switch
    {
        ConsoleKey.Q => ProxyCommand.Quit,
        ConsoleKey.U => ProxyCommand.Update,
        _ => null
    };
}

public enum ProxyCommand
{
    Quit,
    Update
}
