using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Sandbox.Protocol;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

const int MaxCachedStrategies = 64;

var strategies = new Dictionary<string, CompiledStrategy>(StringComparer.Ordinal);
var bonesStrategies = new Dictionary<string, BonesCompiledStrategy>(StringComparer.Ordinal);
var handshakeCompleted = false;
using var stdin = Console.OpenStandardInput();
string? line;
while ((line = ReadBoundedLine(stdin, SandboxProtocol.MaxFrameBytes, out var tooLong)) is not null)
{
    if (tooLong)
    {
        SendError(string.Empty, SandboxErrorCodes.LimitExceeded, "Frame exceeds the protocol limit.");
        continue;
    }

    if (!string.IsNullOrEmpty(line) && line[0] == '\uFEFF')
    {
        line = line[1..];
    }

    if (line.StartsWith("\u00EF\u00BB\u00BF", StringComparison.Ordinal))
    {
        line = line[3..];
    }

    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = GetString(root, "type");
        var id = GetString(root, "id");
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(id))
        {
            SendError(id ?? string.Empty, SandboxErrorCodes.InvalidMessage, "type and id are required.");
            continue;
        }

        switch (type)
        {
            case "handshake":
                HandleHandshake(root, id);
                break;
            case "ping":
                Send("pong", id, true, null, null, new { });
                break;
            case "compile":
                if (!handshakeCompleted)
                {
                    SendError(id, SandboxErrorCodes.InvalidMessage, "Handshake is required before compile.");
                    break;
                }

                HandleCompile(root, id);
                break;
            case "chooseMove":
                if (!handshakeCompleted)
                {
                    SendError(id, SandboxErrorCodes.InvalidMessage, "Handshake is required before chooseMove.");
                    break;
                }

                HandleChooseMove(root, id);
                break;
            case "compileBones":
                if (!handshakeCompleted)
                {
                    SendError(id, SandboxErrorCodes.InvalidMessage, "Handshake is required before compileBones.");
                    break;
                }

                HandleCompileBones(root, id);
                break;
            case "chooseBonesMove":
                if (!handshakeCompleted)
                {
                    SendError(id, SandboxErrorCodes.InvalidMessage, "Handshake is required before chooseBonesMove.");
                    break;
                }

                HandleChooseBonesMove(root, id);
                break;
            case "shutdown":
                Send("shutdownResult", id, true, null, null, new { });
                foreach (var cached in strategies.Values)
                {
                    cached.Context.Unload();
                }

                foreach (var cached in bonesStrategies.Values)
                {
                    cached.Context.Unload();
                }

                return;
            default:
                SendError(id, SandboxErrorCodes.InvalidMessage, $"Unknown message type '{type}'.");
                break;
        }
    }
    catch (JsonException)
    {
        SendError(string.Empty, SandboxErrorCodes.InvalidMessage, "Malformed JSON frame.");
    }
    catch
    {
        SendError(string.Empty, SandboxErrorCodes.InternalError, "Worker internal error.");
    }
}

void HandleHandshake(JsonElement root, string id)
{
    var version = GetString(root, "protocolVersion") ?? "0.0";
    if (!version.StartsWith("1.", StringComparison.Ordinal))
    {
        SendError(id, SandboxErrorCodes.ProtocolVersionUnsupported, $"Unsupported protocol version '{version}'.");
        Environment.Exit(2);
    }

    handshakeCompleted = true;
    Send("handshakeResult", id, true, null, null, new
    {
        protocolVersion = SandboxProtocol.Version,
        capabilities = new[] { "compile", "chooseMove", "compileBones", "chooseBonesMove", "ping", "shutdown" }
    });
}

