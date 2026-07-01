namespace Wip.Bones.Host.Tests;

internal static class BonesDeepSeekHostRequirementsChecklistItems
{
    public const string BonesHostOptionsModelProvider =
        "Extend `BonesHostOptions` with `ModelProvider` enum (`DeepSeek` default, `Stub` for offline/test) and nested `DeepSeek` options bound from `BonesHost:ModelProvider` and `BonesHost:DeepSeek` configuration sections [depends on options contract]";

    public const string BonesHostServiceCollectionDeepSeekRegistration =
        "Update `BonesHostServiceCollectionExtensions` to register DeepSeek adapters when `ModelProvider=DeepSeek` and stub providers only when `ModelProvider=Stub`; production default must be DeepSeek [depends on host options and registration extension] [mandatory - production default]";

    public const string BonesHostWorkflowModelIdPassthrough =
        "Pass configured DeepSeek model identifier into workflow stage requests (ponder/play/enhance) so HTTP payloads use `deepseek-chat` or `deepseek-reasoner` rather than bones-local default model names when DeepSeek is active [depends on host DI wiring]";

    public const string BonesHostModelProviderDiagnostics =
        "Expose startup and `GET /api/bones/status` diagnostics proving active model provider (`deepseek` vs `stub`), configured model name, base URL, timeout, and key source reference without leaking secret values [depends on host wiring] [mandatory - observability]";

    public const string HostDeepSeekIntegration =
        "Add `Wip.Bones.Host.Tests` integration proving host with `ModelProvider=DeepSeek` and mock handler completes one learning iteration with strategy and match artifacts whose content reflects mocked LLM responses (not stub boilerplate) [depends on host wiring]";

    public const string OperatorDeepSeekSetup =
        "Document operator setup: set `DEEPSEEK_API_KEY`, run `dotnet run --project WIP/Wip.Bones.Host`, verify status shows `provider=deepseek`; document `ModelProvider=stub` for offline development [depends on host integration test]";
}
