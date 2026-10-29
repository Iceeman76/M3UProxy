using System.Text.Json;
using Microsoft.Playwright;

namespace DotNetLab.M3UProxy;

public sealed class ChromeContentClient : IAsyncDisposable
{
    private const float NavigationTimeoutMilliseconds = 300_000;
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;

    public async Task<ChromeContentResponse> OpenAsync(string url, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var activeBrowser = await GetBrowserAsync();
        var context = await activeBrowser.NewContextAsync(new BrowserNewContextOptions { Locale = "sv-SE", ExtraHTTPHeaders = headers });

        try
        {
            return await OpenResponseAsync(context, url, cancellationToken);
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

        playwright?.Dispose();
        initializationLock.Dispose();
    }

    private static async Task<ChromeContentResponse> OpenResponseAsync(IBrowserContext context, string url, CancellationToken cancellationToken)
    {
        var page = await context.NewPageAsync();
        page.SetDefaultNavigationTimeout(NavigationTimeoutMilliseconds);
        var cdpSession = await context.NewCDPSessionAsync(page);
        var responsePaused = CreateResponseTask(cdpSession);
        await EnableResponseInterceptionAsync(cdpSession);
        var navigationTask = page.GotoAsync(url, new PageGotoOptions { Timeout = NavigationTimeoutMilliseconds, WaitUntil = WaitUntilState.Commit });
        var completedTask = await Task.WhenAny(responsePaused.Task, navigationTask).WaitAsync(cancellationToken);

        if (ReferenceEquals(completedTask, navigationTask))
        {
            await navigationTask;
            throw new IOException("Chrome completed navigation without returning a response body.");
        }

        var pausedResponse = await responsePaused.Task.WaitAsync(cancellationToken);
        var streamHandle = await TakeResponseStreamAsync(cdpSession, pausedResponse);

        return new ChromeContentResponse(context, cdpSession, streamHandle, GetStatusCode(pausedResponse), GetHeaders(pausedResponse));
    }

    private static TaskCompletionSource<JsonElement> CreateResponseTask(ICDPSession cdpSession)
    {
        var responsePaused = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        cdpSession.Event("Fetch.requestPaused").OnEvent += async (eventSource, eventArgs) =>
        {
            if (IsDocumentResponse(eventArgs, out var response) is false)
            {
                return;
            }

            if (response.GetProperty("responseStatusCode").GetInt32() is >= 300 and < 400)
            {
                await ContinueRedirectAsync(cdpSession, responsePaused, response);

                return;
            }

            responsePaused.TrySetResult(response.Clone());
        };

        return responsePaused;
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

    private async Task<IBrowser> GetBrowserAsync()
    {
        if (browser is not null)
        {
            return browser;
        }

        await initializationLock.WaitAsync();

        try
        {
            if (browser is not null)
            {
                return browser;
            }

            playwright = await Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "chrome", Headless = true });

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