void HandleCompile(JsonElement root, string id)
{
    if (!root.TryGetProperty("payload", out var payload))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "compile payload is required.");
        return;
    }

    var strategyId = GetString(payload, "strategyId");
    var source = GetString(payload, "source");
    var claimedSourceHash = GetString(payload, "sourceHash") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(strategyId) || string.IsNullOrWhiteSpace(source))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "strategyId and source are required.");
        return;
    }

    var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    if (!string.IsNullOrEmpty(claimedSourceHash)
        && !string.Equals(claimedSourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
    {
        Send("compileResult", id, false, SandboxErrorCodes.SourceHashMismatch, "sourceHash does not match the source.", new { diagnostics = Array.Empty<string>() });
        return;
    }

    if (strategies.TryGetValue(strategyId, out var cached) && string.Equals(cached.SourceHash, sourceHash, StringComparison.Ordinal))
    {
        Send("compileResult", id, true, null, null, new { assemblyHash = cached.AssemblyHash, diagnostics = Array.Empty<string>() });
        return;
    }

    if (strategies.Count >= MaxCachedStrategies && !strategies.ContainsKey(strategyId))
    {
        SendError(id, SandboxErrorCodes.ResourceExhausted, "Strategy cache limit reached.");
        return;
    }

    CompilationResult compilation;
    try
    {
        compilation = Compile(source);
    }
    catch
    {
        Send("compileResult", id, false, SandboxErrorCodes.CompileError, "Strategy compilation failed.", new { diagnostics = Array.Empty<string>() });
        return;
    }

    if (!compilation.Success)
    {
        Send("compileResult", id, false, SandboxErrorCodes.CompileError, "Strategy compilation failed.", new { diagnostics = compilation.Diagnostics });
        return;
    }

    if (strategies.TryGetValue(strategyId, out var previous))
    {
        strategies.Remove(strategyId);
        previous.Context.Unload();
    }

    strategies[strategyId] = new CompiledStrategy(compilation.Type!, sourceHash, compilation.AssemblyHash, compilation.Context!);
    Send("compileResult", id, true, null, null, new { assemblyHash = compilation.AssemblyHash, diagnostics = Array.Empty<string>() });
}

void HandleChooseMove(JsonElement root, string id)
{
    if (!root.TryGetProperty("payload", out var payload))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "chooseMove payload is required.");
        return;
    }

    var strategyId = GetString(payload, "strategyId");
    if (string.IsNullOrWhiteSpace(strategyId) || !strategies.TryGetValue(strategyId, out var compiled))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "Unknown or uncompiled strategy.");
        return;
    }

    if (!payload.TryGetProperty("state", out var stateElement))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "state is required.");
        return;
    }

    var state = stateElement.Deserialize<WorkerStrategyState>(SandboxJson.Options);
    if (state is null)
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "state could not be read.");
        return;
    }

    var originalOut = Console.Out;
    var originalError = Console.Error;
    try
    {
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        var instance = (IWorkerStrategy)Activator.CreateInstance(compiled.Type)!;
        var move = instance.ChooseMove(state);
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        Send("chooseMoveResult", id, true, null, null, new { move });
    }
    catch
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        SendError(id, SandboxErrorCodes.InternalError, "Worker internal error.");
    }
}

void HandleCompileBones(JsonElement root, string id)
{
    if (!root.TryGetProperty("payload", out var payload))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "compileBones payload is required.");
        return;
    }

    var strategyId = GetString(payload, "strategyId");
    var source = GetString(payload, "source");
    var claimedSourceHash = GetString(payload, "sourceHash") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(strategyId) || string.IsNullOrWhiteSpace(source))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "strategyId and source are required.");
        return;
    }

    var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    if (!string.IsNullOrEmpty(claimedSourceHash) && !string.Equals(claimedSourceHash, sourceHash, StringComparison.OrdinalIgnoreCase))
    {
        Send("compileBonesResult", id, false, SandboxErrorCodes.SourceHashMismatch, "sourceHash does not match the source.", new { diagnostics = Array.Empty<string>() });
        return;
    }

    if (bonesStrategies.TryGetValue(strategyId, out var cached) && string.Equals(cached.SourceHash, sourceHash, StringComparison.Ordinal))
    {
        Send("compileBonesResult", id, true, null, null, new { assemblyHash = cached.AssemblyHash, diagnostics = Array.Empty<string>() });
        return;
    }

    var compilation = CompileBones(source);
    if (!compilation.Success)
    {
        Send("compileBonesResult", id, false, SandboxErrorCodes.CompileError, "Bones strategy compilation failed: " + string.Join("; ", compilation.Diagnostics.Take(3)), new { diagnostics = compilation.Diagnostics });
        return;
    }

    if (bonesStrategies.TryGetValue(strategyId, out var previous))
    {
        bonesStrategies.Remove(strategyId);
        previous.Context.Unload();
    }

    bonesStrategies[strategyId] = new BonesCompiledStrategy(compilation.Type!, sourceHash, compilation.AssemblyHash, compilation.Context!);
    Send("compileBonesResult", id, true, null, null, new { assemblyHash = compilation.AssemblyHash, diagnostics = Array.Empty<string>() });
}

