namespace Wip.Bones.ModelProviders.DeepSeek.Tests;

internal static class BonesDeepSeekRequirementsChecklistItems
{
    public const string AdapterAssembly =
        "Introduce `Wip.Bones.ModelProviders.DeepSeek` class library referencing `Wip.Bones.Agents`, `Wip.Abstractions`, and `Wip.Runtime` (for `DeepSeekModelProvider` reuse) [foundation for DeepSeek adapters] [mandatory - adapter assembly]";

    public const string ProviderOptions =
        "Implement `BonesDeepSeekProviderOptions` with validated `BaseUrl` (absolute HTTP/HTTPS), supported model identifier (`deepseek-chat`, `deepseek-reasoner`), positive `Timeout`, and `ApiKeySource` in `env:<VARIABLE_NAME>` format matching `DeepSeekModelProviderOptions` conventions [depends on adapter assembly] [mandatory - configuration contract]";

    public const string MessageMapper =
        "Implement shared message mapper translating `BonesStrategyAuthoringMessage`, `BonesPlayTurnMessage`, and `BonesStrategyEnhancementMessage` into `DeepSeekChatMessage` lists for outbound HTTP requests [depends on options contract]";

    public const string StrategyAuthoringProvider =
        "Implement `DeepSeekBonesStrategyAuthoringProvider` implementing `IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>` — forwards ponder prompts to `DeepSeekModelProvider`, returns assistant markdown as `BonesStrategyAuthoringResult`, propagates `providerId=deepseek`, configured `modelId`, usage tokens, and `correlationId` [depends on message mapper] [mandatory - strategy authoring LLM]";

    public const string PlayTurnResponseParser =
        "Implement `BonesPlayTurnResponseParser` extracting a move id from LLM content (exact match against `AllowedMoves`, trimmed line, or JSON `moveId` field) and returning null when no legal id is found so `BonesPlayMatchTool` retry logic applies [depends on play-turn provider]";

    public const string PlayTurnProvider =
        "Implement `DeepSeekBonesPlayTurnProvider` implementing `IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>` — includes allowed-move enumeration in user prompt, instructs model to reply with exactly one move id from the list (or `pass` when only pass is legal), parses response via `BonesPlayTurnResponseParser`, and returns `BonesPlayTurnResult` with selected move id [depends on message mapper] [mandatory - play-turn LLM engine]";

    public const string StrategyEnhancementProvider =
        "Implement `DeepSeekBonesStrategyEnhancementProvider` implementing `IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>` — forwards enhance prompts to DeepSeek, returns enhanced markdown preserving `## Rules` and appending `## Enhancements` section semantics [depends on message mapper] [mandatory - strategy enhancement LLM]";

    public const string AddBonesDeepSeekModelProviders =
        "Add `AddBonesDeepSeekModelProviders(IServiceCollection, BonesDeepSeekProviderOptions)` extension registering `DeepSeekModelProviderOptions`, `HttpClient`, inner `DeepSeekModelProvider`, and all three Bones adapter singletons [depends on all three adapters]";

    public const string HttpMappingProofTests =
        "Add `Wip.Bones.ModelProviders.DeepSeek.Tests` with mock `HttpMessageHandler` proving outbound request contains ponder/play/enhance prompt content, correct model id, bearer auth, and correlation id continuity on success paths [depends on adapters] [mandatory - HTTP mapping proof]";

    public const string NegativePathIsolationTests =
        "Add negative-path tests: missing `DEEPSEEK_API_KEY`, unsupported model selection, HTTP 503 endpoint failure, malformed empty choices payload, and play-turn response with illegal move id — each must fail deterministically without mutating artifact store or board state [depends on adapter tests] [mandatory - negative-path isolation]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.DeepSeek.md` in `BehaviorProofComplianceRegistry` with owning assembly `Wip.Bones.ModelProviders.DeepSeek.Tests` [depends on adapter tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string DeepSeekScriptAuthoringHttpMapping =
        "Update `DeepSeekBonesStrategyAuthoringProvider` and `DeepSeekBonesStrategyEnhancementProvider` HTTP mapping tests to assert outbound user content includes script API contract headers and `IBonesPlayerSlot` requirement [depends on prompt builder changes]";

    public const string ScriptStrategiesDeepSeekPromptCoverage =
        "Add `Wip.Bones.ModelProviders.DeepSeek.Tests` coverage proving ponder/enhance prompts request C# `IBonesPlayerSlot` implementation [depends on prompt changes]";

    public const string LearningLoopForbiddenMembers =
        "Update `DeepSeekBonesStrategyAuthoringProvider` tests to assert outbound content includes forbidden API members from `BonesStrategyScriptApiReference` [depends on API reference] [mandatory - prompt contract regression gate]";
}
