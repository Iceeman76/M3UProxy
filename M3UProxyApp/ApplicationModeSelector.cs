using Spectre.Console;

namespace M3UProxyApp;

internal static class ApplicationModeSelector
{
    private const string DownloadLabel = "Download M3U files";
    private const string ProxyLabel = "Start proxy";

    public static ApplicationMode Select(IEnumerable<string> arguments)
    {
        return arguments.FirstOrDefault()?.ToLowerInvariant() switch
        {
            "download" or "--download" => ApplicationMode.Download,
            "proxy" or "--proxy" => ApplicationMode.Proxy,
            null => Prompt(),
            _ => throw new ArgumentException("Use '--download' or '--proxy' as the command-line argument.")
        };
    }

    private static ApplicationMode Prompt()
    {
        var selection = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("What do you want to do?").AddChoices(DownloadLabel, ProxyLabel));

        return selection is DownloadLabel ? ApplicationMode.Download : ApplicationMode.Proxy;
    }
}
