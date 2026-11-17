using System.Text.Json;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;

namespace DotNetLab.M3UProxy;

public sealed class ChromeContentClient(BrowserOptions? options = null, ILogger? logger = null) : IAsyncDisposable
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromMinutes(5);
    private readonly LaunchOptions launchOptions = (options ?? new BrowserOptions()).CreateLaunchOptions();
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private IBrowser? browser;

    public async Task<ChromeContentResponse> OpenAsync(string url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var activeBrowser = await GetBrowserAsync(cancellationToken);
        var context = await activeBrowser.CreateBrowserContextAsync();

        try
        {
            return await OpenResponseAsync(context, url, headers, cancellationToken);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        initializationLock.Dispose();
    }

    private static async Task<ChromeContentResponse> OpenResponseAsync(IBrowserContext context, string url, IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        var page = await context.NewPageAsync();
        var requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Accept-Language"] = "sv-SE" };

        foreach (var header in headers ?? new Dictionary<string, string>())
        {
            requestHeaders[header.Key] = header.Value;
        }

        await page.SetExtraHttpHeadersAsync(requestHeaders);
        var cdpSession = await page.CreateCDPSessionAsync();
        await cdpSession.SendAsync("Emulation.setLocaleOverride", new { locale = "sv-SE" });
        var responsePaused = CreateResponseTask(cdpSession);
        await EnableResponseInterceptionAsync(cdpSession);
        _ = NavigateAsync(cdpSession, url, responsePaused);
        var pausedResponse = await responsePaused.Task.WaitAsync(NavigationTimeout, cancellationToken);
        var streamHandle = await TakeResponseStreamAsync(cdpSession, pausedResponse).WaitAsync(cancellationToken);

        return new ChromeContentResponse(context, cdpSession, streamHandle, GetStatusCode(pausedResponse), GetHeaders(pausedResponse));
    }

    private static TaskCompletionSource<JsonElement> CreateResponseTask(ICDPSession cdpSession)
    {
        var responsePaused = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        cdpSession.MessageReceived += (_, eventArgs) =>
        {
            if (eventArgs.MessageID is not "Fetch.requestPaused" || IsDocumentResponse(eventArgs.MessageData, out var response) is false)
            {
                return;
            }

            if (response.GetProperty("responseStatusCode").GetInt32() is >= 300 and < 400)
            {
                _ = ContinueRedirectAsync(cdpSession, responsePaused, response);

                return;
            }

            responsePaused.TrySetResult(response.Clone());
        };

        return responsePaused;
    }

    private static async Task NavigateAsync(ICDPSession cdpSession, string url, TaskCompletionSource<JsonElement> responsePaused)
    {
        try
        {
            var response = await cdpSession.SendAsync("Page.navigate", new { url });

            if (response is { } navigation && navigation.TryGetProperty("errorText", out var error))
            {
                responsePaused.TrySetException(new IOException($"Browser navigation failed: {error.GetString()}"));
            }
        }
        catch (Exception exception)
        {
            responsePaused.TrySetException(exception);
        }
    }

    private static bool IsDocumentResponse(JsonElement? eventArgs, out JsonElement response)
    {
        response = eventArgs ?? default;

        return eventArgs is not null && response.TryGetProperty("resourceType", out var resourceType) && resourceType.GetString() is "Document" && response.TryGetProperty("responseStatusCode", out _);
    }

    private static async Task ContinueRedirectAsync(ICDPSession cdpSession, TaskCompletionSource<JsonElement> responsePaused, JsonElement response)
    {
        try
        {
            var requestId = response.GetProperty("requestId").GetString() ?? throw new IOException("Chrome did not return a redirect request ID.");
            await cdpSession.SendAsync("Fetch.continueResponse", new Dictionary<string, object> { ["requestId"] = requestId });
        }
        catch (Exception exception)
        {
            responsePaused.TrySetException(exception);
        }
    }

    private static Task<JsonElement?> EnableResponseInterceptionAsync(ICDPSession cdpSession) => cdpSession.SendAsync("Fetch.enable", new Dictionary<string, object>
    {
        ["patterns"] = new[]
        {
            new Dictionary<string, object>
            {
                ["urlPattern"] = "*",
                ["requestStage"] = "Response"
            }
        }
    });

    private static async Task<string> TakeResponseStreamAsync(ICDPSession cdpSession, JsonElement pausedResponse)
    {
        var requestId = pausedResponse.GetProperty("requestId").GetString() ?? throw new IOException("Chrome did not return a request ID.");
        var streamResponse = await cdpSession.SendAsync("Fetch.takeResponseBodyAsStream", new Dictionary<string, object> { ["requestId"] = requestId });

        return streamResponse?.GetProperty("stream").GetString() ?? throw new IOException("Chrome did not return a response stream.");
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (browser is not null)
        {
            return browser;
        }

        await initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (browser is not null)
            {
                return browser;
            }

            browser = await Puppeteer.LaunchAsync(launchOptions);
            var selection = string.IsNullOrWhiteSpace(options?.ExecutablePath) ? "channel" : "executable_path";
            logger?.LogInformation("Browser started channel={Channel} executable_path={ExecutablePath} selection={Selection}", options?.Channel.Trim().ToLowerInvariant() ?? "chrome", browser.Process.StartInfo.FileName, selection);
            cancellationToken.ThrowIfCancellationRequested();

            return browser;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static int GetStatusCode(JsonElement pausedResponse) => pausedResponse.GetProperty("responseStatusCode").GetInt32();

    private static IReadOnlyDictionary<string, string> GetHeaders(JsonElement pausedResponse)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (pausedResponse.TryGetProperty("responseHeaders", out var responseHeaders) is false)
        {
            return headers;
        }

        foreach (var header in responseHeaders.EnumerateArray())
        {
            var name = header.GetProperty("name").GetString();
            var value = header.GetProperty("value").GetString();

            if (name is not null && value is not null)
            {
                headers[name] = value;
            }
        }

        return headers;
    }
}
