# Baseline Witness: Checklist Item 15 (Unchecked)

Date (UTC): 2026-05-26T19:06:20.1705895Z

## External Immutable Source Anchor

Source file (outside requirements artifacts):
- tests/Wip.Modus.Tests/Hosting/ModusWipBridgeTests.cs
- source SHA256: 222e33b522b1b112c1635c95282aead068e558ddb32aaa3b4cb91a186550ba9e
- source LastWriteTimeUtc: 2026-05-26T19:00:18.4419740Z

Anchored source text proving separate-project builder import and runtime shell discovery behavior:

- line 15: public void ExternalPluginBuild_GivenSeparateProjectUsingBuilderApis_BuildsWithoutShellHostCodeChanges()
- line 39: public async Task ShellDiscovery_GivenExternalPluginAssembly_ListsRegisteredAgentValidatorAndWorkflow()

## Deterministic Unchecked Baseline Line

Normalization rule:
- UTF-8 text, LF line ending, no trailing spaces
- baseline line is formed as: "- [ ] " + checklist item text

Baseline unchecked line:

- [ ] Implement external sample plugin proving builder import in a separate project with typed agent, typed validator, and typed workflow discovered without shell-host code changes [depends on builder usability outside shell]

Baseline line SHA256:

c5521112eb7a03a3219b9f19f86b705a8c9c33a0d79ad83dcf701a4e083d2169

## Why This Is Independent

This witness does not rely on git history of .github/requirements/Wip.Shell-Builder-MVP-Next-Steps.md.
It anchors the baseline to executable behavior-proof source evidence and records a deterministic unchecked checklist line hash.
