using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Wip.Sandbox.Protocol;

Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

var strategies = new Dictionary<string, CompiledStrategy>(StringComparer.Ordinal);
var stdin = Console.In;
string? line;
while ((line = stdin.ReadLine()) is not null)
{
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

    if (line.Length > SandboxProtocol.MaxFrameBytes)
    {
        SendError(string.Empty, SandboxErrorCodes.LimitExceeded, "Frame exceeds the protocol limit.");
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
                HandleCompile(root, id);
                break;
            case "chooseMove":
                HandleChooseMove(root, id);
                break;
            case "shutdown":
                Send("shutdownResult", id, true, null, null, new { });
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
    catch (Exception exception)
    {
        SendError(string.Empty, SandboxErrorCodes.InternalError, exception.GetType().Name);
    }
}

void HandleHandshake(JsonElement root, string id)
{
    var version = GetString(root, "protocolVersion") ?? "0.0";
    if (!string.Equals(version, SandboxProtocol.Version, StringComparison.Ordinal))
    {
        SendError(id, SandboxErrorCodes.ProtocolVersionUnsupported, $"Unsupported protocol version '{version}'.");
        return;
    }

    Send("handshakeResult", id, true, null, null, new
    {
        protocolVersion = SandboxProtocol.Version,
        capabilities = new[] { "compile", "chooseMove", "ping", "shutdown" }
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
    var sourceHash = GetString(payload, "sourceHash") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(strategyId) || string.IsNullOrWhiteSpace(source))
    {
        SendError(id, SandboxErrorCodes.InvalidMessage, "strategyId and source are required.");
        return;
    }

    if (strategies.TryGetValue(strategyId, out var cached) && string.Equals(cached.SourceHash, sourceHash, StringComparison.Ordinal))
    {
        Send("compileResult", id, true, null, null, new { assemblyHash = cached.AssemblyHash, diagnostics = Array.Empty<string>() });
        return;
    }

    var compilation = Compile(source);
    if (!compilation.Success)
    {
        Send("compileResult", id, false, SandboxErrorCodes.CompileError, "Strategy compilation failed.", new { diagnostics = compilation.Diagnostics });
        return;
    }

    strategies[strategyId] = new CompiledStrategy(compilation.Type!, sourceHash, compilation.AssemblyHash);
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

    try
    {
        var instance = (IWorkerStrategy)Activator.CreateInstance(compiled.Type)!;
        var move = instance.ChooseMove(state);
        Send("chooseMoveResult", id, true, null, null, new { move });
    }
    catch (Exception exception)
    {
        SendError(id, SandboxErrorCodes.InternalError, exception.GetType().Name);
    }
}

CompilationResult Compile(string source)
{
    var syntaxTree = CSharpSyntaxTree.ParseText(source);
    var references = new List<MetadataReference>();
    var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
    if (!string.IsNullOrWhiteSpace(trusted))
    {
        foreach (var path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(path))
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
        return new CompilationResult(false, null, string.Empty, diagnostics);
    }

    var bytes = stream.ToArray();
    var assembly = Assembly.Load(bytes);
    var type = assembly.GetTypes().FirstOrDefault(static candidate =>
        typeof(IWorkerStrategy).IsAssignableFrom(candidate) && !candidate.IsAbstract && !candidate.IsInterface);
    if (type is null)
    {
        return new CompilationResult(false, null, string.Empty, ["No IWorkerStrategy implementation was found."]);
    }

    return new CompilationResult(true, type, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), []);
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
    Console.Out.WriteLine(JsonSerializer.Serialize(response, SandboxJson.Options));
    Console.Out.Flush();
}

internal sealed record CompiledStrategy(Type Type, string SourceHash, string AssemblyHash);

internal sealed record CompilationResult(bool Success, Type? Type, string AssemblyHash, string[] Diagnostics);
