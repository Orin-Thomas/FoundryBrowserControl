using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoundryBrowserControl.Host.Llm;

/// <summary>
/// Client for Foundry Local's OpenAI-compatible chat completions API.
/// </summary>
public sealed class FoundryLocalClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;

    public FoundryLocalClient(string endpoint, string model)
    {
        _model = model;
        _http = new HttpClient
        {
            BaseAddress = new Uri(endpoint.TrimEnd('/')),
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <summary>
    /// Sends a chat completion request and returns the assistant's reply.
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        var request = new ChatCompletionRequest
        {
            Model = _model,
            Messages = messages,
            Temperature = 0.2,
            MaxTokens = 2048
        };

        var response = await _http.PostAsJsonAsync("/v1/chat/completions", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: ct);
        return result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
    }

    public void Dispose() => _http.Dispose();
}

#region API DTOs

public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public static ChatMessage System(string content) => new() { Role = "system", Content = content };
    public static ChatMessage User(string content) => new() { Role = "user", Content = content };
    public static ChatMessage Assistant(string content) => new() { Role = "assistant", Content = content };
}

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.2;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 2048;
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<Choice>? Choices { get; set; }
}

public sealed class Choice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}

#endregion
