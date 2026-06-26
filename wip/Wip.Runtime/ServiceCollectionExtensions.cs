using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Runtime.Runtime;

namespace Wip.Runtime;

public static class ServiceCollectionExtensions
{
    public static WipBuilder AddWipRuntime(this IServiceCollection services)
    {
        return services.AddWipRuntime(configureCapabilities: null);
    }

    public static WipBuilder AddWipRuntime(
        this IServiceCollection services,
        Action<WipBuilderOptions>? configureCapabilities)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISessionStore, InMemorySessionStore>();
        services.TryAddSingleton<ISessionEventPublisher, NoOpSessionEventPublisher>();
        services.TryAddSingleton<WipRuntimeOrchestrator>();

        return configureCapabilities is null
            ? services.AddWipCapabilities()
            : services.AddWipCapabilities(configureCapabilities);
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            return _sessions.TryGetValue(sessionId, out var snapshot)
                ? ValueTask.FromResult<SessionSnapshot?>(snapshot)
                : ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class NoOpSessionEventPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
