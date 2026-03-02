using System.Diagnostics;
using System.Net.Http;
using System.Text;
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
/// As a last resort, probes common ports via HTTP to find a running Foundry Local instance.
/// </summary>
static async Task<string> DiscoverFoundryEndpointAsync(StreamWriter log)
{
    const string fallback = "http://localhost:5273";

    try
    {
        // Attempt 1: Parse endpoint from 'foundry service status'
        var (output, endpointUrl) = await CheckServiceStatusAsync(log);

        if (endpointUrl != null)
            return endpointUrl;

        // Attempt 2: If service is not running (or status was empty/timed out), try to start it
        if (output.Length == 0 || output.Contains("not running", StringComparison.OrdinalIgnoreCase))
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Foundry Local not running or status unclear, attempting to start...");
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

            await log.WriteLineAsync($"[{DateTime.Now:o}] Service did not start within 30s via CLI polling");
        }

        // Attempt 3: Probe for a running Foundry Local instance via HTTP
        await log.WriteLineAsync($"[{DateTime.Now:o}] CLI discovery failed, probing for Foundry Local via HTTP...");
        var probed = await ProbeForFoundryEndpointAsync(log);
        if (probed != null)
            return probed;

        await log.WriteLineAsync($"[{DateTime.Now:o}] All discovery methods failed, using fallback: {fallback}");
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] Endpoint discovery failed: {ex.Message}, using fallback: {fallback}");
    }

    return fallback;
}

/// <summary>
/// Runs 'foundry service status' and returns the raw output (stdout+stderr) and parsed endpoint URL (if found).
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
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
            return ("", null);

        // Read both stdout and stderr - Foundry CLI may output to either
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = stdout + "\n" + stderr;

        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status stdout: {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr))
            await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status stderr: {stderr.Trim()}");

        // Match any http(s) URL with a port number
        var match = Regex.Match(combined, @"https?://[\w\.\-]+:\d+");
        return (combined, match.Success ? match.Value : null);
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
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
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

/// <summary>
/// Probes for a running Foundry Local instance by querying /v1/models on likely ports.
/// Foundry Local uses dynamic ports, so we scan a range of common ones.
/// </summary>
static async Task<string?> ProbeForFoundryEndpointAsync(StreamWriter log)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    // Check well-known ports and a range where Foundry Local typically binds
    var portsToCheck = new List<int> { 5273 };
    // Foundry Local often uses high ports in the 50000-60000 range
    for (int p = 57670; p <= 57690; p++)
        portsToCheck.Add(p);
    // Also try some other common ranges
    for (int p = 5000; p <= 5010; p++)
        portsToCheck.Add(p);

    // Also try to find ports from netstat
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        if (proc != null)
        {
            var netstatOutput = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            // Find all listening ports on 127.0.0.1 or 0.0.0.0
            foreach (Match m in Regex.Matches(netstatOutput,
                @"(?:127\.0\.0\.1|0\.0\.0\.0):(\d+)\s+.*LISTENING"))
            {
                if (int.TryParse(m.Groups[1].Value, out var port) && port > 1024)
                    portsToCheck.Add(port);
            }
        }
    }
    catch { /* netstat not available, continue with known ports */ }

    var uniquePorts = portsToCheck.Distinct().ToList();
    await log.WriteLineAsync($"[{DateTime.Now:o}] Probing {uniquePorts.Count} ports for Foundry Local...");

    foreach (var port in uniquePorts)
    {
        try
        {
            var url = $"http://127.0.0.1:{port}";
            var response = await http.GetAsync($"{url}/v1/models");
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                if (body.Contains("model") || body.Contains("data"))
                {
                    await log.WriteLineAsync($"[{DateTime.Now:o}] Found Foundry Local via HTTP probe at {url}");
                    return url;
                }
            }
        }
        catch
        {
            // Port not responding, continue
        }
    }

    await log.WriteLineAsync($"[{DateTime.Now:o}] HTTP probe found no Foundry Local instance");
    return null;
}
