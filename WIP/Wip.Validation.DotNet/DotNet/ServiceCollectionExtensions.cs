using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Capabilities;
using Wip.Workspaces.Git;

namespace Wip.Validation.DotNet.DotNet;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWipValidationDotNet(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IRoslynAstAnalyzer, RoslynAstAnalyzer>();
        services.TryAddScoped<WipWorkspaceProviderGit>();
        services.TryAddScoped<IValidator<DotNetValidationRequest, DotNetValidationResult>, DotNetValidationValidator>();

        return services;
    }
}