using System.Text.Json.Serialization;

namespace FoundryBrowserControl.Host.Models;

/// <summary>
/// An action the agent wants the browser to execute.
/// </summary>
public sealed class BrowserAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("selector")]
    public string? Selector { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("amount")]
    public int? Amount { get; set; }

    [JsonPropertyName("milliseconds")]
    public int? Milliseconds { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("question")]
    public string? Question { get; set; }

    [JsonPropertyName("elementId")]
    public int? ElementId { get; set; }

    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }
}

/// <summary>
/// Result of executing a browser action, sent back from the content script.
/// </summary>
public sealed class ActionResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// Known action type constants.
/// </summary>
public static class ActionTypes
{
    public const string Navigate = "navigate";
    public const string Click = "click";
    public const string Type = "type";
    public const string Read = "read";
    public const string Scroll = "scroll";
    public const string Wait = "wait";
    public const string Extract = "extract";
    public const string Back = "back";
    public const string Complete = "complete";
    public const string AskUser = "ask_user";
}
