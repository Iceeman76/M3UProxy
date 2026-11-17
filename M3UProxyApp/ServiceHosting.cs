using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public static class ServiceHosting
{
    public static bool IsService => WindowsServiceHelpers.IsWindowsService() || SystemdHelpers.IsSystemdService();

    public static void Configure(IServiceCollection services, ILoggerFactory loggerFactory)
    {
        services.AddWindowsService(options => options.ServiceName = "M3UProxy");
        services.AddSystemd();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddSingleton(loggerFactory);
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
    }
}
