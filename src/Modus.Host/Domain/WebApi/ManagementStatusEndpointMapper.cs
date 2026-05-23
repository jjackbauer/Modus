using Microsoft.AspNetCore.Builder;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

internal sealed class ManagementStatusEndpointMapper
{
    private readonly HostStatusRegistry _hostStatusRegistry;

    public ManagementStatusEndpointMapper(HostStatusRegistry hostStatusRegistry)
    {
        _hostStatusRegistry = hostStatusRegistry ?? throw new ArgumentNullException(nameof(hostStatusRegistry));
    }

    public WebApplication Map(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/management/status",
            () => Results.Ok(_hostStatusRegistry.GetCurrent()))
            .WithName("ManagementStatus_Get")
            .WithTags("Management")
            .WithSummary("Get current host runtime status")
            .WithDescription("Returns the current host lifecycle state, loaded plugin inventory, capability ownership, and startup diagnostics.");

        return app;
    }
}