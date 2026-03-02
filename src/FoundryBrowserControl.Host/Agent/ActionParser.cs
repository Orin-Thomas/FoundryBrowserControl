using System.Text.Json;
using FoundryBrowserControl.Host.Models;

namespace FoundryBrowserControl.Host.Agent;

/// <summary>
/// Parses LLM text output into a structured BrowserAction.
/// Handles JSON extraction from potentially noisy LLM responses.
/// </summary>
public static class ActionParser
{
    /// <summary>
    /// Attempts to parse a BrowserAction from the LLM's response text.
    /// </summary>
    public static BrowserAction? Parse(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
            return null;

        // Try direct parse first
        var action = TryDeserialize(llmResponse.Trim());
        if (action != null)
            return action;

        // Try to extract JSON from markdown code blocks
        var jsonBlock = ExtractJsonBlock(llmResponse);
        if (jsonBlock != null)
        {
            action = TryDeserialize(jsonBlock);
            if (action != null)
                return action;
        }

        // Try to find any JSON object in the response
        var jsonObject = ExtractFirstJsonObject(llmResponse);
        if (jsonObject != null)
        {
            action = TryDeserialize(jsonObject);
            if (action != null)
                return action;
        }

        return null;
    }

    private static BrowserAction? TryDeserialize(string json)
    {
        try
        {
            var action = JsonSerializer.Deserialize<BrowserAction>(json);
            return string.IsNullOrEmpty(action?.Action) ? null : action;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonBlock(string text)
    {
        var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            start = text.IndexOf("```", StringComparison.Ordinal);
        if (start < 0)
            return null;

        start = text.IndexOf('\n', start);
        if (start < 0)
            return null;
        start++;

        var end = text.IndexOf("```", start, StringComparison.Ordinal);
        if (end < 0)
            return null;

        return text[start..end].Trim();
    }

    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}') depth--;

            if (depth == 0)
                return text[start..(i + 1)];
        }

        return null;
    }
}