void HandleChooseBonesMove(JsonElement root, string id)
{
    if (!root.TryGetProperty("payload", out var payload))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "chooseBonesMove payload is required.");
        return;
    }

    var strategyId = GetString(payload, "strategyId");
    if (string.IsNullOrWhiteSpace(strategyId) || !bonesStrategies.TryGetValue(strategyId, out var compiled))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "Unknown or uncompiled Bones strategy.");
        return;
    }

    if (!payload.TryGetProperty("state", out var stateElement) || !payload.TryGetProperty("legalMoves", out var legalElement))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "state and legalMoves are required.");
        return;
    }

    var dto = stateElement.Deserialize<BonesWorkerState>(SandboxJson.Options);
    var legalDtos = legalElement.Deserialize<BonesWorkerMove[]>(SandboxJson.Options);
    if (dto is null || legalDtos is null || legalDtos.Length == 0)
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "Bones state could not be read.");
        return;
    }

    var state = ToBonesState(dto);
    var legal = legalDtos.Select(ToBonesMove).ToArray();
    var originalOut = Console.Out;
    var originalError = Console.Error;
    try
    {
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        var instance = (IBonesPlayerSlot)Activator.CreateInstance(compiled.Type)!;
        var move = instance.ChooseMove(state, legal);
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        Send("chooseBonesMoveResult", id, true, null, null, new { move = ToMoveDto(move) });
    }
    catch
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        SendError(id, SandboxErrorCodes.InternalError, "Worker internal error.");
    }
}

BonesRoundState ToBonesState(BonesWorkerState dto)
{
    var board = dto.Board.Select(t => new BonesChainTile(
        new BonesTile(new BonesPipCount(t.FirstPip), new BonesPipCount(t.SecondPip)),
        new BonesPipCount(t.ChainLeftPip),
        new BonesPipCount(t.ChainRightPip))).ToImmutableArray();
    var hands = dto.Hands.ToDictionary(
        hand => new BonesPlayerId(hand.Seat),
        hand => new BonesHand(hand.Tiles.Select(tile => new BonesTile(new BonesPipCount(tile.FirstPip), new BonesPipCount(tile.SecondPip))).ToArray()));
    var events = dto.EventLog.Select(item => new BonesEvent(
        item.TurnIndex,
        new BonesPlayerId(item.PlayerSeat),
        (BonesEventKind)item.Kind,
        item.Tile is null ? null : new BonesTile(new BonesPipCount(item.Tile.FirstPip), new BonesPipCount(item.Tile.SecondPip)),
        item.Side is null ? null : (BonesBoardSide)item.Side.Value)).ToArray();
    return new BonesRoundState(
        new BonesGameId(dto.GameId),
        new BonesBoard(board),
        new BonesHands(hands),
        new BonesPlayerId(dto.CurrentSeat),
        new BonesTile(new BonesPipCount(dto.OpeningTile.FirstPip), new BonesPipCount(dto.OpeningTile.SecondPip)),
        dto.PassStreak,
        events,
        dto.Outcome is null ? null : (BonesRoundOutcomeKind)dto.Outcome.Value,
        dto.Winner is null ? null : new BonesPlayerId(dto.Winner.Value));
}

BonesMove ToBonesMove(BonesWorkerMove move) =>
    new(new BonesMoveId(move.MoveId),
        new BonesPlayerId(move.PlayerSeat),
        move.Tile is null ? null : new BonesTile(new BonesPipCount(move.Tile.FirstPip), new BonesPipCount(move.Tile.SecondPip)),
        move.Side is null ? null : (BonesBoardSide)move.Side.Value);

