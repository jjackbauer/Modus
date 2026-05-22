using Microsoft.AspNetCore.Builder;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

internal sealed class ManagementPluginCapabilitiesEndpointMapper
{
    private readonly HostStatusRegistry _hostStatusRegistry;

    public ManagementPluginCapabilitiesEndpointMapper(HostStatusRegistry hostStatusRegistry)
    {
        _hostStatusRegistry = hostStatusRegistry ?? throw new ArgumentNullException(nameof(hostStatusRegistry));
    }

    public WebApplication Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/management/plugins/capabilities",
            () => Results.Ok(ManagementPluginCapabilitiesEndpointResponse.FromStatus(_hostStatusRegistry.GetCurrent())))
            .WithName("ManagementPluginCapabilities_Get")
            .WithOpenApi()
            .WithTags("Management")
            .WithSummary("Get plugin capability catalog")
            .WithDescription("Returns the runtime capability ownership mapping and plugin capability catalog.");

        return app;
    }
}