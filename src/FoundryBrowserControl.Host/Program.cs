using System.Diagnostics;
using System.Text.RegularExpressions;
using FoundryBrowserControl.Host.Agent;
using FoundryBrowserControl.Host.Llm;
using FoundryBrowserControl.Host.NativeMessaging;

// Native messaging hosts must not write to stderr (it goes to the browser's debug log).
// Redirect stderr to a log file for diagnostics.
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FoundryBrowserControl",
    "host.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
var logStream = new StreamWriter(logPath, append: true) { AutoFlush = true };

try
{
    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Host started");

    var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
                ?? "phi-4-mini";

    // Discover Foundry Local endpoint: env var > foundry service status > default
    var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");
    if (string.IsNullOrEmpty(endpoint))
    {
        endpoint = await DiscoverFoundryEndpointAsync(logStream);
    }

    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Using endpoint: {endpoint}, model: {model}");

    using var reader = new NativeMessageReader();
    using var writer = new NativeMessageWriter();
    using var llmClient = new FoundryLocalClient(endpoint, model);

    var agent = new BrowserAgent(reader, writer, llmClient);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await agent.RunAsync(cts.Token);
}
catch (Exception ex)
{
    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Fatal error: {ex}");
    throw;
}
finally
{
    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Host exiting");
    logStream.Dispose();
}

/// <summary>
/// Discovers the Foundry Local endpoint by running 'foundry service status' and parsing the output.
/// Falls back to http://localhost:5273 if discovery fails.
/// </summary>
static async Task<string> DiscoverFoundryEndpointAsync(StreamWriter log)
{
    const string fallback = "http://localhost:5273";

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "foundry",
            Arguments = "service status",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Could not start 'foundry service status', using fallback: {fallback}");
            return fallback;
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status output: {output.Trim()}");

        // Look for a URL pattern like http://localhost:NNNNN or http://127.0.0.1:NNNNN
        var match = Regex.Match(output, @"https?://(?:localhost|127\.0\.0\.1):\d+");
        if (match.Success)
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Discovered endpoint: {match.Value}");
            return match.Value;
        }

        await log.WriteLineAsync($"[{DateTime.Now:o}] No endpoint found in output, using fallback: {fallback}");
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] Endpoint discovery failed: {ex.Message}, using fallback: {fallback}");
    }

    return fallback;
}
