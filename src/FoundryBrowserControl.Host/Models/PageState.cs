using System.Text.Json.Serialization;

namespace FoundryBrowserControl.Host.Models;

/// <summary>
/// Snapshot of the current page state captured by the content script.
/// </summary>
public sealed class PageState
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("elements")]
    public List<PageElement> Elements { get; set; } = [];
}

/// <summary>
/// A simplified representation of an interactive DOM element.
/// </summary>
public sealed class PageElement
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("selector")]
    public string Selector { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? InputType { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("ariaLabel")]
    public string? AriaLabel { get; set; }
}
