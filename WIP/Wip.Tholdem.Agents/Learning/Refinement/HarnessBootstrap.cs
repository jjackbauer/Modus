using System.Text;
using System.Text.Json;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>A bootstrap request that failed a stable validation check.</summary>
public sealed class HarnessBootstrapException : RefinementDataException
{
    public HarnessBootstrapException(string code, string message)
        : base(code, message)
    {
    }

    public HarnessBootstrapException(string code, string message, Exception innerException)
        : base(code, message, innerException)
    {
    }
}

/// <summary>
/// The verified, data-only state loaded from a prior data directory: the champion artifact, the
/// verified memory snapshot and its raw file bytes, the champion lineage, the pointer bytes to carry
/// forward and the mode. It carries no clock, no identifier generator and no culture.
/// </summary>
public sealed record HarnessBootstrapState(
    HarnessBootstrapMode Mode,
    StrategyArtifact Champion,
    HarnessMemorySnapshot Memory,
    IReadOnlyList<StrategyLineageEntry> Lineage,
    string MemoryHash,
    byte[] MemoryBytes,
    byte[] ChampionPointerBytes);

/// <summary>
/// Loads a prior data directory for an opt-in bootstrap run and prepares a fresh target from it. The
/// source memory is verified by <see cref="HarnessMemoryStore"/> before anything is copied, the source
/// champion is re-read through <see cref="TrainingLoop.ReadPersistedStrategy"/> (content hash
/// verified) and the lineage is resolved only after both checks pass.
/// </summary>
public static class HarnessBootstrap
{
    /// <summary>The source directory named by the options does not exist.</summary>
    public const string SourceMissingCode = "bootstrap-source-missing";

    /// <summary>The source pointer, pointer content or champion artifact is missing or unreadable.</summary>
    public const string ChampionMissingCode = "bootstrap-champion-missing";

    /// <summary>The source memory file failed schema, shape or content-hash verification.</summary>
    public const string MemoryInvalidCode = "bootstrap-memory-invalid";

    /// <summary>The source champion lineage cannot be resolved or verified.</summary>
    public const string LineageInvalidCode = "bootstrap-lineage-invalid";

    /// <summary>The target already contains an active champion pointer or a training ledger.</summary>
    public const string TargetNotFreshCode = "bootstrap-target-not-fresh";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static HarnessBootstrapState Load(string sourceDirectory, HarnessBootstrapMode mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        if (mode == HarnessBootstrapMode.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "A bootstrap load requires the Frozen or Updating mode.");
        }