BonesWorkerMove ToMoveDto(BonesMove move)
{
    var tile = move.Tile is { } value ? new BonesWorkerTile(value.LowPip.Value, value.HighPip.Value) : null;
    return new BonesWorkerMove(move.MoveId.Value, move.PlayerId.Seat, tile, move.Side is null ? null : (int)move.Side.Value);
}

BonesCompilationResult CompileBones(string source)
{
    string[] forbiddenFragments = ["DllImport", "LibraryImport", "extern ", "unsafe", "System.IO", "System.Net", "System.Diagnostics", "System.Reflection", "System.Runtime.Loader", "System.Environment", "AppDomain", "Process"];
    if (forbiddenFragments.Any(fragment => source.Contains(fragment, StringComparison.Ordinal)))
    {
        return new BonesCompilationResult(false, null, string.Empty, ["Source uses a forbidden API surface."], null);
    }

    var syntaxTree = CSharpSyntaxTree.ParseText("global using System.Collections.Generic;" + Environment.NewLine + source);
    var references = new List<MetadataReference>();
    var allowedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Runtime.dll", "System.Console.dll", "System.Collections.dll", "System.Collections.Immutable.dll",
        "System.Linq.dll", "System.Private.CoreLib.dll", "System.Memory.dll"
    };
    var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
    if (!string.IsNullOrWhiteSpace(trusted))
    {
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(path) && allowedReferences.Contains(Path.GetFileName(path)))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
    }

    references.Add(MetadataReference.CreateFromFile(typeof(IBonesPlayerSlot).Assembly.Location));
    references.Add(MetadataReference.CreateFromFile(typeof(BonesWorkerState).Assembly.Location));
    var compilation = CSharpCompilation.Create("WipBonesWorkerStrategy", [syntaxTree], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var stream = new MemoryStream();
    var emit = compilation.Emit(stream);
    if (!emit.Success)
    {
        var diagnostics = emit.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage())
            .Take(50)
            .ToArray();
        return new BonesCompilationResult(false, null, string.Empty, diagnostics, null);
    }

    var bytes = stream.ToArray();
    var context = new BonesLoadContext();
    try
    {
        using var assemblyStream = new MemoryStream(bytes);
        var assembly = context.LoadFromStream(assemblyStream);
        var type = assembly.GetTypes().FirstOrDefault(static candidate =>
            typeof(IBonesPlayerSlot).IsAssignableFrom(candidate) && !candidate.IsAbstract && !candidate.IsInterface);
        if (type is null)
        {
            context.Unload();
            return new BonesCompilationResult(false, null, string.Empty, ["No IBonesPlayerSlot implementation was found."], null);
        }

        return new BonesCompilationResult(true, type, Convert.ToHexString(SHA256.HashData(bytes)), [], context);
    }
    catch
    {
        context.Unload();
        return new BonesCompilationResult(false, null, string.Empty, ["Bones strategy assembly could not be loaded."], null);
    }
}

