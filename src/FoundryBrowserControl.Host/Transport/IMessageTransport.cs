namespace FoundryBrowserControl.Host.Transport;

/// <summary>
/// Abstraction for bidirectional message transport (WebSocket, Native Messaging, etc.)
/// </summary>
public interface IMessageTransport : IAsyncDisposable
{
    Task<T?> ReadAsync<T>(CancellationToken ct) where T : class;
    Task WriteAsync<T>(T message, CancellationToken ct);
    bool IsConnected { get; }
}
