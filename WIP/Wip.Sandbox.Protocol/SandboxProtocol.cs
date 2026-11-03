using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wip.Sandbox.Protocol;

public static class SandboxProtocol
{
    public const string Version = "1.0";
    public const int MaxFrameBytes = 1024 * 1024;
    public const int MaxOutputBytes = 256 * 1024;
}

public sealed record WorkerStrategyState(string[] Board, string Player, int[] LegalMoves, int Turn);

public interface IWorkerStrategy
{
    int ChooseMove(WorkerStrategyState state);
}

public sealed record SandboxRequest(string Type, string Id, string? ProtocolVersion, JsonElement Payload);

public sealed record SandboxResponse(
    string Type,
    string Id,
    bool Ok,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement? Payload);

public static class SandboxErrorCodes
{
    public const string ProtocolVersionUnsupported = "protocolVersionUnsupported";
    public const string InvalidMessage = "invalidMessage";
    public const string LimitExceeded = "limitExceeded";
    public const string CompileError = "compileError";
    public const string Timeout = "timeout";
    public const string ResourceExhausted = "resourceExhausted";
    public const string WorkerCrash = "workerCrash";
    public const string InternalError = "internalError";
}

public static class SandboxJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
