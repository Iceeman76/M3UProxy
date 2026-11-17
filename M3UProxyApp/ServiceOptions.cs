namespace DotNetLab.M3UProxyApp;

public sealed record ServiceOptions
{
    public double RefreshIntervalMinutes { get; init; } = 60;
    public bool RefreshOnStartup { get; init; } = true;

    public void Validate(ApplicationMode mode)
    {
        if (double.IsFinite(RefreshIntervalMinutes) is false || RefreshIntervalMinutes < 0 || RefreshIntervalMinutes > 71582)
        {
            throw new InvalidOperationException("Service:RefreshIntervalMinutes must be between 0 and 71582 minutes.");
        }

        if (RefreshIntervalMinutes > 0 && RefreshIntervalMinutes * 60000 < 1)
        {
            throw new InvalidOperationException("A nonzero Service:RefreshIntervalMinutes must be at least one millisecond.");
        }

        if (mode is ApplicationMode.Download && RefreshIntervalMinutes is 0)
        {
            throw new InvalidOperationException("Download service mode requires Service:RefreshIntervalMinutes greater than 0.");
        }
    }
}
