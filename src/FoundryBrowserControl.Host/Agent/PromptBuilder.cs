using FoundryBrowserControl.Host.Llm;

namespace FoundryBrowserControl.Host.Agent;

/// <summary>
/// Builds prompts for the LLM agent, including system instructions, page state, and conversation history.
/// </summary>
public static class PromptBuilder
{
    private const string SystemPrompt = """
        You are a browser automation agent. You control a web browser to complete tasks given by the user.

        ## How You Work
        1. You receive the current page state (URL, title, and interactive elements).
        2. You decide what action to take next.
        3. You respond with EXACTLY ONE JSON action object.

        ## Available Actions
        - {"action":"navigate","url":"https://..."} — Navigate to a URL
        - {"action":"click","elementId":N} — Click element by its ID number from the page state
        - {"action":"click","selector":"css-selector"} — Click element by CSS selector
        - {"action":"click","text":"visible text"} — Click element by its visible text
        - {"action":"type","elementId":N,"text":"text to type"} — Type into an input element
        - {"action":"type","selector":"css-selector","text":"text to type"} — Type into input by selector
        - {"action":"read","selector":"css-selector"} — Read text content of an element
        - {"action":"read"} — Read the main text content of the page
        - {"action":"scroll","direction":"down"|"up","amount":500} — Scroll the page (amount in pixels)
        - {"action":"wait","milliseconds":1000} — Wait for a specified time
        - {"action":"wait","selector":"css-selector"} — Wait for an element to appear
        - {"action":"extract","selector":"css-selector","format":"text"|"table"|"list"} — Extract structured data
        - {"action":"back"} — Go back in browser history
        - {"action":"complete","summary":"Description of what was accomplished"} — Task is finished
        - {"action":"ask_user","question":"What would you like me to do?"} — Ask the user for clarification

        ## Rules
        - ALWAYS respond with a single JSON object. No markdown, no explanation outside the JSON.
        - Use the "thinking" field to explain your reasoning: {"action":"click","elementId":3,"thinking":"Clicking the search button to submit the query"}
        - Element IDs from the page state are the most reliable way to target elements.
        - If the page state shows no relevant elements, try scrolling or navigating.
        - If you're stuck after 3 attempts, use "ask_user" to get help.
        - When the task is done, always use "complete" with a summary.
        - Be cautious with sensitive actions (form submissions, purchases). Use "ask_user" to confirm.
        """;

    /// <summary>
    /// Builds the full message list for a chat completion request.
    /// </summary>
    public static List<ChatMessage> Build(
        string userTask,
        string? pageStateJson,
        List<ChatMessage>? conversationHistory = null)
    {
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(SystemPrompt)
        };

        if (conversationHistory != null)
        {
            messages.AddRange(conversationHistory);
        }

        var userContent = $"## Task\n{userTask}";
        if (pageStateJson != null)
        {
            userContent += $"\n\n## Current Page State\n```json\n{pageStateJson}\n```";
        }

        messages.Add(ChatMessage.User(userContent));
        return messages;
    }

    /// <summary>
    /// Builds a follow-up prompt after an action has been executed.
    /// </summary>
    public static ChatMessage BuildFollowUp(string actionResultJson, string? pageStateJson)
    {
        var content = $"## Action Result\n```json\n{actionResultJson}\n```";
        if (pageStateJson != null)
        {
            content += $"\n\n## Updated Page State\n```json\n{pageStateJson}\n```";
        }
        content += "\n\nWhat is the next action? Respond with a single JSON object.";
        return ChatMessage.User(content);
    }
}
