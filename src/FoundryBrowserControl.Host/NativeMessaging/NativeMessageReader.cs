using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace FoundryBrowserControl.Host.NativeMessaging;

/// <summary>
/// Reads Chrome/Edge native messaging protocol messages from stdin.
/// Each message is a 4-byte little-endian length prefix followed by UTF-8 JSON.
/// </summary>
public sealed class NativeMessageReader : IDisposable
{
    private readonly Stream _input;

    public NativeMessageReader(Stream? input = null)
    {
        _input = input ?? Console.OpenStandardInput();
    }

    /// <summary>
    /// Reads the next message. Returns null if the stream is closed.
    /// </summary>
    public async Task<T?> ReadAsync<T>(CancellationToken ct = default)
    {
        var lengthBytes = new byte[4];
        var bytesRead = await ReadExactAsync(_input, lengthBytes, ct);
        if (bytesRead < 4)
            return default;

        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > 4 * 1024 * 1024) // 4MB limit
            throw new InvalidOperationException($"Invalid message length: {length}");

        var messageBytes = new byte[length];
        bytesRead = await ReadExactAsync(_input, messageBytes, ct);
        if (bytesRead < length)
            return default;

        return JsonSerializer.Deserialize<T>(messageBytes);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
                return totalRead; // Stream closed
            totalRead += read;
        }
        return totalRead;
    }

    public void Dispose() => _input.Dispose();
}
