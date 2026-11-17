using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public static class DownloadFileLogger
{
    public static void LogStatus(IEnumerable<DownloadJob> downloads, ILogger logger, DateTime utcNow)
    {
        foreach (var download in downloads)
        {
            var exists = File.Exists(download.FilePath);
            var status = exists ? "EXISTS" : "MISSING";
            var age = exists ? ProxyRequestLogger.FormatAge(utcNow - File.GetLastWriteTimeUtc(download.FilePath)) : "missing";
            logger.LogInformation("Downloaded file name={Name} path={FilePath} status={Status} file_age={FileAge}", download.Name, download.FilePath, status, age);
        }
    }
}
