using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Modus.Core.Hosting;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

[Trait("MigrationRegression", "true")]
public sealed class PluginFolderWatcherRuntimeRegistryTests
{
    [Fact]
    [Trait("ChecklistItem", "Verify folder onboarding integration flow: create in-scope project, process watcher event, assert runtime snapshot contract/catalog mutation, then assert `/api/{pluginId}/{operation}` dispatch success [mandatory - folder hot-load assurance]")]
    [Trait("AuditArtifact", "iterative-implementation-folder-hot-load-assurance-2026-05-22")]
    public async Task PluginFolderWatcher_GivenInScopeProjectCreated_ExpectedSnapshotMutationAndApiDispatchSuccess()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-runtime-dispatch-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            using var provider = CreateProvider();
            var watcher = provider.GetRequiredService<PluginFolderWatcher>();
            var registry = provider.GetRequiredService<RuntimePluginRegistry>();

            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Dispatch.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Orders.Accept</ModusOperations>" +
                $"<ModusRuntimePluginType>{typeof(FolderHotLoadResponder).FullName}</ModusRuntimePluginType><ModusServiceLifetime>Singleton</ModusServiceLifetime></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            var snapshot = registry.GetSnapshot();

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated, string.Join(" | ", onboarding.Diagnostics));
            Assert.Equal(new PluginId("Plugin.Runtime.Dispatch"), onboarding.PluginId);
            Assert.Contains(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Dispatch", StringComparison.Ordinal));
            Assert.Contains(
                snapshot.Catalogs,
                x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Dispatch", StringComparison.Ordinal)
                    && x.SupportedOperations.Contains(new OperationName("Orders.Accept")));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton<FolderHotLoadResponder>();

            await using var app = builder.Build();
            new PluginEndpointMapper(registry).Map(app);
            await app.StartAsync();

            var client = app.GetTestClient();
            var httpResponse = await client.PostAsJsonAsync(
                "/api/Plugin.Runtime.Dispatch/Orders.Accept",
                new PluginOperationHttpRequest { CorrelationId = "folder-hot-load" });
            var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
            Assert.NotNull(response);
            Assert.True(response!.Success);
            Assert.Equal(SyncResponseStatus.Success, response.Status);
            Assert.Equal("folder-hot-load", response.CorrelationId);
            Assert.True(PluginOperationPayload.Contains(response.Payload, "folder-hot-load-dispatch-ok", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Update PluginFolderWatcher onboarding flow to publish add/remove changes into RuntimePluginRegistry [depends on runtime registry abstraction]")]
    public void PluginFolderWatcher_GivenValidPluginOnboarded_ExpectedRegistryUpdatedAndDiagnosticEmitted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-runtime-add-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            using var provider = CreateProvider();
            var watcher = provider.GetRequiredService<PluginFolderWatcher>();
            var registry = provider.GetRequiredService<RuntimePluginRegistry>();

            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Added.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Orders.Accept</ModusOperations></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            var snapshot = registry.GetSnapshot();

            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated);
            Assert.Equal(new PluginId("Plugin.Runtime.Added"), onboarding.PluginId);
            Assert.Contains(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Added", StringComparison.Ordinal));
            Assert.Contains(
                snapshot.Catalogs,
                x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Added", StringComparison.Ordinal)
                    && x.SupportedOperations.Contains(new OperationName("Orders.Accept")));
            Assert.Contains(
                onboarding.Diagnostics,
                x => x.Contains($"outcome=accepted path={Path.GetFullPath(projectPath)}", StringComparison.Ordinal));
            Assert.Contains(
                onboarding.Diagnostics,
                x => x.Contains("stage=registry-update outcome=success", StringComparison.Ordinal)
                    && x.Contains("added=Plugin.Runtime.Added", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Verify folder offboarding integration flow: delete onboarded project, process watcher event, assert runtime snapshot eviction and deterministic unload diagnostics [depends on plugin unload path]")]
    [Trait("ChecklistItem", "Add endpoint integration proofs for owner uniqueness, business payload semantics, correlation continuity, and deterministic isolation after failed load/remove [depends on DI lifetime integration proofs]")]
    [Trait("AuditArtifact", "iterative-implementation-folder-offboarding-assurance-2026-05-22")]
    public async Task PluginFolderWatcher_GivenOnboardedProjectDeleted_ExpectedSnapshotEvictionDiagnosticsAndDispatchMiss()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-watcher-runtime-remove-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            using var provider = CreateProvider();
            var watcher = provider.GetRequiredService<PluginFolderWatcher>();
            var registry = provider.GetRequiredService<RuntimePluginRegistry>();

            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Removed.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Orders.Cancel</ModusOperations>" +
                $"<ModusRuntimePluginType>{typeof(FolderOffboardResponder).FullName}</ModusRuntimePluginType><ModusServiceLifetime>Singleton</ModusServiceLifetime></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            Assert.True(onboarding.PluginActivated);
            Assert.Contains(registry.GetSnapshot().Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(registry);
            builder.Services.AddSingleton<FolderOffboardResponder>();

            await using var app = builder.Build();
            new PluginEndpointMapper(registry).Map(app);
            await app.StartAsync();

            var client = app.GetTestClient();
            var preDeleteHttpResponse = await client.PostAsJsonAsync(
                "/api/Plugin.Runtime.Removed/Orders.Cancel",
                new PluginOperationHttpRequest { CorrelationId = "folder-offboard-pre" });
            var preDeleteResponse = await preDeleteHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

            Assert.Equal(HttpStatusCode.OK, preDeleteHttpResponse.StatusCode);
            Assert.NotNull(preDeleteResponse);
            Assert.True(preDeleteResponse!.Success);
            Assert.Equal(SyncResponseStatus.Success, preDeleteResponse.Status);
            Assert.Equal("folder-offboard-pre", preDeleteResponse.CorrelationId);
            Assert.True(PluginOperationPayload.Contains(preDeleteResponse.Payload, "folder-offboard-dispatch-ok", StringComparison.Ordinal));

            File.Delete(projectPath);
            var offboarding = watcher.OnProjectDeleted(projectPath);
            var snapshot = registry.GetSnapshot();

            Assert.True(offboarding.HostHealthy);
            Assert.True(offboarding.EventAccepted);
            Assert.Equal(new PluginId("Plugin.Runtime.Removed"), offboarding.PluginId);
            Assert.DoesNotContain(snapshot.Contracts, x => string.Equals(x.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));
            Assert.DoesNotContain(snapshot.Catalogs, x => string.Equals((x as IPluginContract)?.PluginId.Value, "Plugin.Runtime.Removed", StringComparison.Ordinal));
            Assert.Contains(
                offboarding.Diagnostics,
                x => x.Contains("phase=deactivating", StringComparison.Ordinal)
                    && x.Contains("plugin=Plugin.Runtime.Removed", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal));
            Assert.Contains(
                offboarding.Diagnostics,
                x => x.Contains("phase=unloaded", StringComparison.Ordinal)
                    && x.Contains("plugin=Plugin.Runtime.Removed", StringComparison.Ordinal)
                    && x.Contains("outcome=success", StringComparison.Ordinal));
            Assert.Contains(
                offboarding.Diagnostics,
                x => x.Contains("stage=registry-update outcome=success", StringComparison.Ordinal)
                    && x.Contains("removed=Plugin.Runtime.Removed", StringComparison.Ordinal));

            var postDeleteHttpResponse = await client.PostAsJsonAsync(
                "/api/Plugin.Runtime.Removed/Orders.Cancel",
                new PluginOperationHttpRequest { CorrelationId = "folder-offboard-post" });
            var postDeleteResponse = await postDeleteHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

            Assert.Equal(HttpStatusCode.InternalServerError, postDeleteHttpResponse.StatusCode);
            Assert.NotNull(postDeleteResponse);
            Assert.False(postDeleteResponse!.Success);
            Assert.Equal(SyncResponseStatus.Failed, postDeleteResponse.Status);
            Assert.Equal("folder-offboard-post", postDeleteResponse.CorrelationId);
            Assert.True(PluginOperationPayload.Contains(postDeleteResponse.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PluginHostingOptions());
        services.AddModusPluginHostingRuntime();
        return services.BuildServiceProvider();
    }
}

internal sealed class FolderHotLoadResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
{
    public PluginId PluginId => new("Plugin.Runtime.Dispatch");

    public ContractName ContractName => new("Folder.HotLoad");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("Orders.Accept")];

    public SyncResponse Handle(SyncRequest request)
    {
        return new SyncResponse(
            Success: true,
            Payload: "folder-hot-load-dispatch-ok",
            Status: SyncResponseStatus.Success,
            CorrelationId: request.CorrelationId);
    }
}

internal sealed class FolderOffboardResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
{
    public PluginId PluginId => new("Plugin.Runtime.Removed");

    public ContractName ContractName => new("Folder.Offboard");

    public Version ContractVersion => new(1, 0, 0);

    public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName("Orders.Cancel")];

    public SyncResponse Handle(SyncRequest request)
    {
        return new SyncResponse(
            Success: true,
            Payload: "folder-offboard-dispatch-ok",
            Status: SyncResponseStatus.Success,
            CorrelationId: request.CorrelationId);
    }
}