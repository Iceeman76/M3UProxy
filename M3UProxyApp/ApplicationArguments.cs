namespace DotNetLab.M3UProxyApp;

public sealed record ApplicationArguments(ApplicationMode? Mode, bool Service, string ConfigurationPath)
{
    public static ApplicationArguments Parse(IReadOnlyList<string> arguments, bool serviceDetected = false)
    {
        ApplicationMode? mode = null;
        var service = serviceDetected;
        var configurationPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index].ToLowerInvariant();

            if (argument is "--service")
            {
                service = true;
                continue;
            }

            if (argument is "--config")
            {
                if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("--config requires a configuration file path.");
                }

                configurationPath = Path.GetFullPath(arguments[index], AppContext.BaseDirectory);
                continue;
            }

            if (argument is not ("proxy" or "--proxy" or "download" or "--download"))
            {
                throw new ArgumentException("Use --proxy or --download, optionally with --service and --config <path>.");
            }

            if (mode is not null)
            {
                throw new ArgumentException("Choose exactly one operating mode: --proxy or --download.");
            }

            mode = argument is "proxy" or "--proxy" ? ApplicationMode.Proxy : ApplicationMode.Download;
        }

        return new ApplicationArguments(mode ?? (service ? ApplicationMode.Proxy : null), service, configurationPath);
    }
}
