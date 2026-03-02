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
    private string _model;
    private bool _modelResolved;

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
    /// Tests connectivity by querying /v1/models. Returns the list of available model IDs.
    /// Also resolves the actual model ID if it hasn't been resolved yet.
    /// </summary>
    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var response = await _http.GetAsync("/v1/models", cts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cts.Token);
        using var doc = JsonDocument.Parse(json);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                    models.Add(id.GetString() ?? "");
            }
        }

        // Auto-resolve model name to match what Foundry Local actually has loaded
        if (!_modelResolved && models.Count > 0)
        {
            var exact = models.FirstOrDefault(m =>
                m.Equals(_model, StringComparison.OrdinalIgnoreCase));
            var partial = models.FirstOrDefault(m =>
                m.Contains(_model, StringComparison.OrdinalIgnoreCase));

            if (exact != null)
                _model = exact;
            else if (partial != null)
                _model = partial;
            else
                _model = models[0]; // Use whatever is loaded

            _modelResolved = true;
        }

        return models;
    }

    /// <summary>
    /// Returns the resolved model name (after ListModelsAsync has been called).
    /// </summary>
    public string ResolvedModel => _model;

    /// <summary>
    /// Sends a chat completion request and returns the assistant's reply.
    /// </summary>
    public async Task<string> ChatAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        // Ensure model name is resolved before first chat
        if (!_modelResolved)
        {
            try { await ListModelsAsync(ct); }
            catch { /* proceed with configured name */ }
        }

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
