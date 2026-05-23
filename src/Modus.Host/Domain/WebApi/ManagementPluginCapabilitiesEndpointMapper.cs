using Microsoft.AspNetCore.Builder;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

internal sealed class ManagementPluginCapabilitiesEndpointMapper
{
    private readonly HostStatusRegistry _hostStatusRegistry;
    private readonly RuntimePluginRegistry _runtimePluginRegistry;

    public ManagementPluginCapabilitiesEndpointMapper(
        HostStatusRegistry hostStatusRegistry,
        RuntimePluginRegistry runtimePluginRegistry)
    {
        _hostStatusRegistry = hostStatusRegistry ?? throw new ArgumentNullException(nameof(hostStatusRegistry));
        _runtimePluginRegistry = runtimePluginRegistry ?? throw new ArgumentNullException(nameof(runtimePluginRegistry));
    }

    public WebApplication Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/management/plugins/capabilities",
            () => Results.Ok(ManagementPluginCapabilitiesEndpointResponse.FromStatus(
                _hostStatusRegistry.GetCurrent(),
                _runtimePluginRegistry.GetSnapshot())))
            .WithName("ManagementPluginCapabilities_Get")
            .WithTags("Management")
            .WithSummary("Get plugin capability catalog")
            .WithDescription("Returns the runtime capability ownership mapping and plugin capability catalog.");

        return app;
    }
}