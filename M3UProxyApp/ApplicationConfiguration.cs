using Microsoft.Extensions.Configuration;

namespace DotNetLab.M3UProxyApp;

public static class ApplicationConfiguration
{
    public static IConfigurationRoot Load(string configurationPath)
    {
        var fullPath = Path.GetFullPath(configurationPath, AppContext.BaseDirectory);
        var directory = Path.GetDirectoryName(fullPath)!;
        var configuration = new ConfigurationBuilder().SetBasePath(directory).AddJsonFile(Path.GetFileName(fullPath), optional: false, reloadOnChange: false).Build();
        var paths = new Dictionary<string, string?>();

        foreach (var download in configuration.GetSection("Downloads").GetChildren())
        {
            ResolvePath(download.GetSection("FilePath"), directory, paths);
        }

        ResolvePath(configuration.GetSection("Browser:ExecutablePath"), directory, paths);
        paths["Logging:File:Path"] = Path.GetFullPath(configuration["Logging:File:Path"] ?? "logs/m3uproxy.log", directory);

        foreach (var path in paths)
        {
            configuration[path.Key] = path.Value;
        }

        return configuration;
    }

    private static void ResolvePath(IConfigurationSection setting, string directory, IDictionary<string, string?> paths)
    {
        if (string.IsNullOrWhiteSpace(setting.Value) is false)
        {
            paths[setting.Path] = Path.GetFullPath(setting.Value, directory);
        }
    }
}
