using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;

namespace Modus.Host.Domain.WebApi;

public sealed class PluginOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private readonly RuntimePluginRegistry _runtimePluginRegistry;
    private readonly IServiceProvider? _serviceProvider;

    public PluginOpenApiDocumentTransformer(RuntimePluginRegistry runtimePluginRegistry, IServiceProvider? serviceProvider)
    {
        _runtimePluginRegistry = runtimePluginRegistry ?? throw new ArgumentNullException(nameof(runtimePluginRegistry));
        _serviceProvider = serviceProvider;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Paths ??= new OpenApiPaths();

        foreach (var concretePath in document.Paths.Keys.Where(IsProjectedPluginOperationPath).ToArray())
        {
            document.Paths.Remove(concretePath);
        }

        if (!document.Paths.TryGetValue("/api/{pluginId}/{operation}", out var catchAllPath))
        {
            return Task.CompletedTask;
        }

        try
        {
            var projections = _runtimePluginRegistry.GetSnapshot().Catalogs
                .Where(static catalog => catalog is IPluginContract)
                .SelectMany(static catalog => catalog.SupportedOperations.Select(operation => (
                    PluginId: ((IPluginContract)catalog).PluginId.Value,
                    Operation: operation.Value)))
                .Distinct()
                .OrderBy(static projection => projection.PluginId, StringComparer.Ordinal)
                .ThenBy(static projection => projection.Operation, StringComparer.Ordinal)
                .ToArray();

            foreach (var projection in projections)
            {
                var path = $"/api/{projection.PluginId}/{projection.Operation}";
                document.Paths[path] = CreateConcretePathItem(catchAllPath, projection.PluginId, projection.Operation);
            }
        }
        catch (Exception ex)
        {
            _serviceProvider?.GetService<HostStatusRegistry>()?.AppendDiagnostics([$"stage=openapi outcome=failure reason={ex.Message}"]);
        }

        return Task.CompletedTask;
    }

    private static bool IsProjectedPluginOperationPath(string path)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Contains('{', StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 3 && string.Equals(segments[0], "api", StringComparison.Ordinal);
    }

    private static OpenApiPathItem CreateConcretePathItem(IOpenApiPathItem catchAllPath, string pluginId, string operationName)
    {
        var clonedPath = new OpenApiPathItem
        {
            Summary = catchAllPath.Summary,
            Description = catchAllPath.Description
        };

        clonedPath.Operations ??= [];
        clonedPath.Parameters ??= [];
        clonedPath.Servers ??= [];
        clonedPath.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);

        if (catchAllPath.Operations is not null)
        {
            foreach (var operation in catchAllPath.Operations)
            {
                clonedPath.Operations[operation.Key] = CreateConcreteOperation(
                    operation.Value,
                    pluginId,
                    operationName);
            }
        }

        if (catchAllPath.Parameters is not null)
        {
            foreach (var parameter in catchAllPath.Parameters)
            {
                if (!IsDynamicRoutePathParameter(parameter))
                {
                    clonedPath.Parameters.Add(parameter);
                }
            }
        }

        if (catchAllPath.Servers is not null)
        {
            foreach (var server in catchAllPath.Servers)
            {
                clonedPath.Servers.Add(server);
            }
        }

        if (catchAllPath.Extensions is not null)
        {
            foreach (var extension in catchAllPath.Extensions)
            {
                clonedPath.Extensions[extension.Key] = extension.Value;
            }
        }

        return clonedPath;
    }

    private static OpenApiOperation CreateConcreteOperation(OpenApiOperation source, string pluginId, string operationName)
    {
        var sourceOperationId = string.IsNullOrWhiteSpace(source.OperationId)
            ? "PluginOperation"
            : source.OperationId!;

        var operation = new OpenApiOperation
        {
            OperationId = BuildConcreteOperationId(sourceOperationId, pluginId, operationName),
            Summary = source.Summary,
            Description = source.Description,
            Deprecated = source.Deprecated,
            RequestBody = source.RequestBody,
            Responses = source.Responses
        };

        operation.Parameters ??= [];
        operation.Tags ??= new HashSet<OpenApiTagReference>();
        operation.Callbacks ??= new Dictionary<string, IOpenApiCallback>(StringComparer.Ordinal);
        operation.Security ??= [];
        operation.Servers ??= [];
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);

        if (source.Parameters is not null)
        {
            foreach (var parameter in source.Parameters)
            {
                if (!IsDynamicRoutePathParameter(parameter))
                {
                    operation.Parameters.Add(parameter);
                }
            }
        }

        if (source.Tags is not null)
        {
            foreach (var tag in source.Tags)
            {
                operation.Tags.Add(tag);
            }
        }

        if (source.Callbacks is not null)
        {
            foreach (var callback in source.Callbacks)
            {
                operation.Callbacks[callback.Key] = callback.Value;
            }
        }

        if (source.Security is not null)
        {
            foreach (var security in source.Security)
            {
                operation.Security.Add(security);
            }
        }

        if (source.Servers is not null)
        {
            foreach (var server in source.Servers)
            {
                operation.Servers.Add(server);
            }
        }

        if (source.Extensions is not null)
        {
            foreach (var extension in source.Extensions)
            {
                operation.Extensions[extension.Key] = extension.Value;
            }
        }

        return operation;
    }

    private static string BuildConcreteOperationId(string baseOperationId, string pluginId, string operationName)
    {
        return string.Concat(
            SanitizeOperationIdPart(baseOperationId),
            "__",
            SanitizeOperationIdPart(pluginId),
            "__",
            SanitizeOperationIdPart(operationName));
    }

    private static string SanitizeOperationIdPart(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;

        foreach (var character in value)
        {
            buffer[length++] = char.IsLetterOrDigit(character) ? character : '_';
        }

        return new string(buffer[..length]);
    }

    private static bool IsDynamicRoutePathParameter(IOpenApiParameter parameter)
    {
        if (parameter.In != ParameterLocation.Path)
        {
            return false;
        }

        return string.Equals(parameter.Name, "pluginId", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.Name, "operation", StringComparison.OrdinalIgnoreCase);
    }

}