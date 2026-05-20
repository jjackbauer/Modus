using Modus.Core.Hosting;
using Modus.Core.Plugins;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Modus.Host.Plugins;

var runOnce = args.Any(x => string.Equals(x, "--run-once", StringComparison.OrdinalIgnoreCase));
var pluginsPath = args.FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal))
    ?? Path.Combine(Directory.GetCurrentDirectory(), "plugins");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddModusPluginHosting(opts =>
{
    opts.PluginsPath = pluginsPath;
    opts.RunOnce = runOnce;
});

await using var app = builder.Build();
var provider = app.Services;
var registrationDiagnostics = provider.GetService<PluginDiRegistrationDiagnostics>();

string DescribeSelectedLifetime(string pluginId)
    => registrationDiagnostics?.Entries
        .LastOrDefault(entry => string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal)
            && entry.Outcome == PluginRegistrationOutcome.Success)
        ?.SelectedLifetime?.ToString()
        ?? "unknown";

string DescribeSkipReason(string pluginId)
    => registrationDiagnostics?.Entries
        .LastOrDefault(entry => string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal)
            && entry.Outcome == PluginRegistrationOutcome.Skipped)
        ?.Reason
        ?? "no matching registration";

var pluginContracts = provider.GetServices<IPluginContract>().ToArray();
if (pluginContracts.Length == 0)
{
    Console.WriteLine("stage=di outcome=failure reason=no plugin contracts resolved from host provider");
}
else
{
    var pluginIds = string.Join(",", pluginContracts.Select(static x => x.PluginId.Value).OrderBy(static x => x, StringComparer.Ordinal));
    Console.WriteLine($"stage=di outcome=success pluginContracts={pluginContracts.Length} pluginIds={pluginIds}");
}

var hostTelemetryContracts = provider.GetServices<IHostTelemetryPluginContract>().ToArray();
Console.WriteLine(hostTelemetryContracts.Length > 0
    ? $"stage=di outcome=success contract={nameof(IHostTelemetryPluginContract)} resolvedCount={hostTelemetryContracts.Length} selectedLifetime={DescribeSelectedLifetime("Plugin.Host.Telemetry")}" 
    : $"stage=di outcome=failure contract={nameof(IHostTelemetryPluginContract)} resolvedCount=0 reason={DescribeSkipReason("Plugin.Host.Telemetry")}");

var machineTelemetryContracts = provider.GetServices<IMachineTelemetryPluginContract>().ToArray();
Console.WriteLine(machineTelemetryContracts.Length > 0
    ? $"stage=di outcome=success contract={nameof(IMachineTelemetryPluginContract)} resolvedCount={machineTelemetryContracts.Length} selectedLifetime={DescribeSelectedLifetime("Plugin.Machine.Telemetry")}" 
    : $"stage=di outcome=failure contract={nameof(IMachineTelemetryPluginContract)} resolvedCount=0 reason={DescribeSkipReason("Plugin.Machine.Telemetry")}");

Console.WriteLine(hostTelemetryContracts.Any(static x => string.Equals(x.PluginId.Value, "Plugin.Host.Telemetry", StringComparison.Ordinal))
    ? "stage=di outcome=success resolved=Plugin.Host.Telemetry"
    : "stage=di outcome=failure missing=Plugin.Host.Telemetry");

Console.WriteLine(machineTelemetryContracts.Any(static x => string.Equals(x.PluginId.Value, "Plugin.Machine.Telemetry", StringComparison.Ordinal))
    ? "stage=di outcome=success resolved=Plugin.Machine.Telemetry"
    : "stage=di outcome=failure missing=Plugin.Machine.Telemetry");

var runner = provider.GetRequiredService<HostRunner>();
var start = await runner.StartAsync(pluginsPath, CancellationToken.None);

foreach (var diagnostic in start.Diagnostics)
{
    Console.WriteLine(diagnostic);
}

if (!start.HostHealthy)
{
    return 1;
}

// Register plugin operation endpoints on the WebApplication
var mapper = provider.GetRequiredService<PluginEndpointMapper>();
mapper.Map(app);

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Modus Host API v1");
    options.RoutePrefix = "swagger";
});

if (runOnce)
{
    return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { shutdown.Cancel(); }
    catch (ObjectDisposedException) { }
};

try
{
    await app.RunAsync(shutdown.Token);
}
catch (OperationCanceledException)
{
}

return 0;
