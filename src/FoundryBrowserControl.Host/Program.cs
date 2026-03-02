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
/// If the service is not running, starts it automatically and waits for it to become available.
/// Falls back to http://localhost:5273 if all attempts fail.
/// </summary>
static async Task<string> DiscoverFoundryEndpointAsync(StreamWriter log)
{
    const string fallback = "http://localhost:5273";

    try
    {
        var (output, endpointUrl) = await CheckServiceStatusAsync(log);

        if (endpointUrl != null)
            return endpointUrl;

        // Service is not running - attempt to start it
        if (output.Contains("not running", StringComparison.OrdinalIgnoreCase))
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Foundry Local not running, attempting to start...");
            await StartFoundryServiceAsync(log);

            // Poll for the service to become available (up to 30 seconds)
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(3000);
                var (_, url) = await CheckServiceStatusAsync(log);
                if (url != null)
                {
                    await log.WriteLineAsync($"[{DateTime.Now:o}] Service started successfully after {(i + 1) * 3}s");
                    return url;
                }
            }

            await log.WriteLineAsync($"[{DateTime.Now:o}] Service did not start within 30s, using fallback: {fallback}");
        }
        else
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] No endpoint found in output, using fallback: {fallback}");
        }
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] Endpoint discovery failed: {ex.Message}, using fallback: {fallback}");
    }

    return fallback;
}

/// <summary>
/// Runs 'foundry service status' and returns the raw output and parsed endpoint URL (if found).
/// </summary>
static async Task<(string output, string? endpointUrl)> CheckServiceStatusAsync(StreamWriter log)
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
            return ("", null);

        var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status: {output.Trim()}");

        var match = Regex.Match(output, @"https?://(?:localhost|127\.0\.0\.1):\d+");
        return (output, match.Success ? match.Value : null);
    }
    catch (OperationCanceledException)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status timed out");
        return ("", null);
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status error: {ex.Message}");
        return ("", null);
    }
}

/// <summary>
/// Starts the Foundry Local service in the background.
/// </summary>
static async Task StartFoundryServiceAsync(StreamWriter log)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "foundry",
            Arguments = "service start",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Could not start 'foundry service start'");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service start output: {output.Trim()}");
    }
    catch (OperationCanceledException)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service start timed out (may still be starting in background)");
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service start error: {ex.Message}");
    }
}
