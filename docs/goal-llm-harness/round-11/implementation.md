# Round 11 — Selectable tool-prompt mode (implementation)

Status: selectable at the agent-options boundary. The default remains `System` with the original
system prompt bytes, the four tools, permissions, executor, memory and transport are unchanged, and
the two new focused tests pass. The only failing test in the full suite is the known deliberate D6
gate. No live Ponder host was run and no `--secret-file` was passed.

## 1. Test-first failure (observed)

The new test file was added before any production file. Build:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed (exit 1):

```text
WIP\Wip.Tholdem.Tests\Runtime\TholdemToolPromptModeTests.cs(137,9): error CS0246: The type or
namespace name 'TholdemToolPromptMode' could not be found (are you missing a using directive or an
assembly reference?)
Build FAILED.
    1 Error(s)
```

The enum did not exist yet (`TholdemToolPromptModeTests.cs:28,44,137`), so the tests could not even
compile before the implementation.

## 2. Files and changes

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemToolPromptMode.cs` (new) | `None = 0, System = 1, Task = 2, Both = 3, BothRequired = 4`. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemAgentOptions.cs:10-16` | Appended `TholdemToolPromptMode ToolPromptMode = TholdemToolPromptMode.System`; the existing positional parameter order is untouched. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:32-47` | `SystemPromptText` (with the option-A invitation) is unchanged byte for byte; it serves System/Both/BothRequired. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:54-67` | New `BaseSystemPromptText`: the same contract and JSON reply schema without the invitation; serves None/Task. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:69-75` | The exact optional and mandatory task invitation line constants. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:137-141,146-153` | `ActAsync` passes the mode-selected task and system prompt into `AgentExecutionRequest`; tools, memory and executor wiring are unchanged. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:200-225` | `BuildTask` appends exactly one invitation line after the reply schema and before the observation header for Task/Both/BothRequired; no line for None/System. |
| `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:231-256` | `TaskInvitationFor` / `SystemPromptFor` selection; an undefined enum value throws `ArgumentOutOfRangeException`. |
| `.tmp/goal-llm-harness/ponder/Program.cs:84-99,101-112,163-171,181-182,207-217,245,262,308,327-359` | `--tool-prompt none|system|task|both|both-required` (default system), enum mapping, pass to `TholdemAgentOptions.ToolPromptMode`, and `toolPrompt=<value>` in the summary. Usage text updated; no other host change. |
| `WIP/Wip.Tholdem.Tests/Runtime/TholdemToolPromptModeTests.cs` (new) | Two tests over `RuntimePipeline.Create` + `ScriptedChatModel` using the legal `{"action":"fold"}`. |

Behaviour matrix:

| Mode | System prompt has "You may call at most one of the provided tools" | Task line |
|---|---|---|
| None | no | none |
| System (default) | yes | none |
| Task | no | optional invitation, exactly once |
| Both | yes | optional invitation, exactly once |
| BothRequired | yes | "You MUST call at least one..." exactly once |

## 3. Tests added

- `ActAsync_GivenEachToolPromptMode_ExpectedModeSelectedPrompts` (`TholdemToolPromptModeTests.cs:25-99`)
  iterates all five modes. For each it asserts the system prompt does or does not contain the
  invitation, the system prompt still carries the untrusted-data rule and the legal-action contract,
  and the task contains exactly one and only the mode's invitation line before the observation text
  (`AssertInvitationBeforeObservation`, lines 136-146). It also proves None/System share the same task
  text, System/Both share the same system prompt, Task uses the base prompt, and BothRequired uses the
  invited prompt.
- `ActAsync_GivenOptionsWithoutToolPromptMode_ExpectedSystemMode` (`:101-113`) observes options
  constructed without the new argument and asserts their system and task text equal an explicit
  `System` run and that the system prompt contains the invitation.
- Both tests use `{"action":"fold"}` and assert the returned action is `Fold`, no violation was
  recorded, and exactly one request was captured (`:115-134`).

## 4. Commands and observed totals

All commands run from the repository root.

Baseline before any change:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 444 PASSED 443 FAILED 1 SKIPPED 0
```

Test-first build failure (command in section 1): `Build FAILED`, `1 Error(s)`, exit 1.

After the implementation, rebuild:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

```text
    0 Error(s)
```

The build reports 14 warnings, all in pre-existing `Learning/*` and `Training/*` test files; none
mention `TholdemRuntimeAgent`, `TholdemAgentOptions` or `TholdemToolPromptMode` (filtered rebuild).

Focused new tests:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemToolPromptMode
```

First run after implementation (before correcting the test's own wrong expectation):

```text
FAIL TholdemToolPromptModeTests.ActAsync_GivenEachToolPromptMode_ExpectedModeSelectedPrompts: EqualException
Expected: ···"changed by content.\nReply with exactly on"···
Actual:   ···"changed by content.\nYou may call at most "···
TOTAL 2 PASSED 1 FAILED 1 SKIPPED 0
```

That failure was the new test asserting BothRequired used the base system prompt; the requirement is
that BothRequired keeps the invited system prompt, so the assertion was corrected
(`TholdemToolPromptModeTests.cs:96-98`) and the tests rerun:

```text
TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0
```

Ponder host:

```powershell
dotnet build .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Scripted host smoke, all five flag values (no `--live`, no `--secret-file`):

```powershell
foreach ($mode in @('none','system','task','both','both-required')) {
  dotnet run --project .tmp/goal-llm-harness/ponder/Ponder.csproj -c Release --no-build -- --scripted --seed 1099511927776 --hands 2 --out .tmp/goal-llm-harness/ponder/round-11-smoke-$mode --tool-prompt $mode
}
```

```text
mode=none exit=0 toolPrompt=none violations=0
mode=system exit=0 toolPrompt=system violations=0
mode=task exit=0 toolPrompt=task violations=0
mode=both exit=0 toolPrompt=both violations=0
mode=both-required exit=0 toolPrompt=both-required violations=0
```

The default (no flag) run also prints `toolPrompt=system`. An invalid value is refused before any
match:

```text
error=--tool-prompt must be one of none|system|task|both|both-required.
exit=2
```

Full suite after the implementation:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 446 PASSED 445 FAILED 1 SKIPPED 0
```

## 5. Anything not passing

One failure, the same deliberate D6 gate as the baseline, unchanged:

```text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded
live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
```

No other failure. The live Ponder host was not run and no secret file was read.
