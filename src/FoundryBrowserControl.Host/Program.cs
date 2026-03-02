using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FoundryBrowserControl.Host.Agent;
using FoundryBrowserControl.Host.Llm;
using FoundryBrowserControl.Host.Transport;

var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FoundryBrowserControl",
    "host.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
var logStream = new StreamWriter(logPath, append: true) { AutoFlush = true };

try
{
    var port = int.TryParse(Environment.GetEnvironmentVariable("BROWSER_CONTROL_PORT"), out var p) ? p : 52945;
    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Host starting on ws://localhost:{port}/ws");

    var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL") ?? "phi-4-mini";

    // Discover Foundry Local endpoint
    var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT");
    if (string.IsNullOrEmpty(endpoint))
    {
        endpoint = await DiscoverFoundryEndpointAsync(logStream);
    }

    await logStream.WriteLineAsync($"[{DateTime.Now:o}] Using Foundry endpoint: {endpoint}, model: {model}");

    using var llmClient = new FoundryLocalClient(endpoint, model);

    // Pre-resolve model name
    try
    {
        var models = await llmClient.ListModelsAsync(CancellationToken.None);
        await logStream.WriteLineAsync($"[{DateTime.Now:o}] Available models: {string.Join(", ", models)}");
        await logStream.WriteLineAsync($"[{DateTime.Now:o}] Resolved model: {llmClient.ResolvedModel}");
    }
    catch (Exception ex)
    {
        await logStream.WriteLineAsync($"[{DateTime.Now:o}] Model resolution failed: {ex.Message}");
    }

    // Start WebSocket server
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    listener.Start();
    await logStream.WriteLineAsync($"[{DateTime.Now:o}] WebSocket server listening on http://localhost:{port}/");

    Console.WriteLine($"Foundry Browser Control host running on ws://localhost:{port}/ws");
    Console.WriteLine("Press Ctrl+C to stop.");

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    while (!cts.IsCancellationRequested)
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        // Handle health endpoint for quick checks
        if (context.Request.RawUrl == "/health")
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            var health = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            await context.Response.OutputStream.WriteAsync(health);
            context.Response.Close();
            continue;
        }

        // Handle WebSocket upgrade
        if (context.Request.IsWebSocketRequest)
        {
            await logStream.WriteLineAsync($"[{DateTime.Now:o}] WebSocket client connected from {context.Request.RemoteEndPoint}");
            _ = HandleWebSocketClientAsync(context, llmClient, logStream, cts.Token);
        }
        else
        {
            // Return a helpful message for non-WebSocket requests
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/plain";
            var msg = Encoding.UTF8.GetBytes(
                $"Foundry Browser Control Host\nWebSocket: ws://localhost:{port}/ws\nHealth: http://localhost:{port}/health\n");
            await context.Response.OutputStream.WriteAsync(msg);
            context.Response.Close();
        }
    }
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

static async Task HandleWebSocketClientAsync(
    HttpListenerContext context,
    FoundryLocalClient llmClient,
    StreamWriter log,
    CancellationToken ct)
{
    try
    {
        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
        await using var transport = new WebSocketTransport(wsContext.WebSocket);
        var agent = new BrowserAgent(transport, llmClient);

        await log.WriteLineAsync($"[{DateTime.Now:o}] Agent session started");
        await agent.RunAsync(ct);
        await log.WriteLineAsync($"[{DateTime.Now:o}] Agent session ended");
    }
    catch (Exception ex)
    {
        await log.WriteLineAsync($"[{DateTime.Now:o}] WebSocket session error: {ex.Message}");
    }
}

// -------------------------------------------------------
// Foundry Local endpoint discovery (unchanged logic)
// -------------------------------------------------------

static async Task<string> DiscoverFoundryEndpointAsync(StreamWriter log)
{
    const string fallback = "http://localhost:5273";

    try
    {
        var (output, endpointUrl) = await CheckServiceStatusAsync(log);

        if (endpointUrl != null)
            return endpointUrl;

        if (output.Length == 0 || output.Contains("not running", StringComparison.OrdinalIgnoreCase))
        {
            await log.WriteLineAsync($"[{DateTime.Now:o}] Foundry Local not running or status unclear, attempting to start...");
            await StartFoundryServiceAsync(log);

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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var combined = stdout + "\n" + stderr;

        await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status stdout: {stdout.Trim()}");
        if (!string.IsNullOrWhiteSpace(stderr))
            await log.WriteLineAsync($"[{DateTime.Now:o}] foundry service status stderr: {stderr.Trim()}");

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

static async Task<string?> ProbeForFoundryEndpointAsync(StreamWriter log)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

    var portsToCheck = new List<int> { 5273 };
    for (int p = 57670; p <= 57690; p++)
        portsToCheck.Add(p);
    for (int p = 5000; p <= 5010; p++)
        portsToCheck.Add(p);

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
            foreach (Match m in Regex.Matches(netstatOutput,
                @"(?:127\.0\.0\.1|0\.0\.0\.0):(\d+)\s+.*LISTENING"))
            {
                if (int.TryParse(m.Groups[1].Value, out var port) && port > 1024)
                    portsToCheck.Add(port);
            }
        }
    }
    catch { /* netstat not available */ }

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
        catch { }
    }

    await log.WriteLineAsync($"[{DateTime.Now:o}] HTTP probe found no Foundry Local instance");
    return null;
}
