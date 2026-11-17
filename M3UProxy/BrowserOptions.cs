using PuppeteerSharp;
using PuppeteerSharp.BrowserData;

namespace DotNetLab.M3UProxy;

public sealed record BrowserOptions
{
    public string Channel { get; init; } = "chrome";
    public string? ExecutablePath { get; init; }

    public LaunchOptions CreateLaunchOptions()
    {
        var options = new LaunchOptions { Headless = true, Args = ["--lang=sv-SE"] };

        if (string.IsNullOrWhiteSpace(ExecutablePath) is false)
        {
            options.ExecutablePath = Path.GetFullPath(ExecutablePath, AppContext.BaseDirectory);

            if (File.Exists(options.ExecutablePath) is false)
            {
                throw new InvalidOperationException("Browser:ExecutablePath must point to an existing browser executable.");
            }

            return options;
        }

        switch (Channel.Trim().ToLowerInvariant())
        {
            case "chrome":
                options.Channel = ChromeReleaseChannel.Stable;
                break;
            case "chrome-beta":
                options.Channel = ChromeReleaseChannel.Beta;
                break;
            case "chrome-dev":
                options.Channel = ChromeReleaseChannel.Dev;
                break;
            case "chrome-canary":
                options.Channel = ChromeReleaseChannel.Canary;
                break;
            case "msedge" or "msedge-beta" or "msedge-dev" or "msedge-canary":
                options.ExecutablePath = FindEdgeExecutable(Channel.Trim().ToLowerInvariant());
                break;
            default:
                throw new InvalidOperationException("Browser:Channel must be chrome, chrome-beta, chrome-dev, chrome-canary, msedge, msedge-beta, msedge-dev, or msedge-canary. Alternatively, set Browser:ExecutablePath.");
        }

        return options;
    }

    private static string FindEdgeExecutable(string channel)
    {
        var candidates = GetEdgeExecutableCandidates(channel);

        return candidates.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException($"The browser channel '{channel}' was not found. Install it or set Browser:ExecutablePath.");
    }

    private static IEnumerable<string> GetEdgeExecutableCandidates(string channel)
    {
        var suffix = channel switch
        {
            "msedge-beta" => " Beta",
            "msedge-dev" => " Dev",
            "msedge-canary" => " SxS",
            _ => string.Empty
        };

        if (OperatingSystem.IsWindows())
        {
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };

            return roots.Where(root => string.IsNullOrEmpty(root) is false).Select(root => Path.Combine(root, "Microsoft", $"Edge{suffix}", "Application", "msedge.exe"));
        }

        if (OperatingSystem.IsMacOS())
        {
            var application = suffix is " SxS" ? "Microsoft Edge Canary" : $"Microsoft Edge{suffix}";

            return [Path.Combine("/Applications", $"{application}.app", "Contents", "MacOS", application)];
        }

        var executable = channel.Replace("msedge", "microsoft-edge", StringComparison.Ordinal);

        return [Path.Combine("/usr/bin", executable), Path.Combine("/usr/bin", executable + "-stable"), Path.Combine("/opt/microsoft", channel, "msedge")];
    }
}
