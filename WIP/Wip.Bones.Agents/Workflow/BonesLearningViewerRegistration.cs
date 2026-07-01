using Microsoft.Extensions.DependencyInjection;
using Wip.Bones.Agents.Viewer;
using Wip.Builder;

namespace Wip.Bones.Agents.Workflow;

public static class BonesLearningViewerRegistration
{
    public static IServiceCollection AddBonesLearningViewerIntegration(
        this IServiceCollection services,
        Uri viewerBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(viewerBaseUrl);

        services.AddSingleton(viewerBaseUrl);
        services.AddSingleton<IBonesViewerStdout, ConsoleBonesViewerStdout>();
        services.AddSingleton<BonesViewerRunPublisher>();
        services.AddSingleton<IBonesViewerRunPublisher>(sp => sp.GetRequiredService<BonesViewerRunPublisher>());
        services.AddSingleton<BonesLearningViewerCoordinator>();

        return services;
    }

    public static WipBuilder AddBonesLearningWorkflowWithViewer(
        this WipBuilder builder,
        Uri viewerBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(viewerBaseUrl);

        builder.Services.AddBonesLearningViewerIntegration(viewerBaseUrl);
        return builder.AddBonesLearningWorkflow();
    }
}
