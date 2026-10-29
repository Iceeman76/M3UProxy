using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace DotNetLab.M3UProxy;

public sealed class ChromeContentResponse(IBrowserContext context, ICDPSession cdpSession, string streamHandle, int statusCode, IReadOnlyDictionary<string, string> headers) : IAsyncDisposable
{
    private const int ChunkSize = 64 * 1024;
    private bool isComplete;

    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> Headers { get; } = headers;

    public async Task<byte[]> ReadChunkAsync(CancellationToken cancellationToken = default)
    {
        while (isComplete is false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readResult = await ReadAsync();
            isComplete = readResult.GetProperty("eof").GetBoolean();
            var content = GetContent(readResult);

            if (content.Length > 0)
            {
                return content;
            }
        }

        return [];
    }

    public async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var bytes = await ReadChunkAsync(cancellationToken);

            if (bytes.Length is 0)
            {
                return;
            }

            await destination.WriteAsync(bytes, cancellationToken);
        }
    }

    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        await using var content = new MemoryStream();
        await CopyToAsync(content, cancellationToken);

        return content.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await cdpSession.SendAsync("IO.close", new Dictionary<string, object> { ["handle"] = streamHandle });
        }
        catch (PlaywrightException)
        {
        }

        await context.DisposeAsync();
    }

    private async Task<JsonElement> ReadAsync()
    {
        var response = await cdpSession.SendAsync("IO.read", new Dictionary<string, object> { ["handle"] = streamHandle, ["size"] = ChunkSize });

        return response ?? throw new IOException("Chrome returned an invalid stream response.");
    }

    private static byte[] GetContent(JsonElement readResult)
    {
        var content = readResult.GetProperty("data").GetString() ?? string.Empty;
        var isBase64 = readResult.TryGetProperty("base64Encoded", out var base64Property) && base64Property.GetBoolean();

        return isBase64 ? Convert.FromBase64String(content) : Encoding.UTF8.GetBytes(content);
    }
}
