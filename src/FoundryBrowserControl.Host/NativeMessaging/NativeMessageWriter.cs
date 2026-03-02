using System.Buffers.Binary;
using System.Text.Json;

namespace FoundryBrowserControl.Host.NativeMessaging;

/// <summary>
/// Writes Chrome/Edge native messaging protocol messages to stdout.
/// Each message is a 4-byte little-endian length prefix followed by UTF-8 JSON.
/// </summary>
public sealed class NativeMessageWriter : IDisposable
{
    private readonly Stream _output;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public NativeMessageWriter(Stream? output = null)
    {
        _output = output ?? Console.OpenStandardOutput();
    }

    /// <summary>
    /// Writes a message with the native messaging length prefix.
    /// Thread-safe via semaphore.
    /// </summary>
    public async Task WriteAsync<T>(T message, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

        await _writeLock.WaitAsync(ct);
        try
        {
            var lengthBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, json.Length);

            await _output.WriteAsync(lengthBytes, ct);
            await _output.WriteAsync(json, ct);
            await _output.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _output.Dispose();
    }
}
