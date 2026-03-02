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

    // Foundry Local endpoint — discover dynamically or use default
    var endpoint = Environment.GetEnvironmentVariable("FOUNDRY_ENDPOINT")
                   ?? "http://localhost:5273";
    var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL")
                ?? "phi-4-mini";

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
