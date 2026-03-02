using System.Text.Json.Serialization;

namespace FoundryBrowserControl.Host.Models;

/// <summary>
/// Envelope for all messages between the extension and the native host.
/// </summary>
public sealed class NativeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}

/// <summary>
/// Strongly-typed message with a known payload type.
/// </summary>
public sealed class NativeMessage<T>
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public T? Payload { get; set; }

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }
}
