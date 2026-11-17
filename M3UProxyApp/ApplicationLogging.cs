using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetLab.M3UProxyApp;

public static class ApplicationLogging
{
    public static ILoggerFactory Create(IConfiguration configuration)
    {
        var redactor = new LogRedactor(configuration);
        var options = configuration.GetSection("Logging:File").Get<FileLogOptions>() ?? new FileLogOptions();

        return LoggerFactory.Create(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));

            if (configuration.GetValue("Logging:Console:Enabled", true))
            {
                builder.Services.AddSingleton<ILoggerProvider>(_ => new ConsoleLogProvider(redactor));
            }

            if (options.Enabled)
            {
                builder.Services.AddSingleton<ILoggerProvider>(_ => new FileLogProvider(options, redactor));
            }
        });
    }
}
