using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace FoundryBrowserControl.Host.Transport;

/// <summary>
/// WebSocket-based message transport. Sends/receives JSON over a WebSocket connection.
/// </summary>
public sealed class WebSocketTransport : IMessageTransport
{
    private readonly WebSocket _ws;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public WebSocketTransport(WebSocket ws)
    {
        _ws = ws;
    }

    public bool IsConnected => _ws.State == WebSocketState.Open;

    public async Task<T?> ReadAsync<T>(CancellationToken ct) where T : class
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;

        do
        {
            result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        ms.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(ms, JsonOptions, ct);
    }

    public async Task WriteAsync<T>(T message, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _ws.SendAsync(
                new ArraySegment<byte>(json),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Host shutting down", CancellationToken.None);
            }
            catch { /* best effort */ }
        }
        _writeLock.Dispose();
        _ws.Dispose();
    }
}
