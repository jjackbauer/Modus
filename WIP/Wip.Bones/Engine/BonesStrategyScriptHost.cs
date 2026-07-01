using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed class BonesStrategyScriptHost
{
    private static readonly string[] DisallowedNamespacePrefixes =
    [
        "System.IO",
        "System.Net",
        "System.Diagnostics.Process",
        "System.Reflection.Emit",
    ];

    private static readonly string[] AllowedTrustedPlatformAssemblyPrefixes =
    [
        "System.Private.CoreLib",
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.Threading",
        "System.Memory",
        "netstandard",
    ];
    private const string ScriptSourcePrologue = "global using System.Collections.Generic;";

    private static readonly Lazy<MetadataReference[]> ScriptReferences = new(CreateScriptReferences);

    private readonly BonesStrategyScriptHostOptions _options;
    private readonly ConcurrentDictionary<string, BonesCompiledStrategy> _compileCache = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _lruOrder = new();
    private int _compileInvocationCount;

    public BonesStrategyScriptHost()
        : this(new BonesStrategyScriptHostOptions())
    {
    }

    public BonesStrategyScriptHost(BonesStrategyScriptHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.CompilationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "CompilationTimeout must be greater than zero.");

        if (options.ExecutionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "ExecutionTimeout must be greater than zero.");

        _options = options;
    }

    public int CompileInvocationCount => _compileInvocationCount;

    public int CompiledScriptsCached => _compileCache.Count;

    public int MaxCompiledScripts => _options.MaxCompiledScripts;

    public BonesStrategyScriptCompileResult TryCompile(BonesStrategyId strategyId, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return BonesStrategyScriptCompileResult.Failure("Strategy script source cannot be null or whitespace.");

        var sourceHash = ComputeSourceHash(source);
        var cacheKey = CreateCacheKey(strategyId, sourceHash);
        if (_compileCache.TryGetValue(cacheKey, out var cached))
            return BonesStrategyScriptCompileResult.Success(cached);

        var namespaceViolation = FindDisallowedNamespaceViolation(source);
        if (namespaceViolation is not null)
            return BonesStrategyScriptCompileResult.Failure(namespaceViolation);

        try
        {
            var compiled = CompileSource(strategyId, source, sourceHash);
            _compileCache[cacheKey] = compiled;
            _lruOrder.Enqueue(cacheKey);
            EvictLruIfNeeded();
            return BonesStrategyScriptCompileResult.Success(compiled);
        }
        catch (OperationCanceledException)
        {
            return BonesStrategyScriptCompileResult.Failure("Strategy script compilation timed out.");
        }
        catch (Exception exception)
        {
            return BonesStrategyScriptCompileResult.Failure($"Strategy script compilation failed: {exception.Message}");
        }
    }

    public BonesMove ExecuteChooseMove(
        BonesCompiledStrategy compiled,
        BonesRoundState state,
        IReadOnlyList<BonesMove> legalMoves)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(legalMoves);

        using var timeoutCts = new CancellationTokenSource(_options.ExecutionTimeout);

        try
        {
            var chooseMoveTask = Task.Run(
                () => compiled.PlayerSlot.ChooseMove(state, legalMoves),
                timeoutCts.Token);

            if (!chooseMoveTask.Wait(_options.ExecutionTimeout))
            {
                timeoutCts.Cancel();
                throw new BonesStrategyScriptExecutionTimeoutException(_options.ExecutionTimeout);
            }

            return chooseMoveTask.Result;
        }
        catch (AggregateException aggregateException) when (aggregateException.InnerException is OperationCanceledException)
        {
            throw new BonesStrategyScriptExecutionTimeoutException(_options.ExecutionTimeout);
        }
        catch (OperationCanceledException)
        {
            throw new BonesStrategyScriptExecutionTimeoutException(_options.ExecutionTimeout);
        }
    }

    private void EvictLruIfNeeded()
    {
        var max = _options.MaxCompiledScripts;
        while (_compileCache.Count > max && _lruOrder.TryDequeue(out var evictedKey))
        {
            _compileCache.TryRemove(evictedKey, out _);
        }
    }

    private BonesCompiledStrategy CompileSource(BonesStrategyId strategyId, string source, string sourceHash)
    {
        Interlocked.Increment(ref _compileInvocationCount);

        using var timeoutCts = new CancellationTokenSource(_options.CompilationTimeout);
        var cancellationToken = timeoutCts.Token;

        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(ScriptSourcePrologue + Environment.NewLine + source, Encoding.UTF8),
            cancellationToken: cancellationToken);

        var compilation = CSharpCompilation.Create(
            assemblyName: $"BonesStrategyScript_{Guid.NewGuid():N}",
            syntaxTrees: [syntaxTree],
            references: ScriptReferences.Value,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false));

        using var assemblyStream = new MemoryStream();
        EmitResult emitResult;

        try
        {
            emitResult = compilation.Emit(assemblyStream, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        if (!emitResult.Success)
        {
            var diagnosticMessages = emitResult.Diagnostics
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(static diagnostic => diagnostic.GetMessage())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var failureReason = diagnosticMessages.Length == 0
                ? "Strategy script compilation produced one or more errors."
                : string.Join(Environment.NewLine, diagnosticMessages);

            throw new InvalidOperationException(failureReason);
        }

        assemblyStream.Position = 0;
        var assembly = Assembly.Load(assemblyStream.ToArray());
        var playerSlot = InstantiatePlayerSlot(assembly);
        return new BonesCompiledStrategy(strategyId, sourceHash, playerSlot);
    }

    private static IBonesPlayerSlot InstantiatePlayerSlot(Assembly assembly)
    {
        var slotTypes = assembly
            .GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(type => typeof(IBonesPlayerSlot).IsAssignableFrom(type))
            .ToArray();

        if (slotTypes.Length != 1)
            throw new InvalidOperationException("Strategy script must define exactly one public class implementing IBonesPlayerSlot.");

        return Activator.CreateInstance(slotTypes[0]) as IBonesPlayerSlot
            ?? throw new InvalidOperationException($"Unable to instantiate '{slotTypes[0].FullName}'.");
    }

    private static string? FindDisallowedNamespaceViolation(string source)
    {
        foreach (var line in source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("using ", StringComparison.Ordinal))
                continue;

            var namespaceName = line["using ".Length..].TrimEnd(';').Trim();
            if (namespaceName.StartsWith("global::", StringComparison.Ordinal))
                namespaceName = namespaceName["global::".Length..];

            foreach (var disallowedPrefix in DisallowedNamespacePrefixes)
            {
                if (namespaceName.Equals(disallowedPrefix, StringComparison.Ordinal)
                    || namespaceName.StartsWith(disallowedPrefix + ".", StringComparison.Ordinal))
                {
                    return $"Strategy script references disallowed namespace '{namespaceName}'.";
                }
            }
        }

        return null;
    }

    private static string CreateCacheKey(BonesStrategyId strategyId, string sourceHash) =>
        $"{strategyId.Value}:{sourceHash}";

    private static string ComputeSourceHash(string source) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));

    private static MetadataReference[] CreateScriptReferences()
    {
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(path);
                if (!IsAllowedTrustedPlatformAssembly(assemblyName))
                    continue;

                referencePaths.Add(path);
            }
        }

        TryAddAssemblyPath(typeof(object).Assembly, referencePaths);
        TryAddAssemblyPath(typeof(IReadOnlyList<>).Assembly, referencePaths);
        TryAddAssemblyPath(typeof(ImmutableArray<>).Assembly, referencePaths);
        TryAddAssemblyPath(typeof(IBonesPlayerSlot).Assembly, referencePaths);

        return referencePaths
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static void TryAddAssemblyPath(Assembly assembly, HashSet<string> referencePaths)
    {
        if (string.IsNullOrWhiteSpace(assembly.Location)
            || IsBlockedReferenceAssembly(assembly.GetName().Name))
        {
            return;
        }

        referencePaths.Add(assembly.Location);
    }

    private static bool IsAllowedTrustedPlatformAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || IsBlockedReferenceAssembly(assemblyName))
            return false;

        return AllowedTrustedPlatformAssemblyPrefixes.Any(
            prefix => assemblyName.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || assemblyName.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsBlockedReferenceAssembly(string? assemblyName) =>
        !string.IsNullOrWhiteSpace(assemblyName)
        && (assemblyName.StartsWith("System.IO", StringComparison.Ordinal)
            || assemblyName.StartsWith("System.Net", StringComparison.Ordinal)
            || assemblyName.StartsWith("System.Diagnostics.Process", StringComparison.Ordinal)
            || assemblyName.StartsWith("Microsoft.", StringComparison.Ordinal));
}
