using System.Text.Json;
using FoundryBrowserControl.Host.Llm;
using FoundryBrowserControl.Host.Models;
using FoundryBrowserControl.Host.NativeMessaging;

namespace FoundryBrowserControl.Host.Agent;

/// <summary>
/// The main agent loop. Orchestrates communication between the extension and the LLM.
/// </summary>
public sealed class BrowserAgent
{
    private readonly NativeMessageReader _reader;
    private readonly NativeMessageWriter _writer;
    private readonly FoundryLocalClient _llm;
    private readonly List<ChatMessage> _conversationHistory = [];

    private const int MaxSteps = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BrowserAgent(NativeMessageReader reader, NativeMessageWriter writer, FoundryLocalClient llm)
    {
        _reader = reader;
        _writer = writer;
        _llm = llm;
    }

    /// <summary>
    /// Main loop: reads messages from the extension and processes them.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var message = await _reader.ReadAsync<NativeMessage>(ct);
            if (message == null)
                break; // Stream closed

            switch (message.Type)
            {
                case "task":
                    var task = message.Payload?.ToString() ?? string.Empty;
                    await HandleTaskAsync(task, ct);
                    break;

                case "stop":
                    _conversationHistory.Clear();
                    await SendStatusAsync("stopped", "Task stopped by user.", ct);
                    break;

                case "clear":
                    _conversationHistory.Clear();
                    await SendStatusAsync("cleared", "Conversation cleared.", ct);
                    break;

                case "user_response":
                    var response = message.Payload?.ToString() ?? string.Empty;
                    await HandleUserResponseAsync(response, ct);
                    break;

                case "health_check":
                    await HandleHealthCheckAsync(ct);
                    break;

                default:
                    await SendStatusAsync("error", $"Unknown message type: {message.Type}", ct);
                    break;
            }
        }
    }

    private async Task HandleHealthCheckAsync(CancellationToken ct)
    {
        // Test connectivity to Foundry Local with a lightweight /v1/models call
        try
        {
            var models = await _llm.ListModelsAsync(ct);
            var ok = models.Count > 0;

            await _writer.WriteAsync(new NativeMessage
            {
                Type = "health_check_result",
                Payload = new
                {
                    nativeHost = true,
                    foundryLocal = ok,
                    model = _llm.ResolvedModel,
                    availableModels = models,
                    error = ok ? (string?)null : "No models loaded in Foundry Local"
                }
            }, ct);
        }
        catch (Exception ex)
        {
            await _writer.WriteAsync(new NativeMessage
            {
                Type = "health_check_result",
                Payload = new { nativeHost = true, foundryLocal = false, error = ex.Message }
            }, ct);
        }
    }

    private async Task HandleTaskAsync(string userTask, CancellationToken ct)
    {
        _conversationHistory.Clear();
        await SendStatusAsync("thinking", "Starting task...", ct);

        // Request page state from the extension
        var pageState = await RequestPageStateAsync(ct);
        var pageStateJson = pageState != null ? JsonSerializer.Serialize(pageState, JsonOptions) : null;

        // Build initial prompt
        var messages = PromptBuilder.Build(userTask, pageStateJson, _conversationHistory);

        for (var step = 0; step < MaxSteps; step++)
        {
            // Call LLM
            await SendStatusAsync("thinking", $"Thinking... (step {step + 1})", ct);
            string llmResponse;
            try
            {
                llmResponse = await _llm.ChatAsync(messages, ct);
            }
            catch (Exception ex)
            {
                await SendStatusAsync("error", $"LLM error: {ex.Message}", ct);
                return;
            }

            // Parse action
            var action = ActionParser.Parse(llmResponse);
            if (action == null)
            {
                await SendStatusAsync("error", $"Could not parse LLM response: {llmResponse}", ct);
                return;
            }

            // Store in conversation history
            _conversationHistory.Add(ChatMessage.Assistant(llmResponse));

            // Send thinking to sidebar
            if (!string.IsNullOrEmpty(action.Thinking))
            {
                await SendStatusAsync("thinking", action.Thinking, ct);
            }

            // Handle complete action
            if (action.Action == ActionTypes.Complete)
            {
                await SendStatusAsync("complete", action.Summary ?? "Task completed.", ct);
                return;
            }

            // Handle ask_user action
            if (action.Action == ActionTypes.AskUser)
            {
                await SendToExtensionAsync("ask_user", action.Question ?? "What would you like me to do?", ct);
                return; // Wait for user_response message
            }

            // Send action to extension for execution
            await SendStatusAsync("acting", $"Executing: {action.Action}", ct);
            var result = await ExecuteActionAsync(action, ct);

            // Get updated page state
            pageState = await RequestPageStateAsync(ct);
            pageStateJson = pageState != null ? JsonSerializer.Serialize(pageState, JsonOptions) : null;
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);

            // Build follow-up prompt
            var followUp = PromptBuilder.BuildFollowUp(resultJson, pageStateJson);
            messages.Add(followUp);
            _conversationHistory.Add(followUp);
        }

        await SendStatusAsync("error", "Maximum steps reached. Task may be incomplete.", ct);
    }

    private async Task HandleUserResponseAsync(string response, CancellationToken ct)
    {
        // Continue the conversation with the user's response
        _conversationHistory.Add(ChatMessage.User(response));

        var pageState = await RequestPageStateAsync(ct);
        var pageStateJson = pageState != null ? JsonSerializer.Serialize(pageState, JsonOptions) : null;

        var messages = PromptBuilder.Build(response, pageStateJson, _conversationHistory);

        // Continue the agent loop
        await HandleAgentLoopAsync(messages, ct);
    }

    private async Task HandleAgentLoopAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        for (var step = 0; step < MaxSteps; step++)
        {
            await SendStatusAsync("thinking", $"Thinking... (step {step + 1})", ct);
            string llmResponse;
            try
            {
                llmResponse = await _llm.ChatAsync(messages, ct);
            }
            catch (Exception ex)
            {
                await SendStatusAsync("error", $"LLM error: {ex.Message}", ct);
                return;
            }

            var action = ActionParser.Parse(llmResponse);
            if (action == null)
            {
                await SendStatusAsync("error", $"Could not parse LLM response: {llmResponse}", ct);
                return;
            }

            _conversationHistory.Add(ChatMessage.Assistant(llmResponse));

            if (!string.IsNullOrEmpty(action.Thinking))
            {
                await SendStatusAsync("thinking", action.Thinking, ct);
            }

            if (action.Action == ActionTypes.Complete)
            {
                await SendStatusAsync("complete", action.Summary ?? "Task completed.", ct);
                return;
            }

            if (action.Action == ActionTypes.AskUser)
            {
                await SendToExtensionAsync("ask_user", action.Question ?? "What would you like me to do?", ct);
                return;
            }

            await SendStatusAsync("acting", $"Executing: {action.Action}", ct);
            var result = await ExecuteActionAsync(action, ct);

            var pageState = await RequestPageStateAsync(ct);
            var pageStateJson = pageState != null ? JsonSerializer.Serialize(pageState, JsonOptions) : null;
            var resultJson = JsonSerializer.Serialize(result, JsonOptions);

            var followUp = PromptBuilder.BuildFollowUp(resultJson, pageStateJson);
            messages.Add(followUp);
            _conversationHistory.Add(followUp);
        }

        await SendStatusAsync("error", "Maximum steps reached. Task may be incomplete.", ct);
    }

    private async Task<PageState?> RequestPageStateAsync(CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        await _writer.WriteAsync(new NativeMessage
        {
            Type = "get_page_state",
            RequestId = requestId
        }, ct);

        // Wait for the page state response
        var response = await _reader.ReadAsync<NativeMessage>(ct);
        if (response?.Type == "page_state" && response.Payload != null)
        {
            var json = response.Payload.ToString()!;
            return JsonSerializer.Deserialize<PageState>(json);
        }
        return null;
    }

    private async Task<ActionResult> ExecuteActionAsync(BrowserAction action, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        await _writer.WriteAsync(new NativeMessage
        {
            Type = "execute_action",
            Payload = action,
            RequestId = requestId
        }, ct);

        // Wait for the action result
        var response = await _reader.ReadAsync<NativeMessage>(ct);
        if (response?.Type == "action_result" && response.Payload != null)
        {
            var json = response.Payload.ToString()!;
            return JsonSerializer.Deserialize<ActionResult>(json) ?? new ActionResult
            {
                Success = false,
                Error = "Failed to deserialize action result"
            };
        }

        return new ActionResult { Success = false, Error = "No response from extension" };
    }

    private async Task SendStatusAsync(string status, string message, CancellationToken ct)
    {
        await _writer.WriteAsync(new NativeMessage
        {
            Type = "status",
            Payload = new { status, message }
        }, ct);
    }

    private async Task SendToExtensionAsync(string type, object payload, CancellationToken ct)
    {
        await _writer.WriteAsync(new NativeMessage
        {
            Type = type,
            Payload = payload
        }, ct);
    }
}