CompilationResult Compile(string source)
{
    string[] forbiddenFragments =
    [
        "DllImport", "LibraryImport", "extern ", "unsafe", "System.IO", "System.Net",
        "System.Diagnostics", "System.Reflection", "System.Runtime.Loader", "System.Environment",
        "AppDomain", "Process"
    ];
    if (forbiddenFragments.Any(fragment => source.Contains(fragment, StringComparison.Ordinal)))
    {
        return new CompilationResult(false, null, string.Empty, ["Source uses a forbidden API surface."], null);
    }

    var syntaxTree = CSharpSyntaxTree.ParseText(source);
    var references = new List<MetadataReference>();
    var allowedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Private.CoreLib.dll"
    };
    var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
    if (!string.IsNullOrWhiteSpace(trusted))
    {
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(path) && allowedReferences.Contains(Path.GetFileName(path)))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }
    }

    references.Add(MetadataReference.CreateFromFile(typeof(IWorkerStrategy).Assembly.Location));
    var compilation = CSharpCompilation.Create(
        "WipSandboxStrategy",
        [syntaxTree],
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    using var stream = new MemoryStream();
    var emit = compilation.Emit(stream);
    if (!emit.Success)
    {
        var diagnostics = emit.Diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage())
            .Take(50)
            .ToArray();
        return new CompilationResult(false, null, string.Empty, diagnostics, null);
    }

    var bytes = stream.ToArray();
    var context = new StrategyLoadContext();
    try
    {
        using var assemblyStream = new MemoryStream(bytes);
        var assembly = context.LoadFromStream(assemblyStream);
        var type = assembly.GetTypes().FirstOrDefault(static candidate =>
            typeof(IWorkerStrategy).IsAssignableFrom(candidate) && !candidate.IsAbstract && !candidate.IsInterface);
        if (type is null)
        {
            context.Unload();
            return new CompilationResult(false, null, string.Empty, ["No IWorkerStrategy implementation was found."], null);
        }

        return new CompilationResult(true, type, Convert.ToHexString(SHA256.HashData(bytes)), [], context);
    }
    catch
    {
        context.Unload();
        return new CompilationResult(false, null, string.Empty, ["Strategy assembly could not be loaded."], null);
    }
}

static string? ReadBoundedLine(Stream stream, int maxBytes, out bool tooLong)
{
    tooLong = false;
    var buffer = new List<byte>(Math.Min(maxBytes, 4096));
    int value;
    while ((value = stream.ReadByte()) >= 0)
    {
        if (value == '\n')
        {
            break;
        }

        if (value == '\r')
        {
            continue;
        }

        if (buffer.Count < maxBytes)
        {
            buffer.Add((byte)value);
        }
        else
        {
            tooLong = true;
        }
    }

    if (value < 0 && buffer.Count == 0)
    {
        return null;
    }

    return Encoding.UTF8.GetString(buffer.ToArray());
}

static string? GetString(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static void SendError(string id, string code, string message) =>
    Send("error", id, false, code, message, new { });

static void Send(string type, string id, bool ok, string? errorCode, string? errorMessage, object? payload)
{
    JsonElement? element = payload is null
        ? null
        : JsonSerializer.SerializeToElement(payload, SandboxJson.Options);
    var response = new SandboxResponse(type, id, ok, errorCode, errorMessage, element);
    var json = JsonSerializer.Serialize(response, SandboxJson.Options);
    if (Encoding.UTF8.GetByteCount(json) > SandboxProtocol.MaxOutputBytes)
    {
        json = JsonSerializer.Serialize(
            new SandboxResponse("error", id, false, SandboxErrorCodes.LimitExceeded, "Response exceeds the output limit.", null),
            SandboxJson.Options);
    }

    Console.Out.WriteLine(json);
    Console.Out.Flush();
}

internal sealed record CompiledStrategy(Type Type, string SourceHash, string AssemblyHash, AssemblyLoadContext Context);

internal sealed record BonesCompiledStrategy(Type Type, string SourceHash, string AssemblyHash, AssemblyLoadContext Context);

internal sealed record BonesCompilationResult(bool Success, Type? Type, string AssemblyHash, string[] Diagnostics, AssemblyLoadContext? Context);

internal sealed record CompilationResult(bool Success, Type? Type, string AssemblyHash, string[] Diagnostics, AssemblyLoadContext? Context);

internal sealed class StrategyLoadContext : AssemblyLoadContext
{
    public StrategyLoadContext()
        : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName) =>
        string.Equals(assemblyName.Name, typeof(IWorkerStrategy).Assembly.GetName().Name, StringComparison.Ordinal)
            ? typeof(IWorkerStrategy).Assembly
            : null;
}

internal sealed class BonesLoadContext : AssemblyLoadContext
{
    public BonesLoadContext()
        : base(isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, typeof(IBonesPlayerSlot).Assembly.GetName().Name, StringComparison.Ordinal))
        {
            return typeof(IBonesPlayerSlot).Assembly;
        }

        if (string.Equals(assemblyName.Name, typeof(BonesWorkerState).Assembly.GetName().Name, StringComparison.Ordinal))
        {
            return typeof(BonesWorkerState).Assembly;
        }

        return null;
    }
}