        var source = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(source))
        {
            throw new HarnessBootstrapException(
                SourceMissingCode,
                $"The bootstrap source directory '{source}' does not exist.");
        }

        // The source memory is verified before anything is copied: a tampered file refuses here and
        // leaves both the source and the target untouched.
        var memoryStore = new HarnessMemoryStore(source);
        HarnessMemorySnapshot memory;
        try
        {
            memory = memoryStore.Load();
        }
        catch (HarnessMemoryException exception)
        {
            throw new HarnessBootstrapException(
                MemoryInvalidCode,
                $"The bootstrap memory '{memoryStore.MemoryPath}' is not valid "
                + $"({exception.Code}): {exception.Message}",
                exception);
        }

        byte[] memoryBytes;
        if (File.Exists(memoryStore.MemoryPath))
        {
            try
            {
                memoryBytes = File.ReadAllBytes(memoryStore.MemoryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new HarnessBootstrapException(
                    MemoryInvalidCode,
                    $"The bootstrap memory '{memoryStore.MemoryPath}' could not be read: {exception.Message}",
                    exception);
            }
        }
        else
        {
            // No source memory is an empty, canonical memory file, so the target always has a
            // verifiable snapshot and Frozen stays a byte-stable read-only state.
            memoryBytes = Utf8.GetBytes(HarnessMemorySnapshot.Empty.ToCanonicalJson());
        }

        var pointerPath = Path.Combine(source, TrainingLoop.ChampionFileName);
        if (!File.Exists(pointerPath))
        {
            throw new HarnessBootstrapException(
                ChampionMissingCode,
                $"The bootstrap source '{source}' has no champion pointer '{pointerPath}'.");
        }

        byte[] pointerBytes;
        string championId;
        try
        {
            pointerBytes = File.ReadAllBytes(pointerPath);
            championId = ReadChampionStrategyId(pointerBytes, pointerPath);
        }
        catch (Exception exception) when (IsChampionReadFailure(exception))
        {
            throw new HarnessBootstrapException(
                ChampionMissingCode,
                $"The bootstrap champion pointer '{pointerPath}' is not readable: {exception.Message}",
                exception);
        }

        StrategyArtifact champion;
        try
        {
            champion = TrainingLoop.ReadPersistedStrategy(source, championId);
        }
        catch (Exception exception) when (IsChampionReadFailure(exception))
        {
            throw new HarnessBootstrapException(
                ChampionMissingCode,
                $"The bootstrap champion '{championId}' could not be read from '{source}': {exception.Message}",
                exception);
        }

        IReadOnlyList<StrategyLineageEntry> lineage;
        try
        {
            lineage = StrategyLineageReader.Read(source, championId);
        }
        catch (RefinementDataException exception)
        {
            throw new HarnessBootstrapException(
                LineageInvalidCode,
                $"The bootstrap lineage from '{championId}' is not valid "
                + $"({exception.Code}): {exception.Message}",
                exception);
        }
        catch (Exception exception) when (IsChampionReadFailure(exception))
        {
            throw new HarnessBootstrapException(
                LineageInvalidCode,
                $"The bootstrap lineage from '{championId}' could not be read: {exception.Message}",
                exception);
        }

        return new HarnessBootstrapState(
            mode,
            champion,
            memory,
            lineage,
            memory.ContentHash,
            memoryBytes,
            pointerBytes);
    }

    public static void PrepareTarget(string targetDirectory, HarnessBootstrapState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentNullException.ThrowIfNull(state);

        var target = Path.GetFullPath(targetDirectory);
        if (File.Exists(Path.Combine(target, TrainingLoop.ChampionFileName))
            || File.Exists(Path.Combine(target, TrainingLoop.LedgerFileName)))
        {
            throw new HarnessBootstrapException(
                TargetNotFreshCode,
                $"The bootstrap target '{target}' is not fresh: it already contains "
                + $"'{TrainingLoop.ChampionFileName}' or '{TrainingLoop.LedgerFileName}'.");
        }

        var strategies = Path.Combine(target, "strategies");
        var harness = Path.Combine(target, HarnessMemoryStore.DirectoryName);
        Directory.CreateDirectory(strategies);
        Directory.CreateDirectory(harness);

        // Data-only preparation: the champion artifact is re-emitted canonically, and the pointer and
        // memory files are copied verbatim. There is no clock, identifier generator or culture here.
        File.WriteAllText(
            Path.Combine(strategies, state.Champion.Id + ".json"),
            state.Champion.ToCanonicalJson(),
            Utf8);
        File.WriteAllBytes(Path.Combine(target, TrainingLoop.ChampionFileName), state.ChampionPointerBytes);
        File.WriteAllBytes(Path.Combine(harness, HarnessMemoryStore.FileName), state.MemoryBytes);
    }

    private static string ReadChampionStrategyId(byte[] pointerBytes, string pointerPath)
    {
        using var document = JsonDocument.Parse(pointerBytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("strategyId", out var strategyIdElement)
            || strategyIdElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(strategyIdElement.GetString()))
        {
            throw new InvalidOperationException(
                $"The champion pointer '{pointerPath}' does not contain a non-empty strategyId.");
        }

        return strategyIdElement.GetString()!;
    }

    private static bool IsChampionReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException or ArgumentException;
}
