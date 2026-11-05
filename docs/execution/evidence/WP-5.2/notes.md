# WP-5.2 — privacy classification, redaction and retention

**Status:** complete 2026-11-05 (local verification; authoritative Tests CI confirmation on the
commit that carries this file).

## Delivered

- **Contracts** (`Wip.Abstractions/Privacy/PrivacyContracts.cs`): `DataClassification`
  (Public/Internal/Confidential/Secret), `RedactionResult`, `ISecretRedactor`, `RetentionPolicy`
  (with validation), `RetentionPurgeResult`, `RetentionStatus`.
- **Redaction** (`Wip.Runtime/Runtime/SecretRedactor.cs`): provider-style `sk-…` keys, `Bearer`
  tokens, secret-name assignments (`apiKey`, `accessToken`, `authToken`, `secret`, `password`,
  `clientSecret`; name preserved), PEM private-key blocks; optional additional patterns; idempotent.
- **Memory integration** (`ArtifactAgentMemory`): message content and tool-call arguments are
  redacted before `agent-memory.json` is written and again on load.
- **Retention** (`Wip.Runtime/Runtime/SessionArtifactRetention.cs`): status (sessions, artifacts,
  bytes, oldest) and purge (regular files under `<repo>/.wip/sessions/*/artifacts` older than the
  boundary; resolved-path confinement; locked files retained and reported).
- **Shell command** `retention [status|purge [days]]` (global), with help text and no-session
  guidance updated; default 30 days.
- **Docs**: `docs/security/data-classification.md` (classification, redaction rules, retention
  limits) and the operator guide/test status.

## Verification

- `SecretRedactorTests` (5) and `SessionArtifactRetentionTests` (3) pass.
- `ArtifactAgentMemoryTests.Append_GivenMessageContainingApiKey…` proves the persisted artifact no
  longer contains the key/bearer token and contains `[REDACTED]`.
- Shell `RetentionCommand_GivenOldArtifact…` proves `retention status` output and `purge 30`
  deletes the old artifact while retaining the recent one.
- **Controls:** a no-op `SecretRedactor` makes the redaction tests fail; ignoring the age boundary in
  purge makes the retention test fail.
- Full suites: `Wip.Runtime.Tests` **146** (139 pass; 7 documented environment/waiver failures),
  `Wip.Shell.Tests` **34/34**. Waiver expected totals updated.

## Limits

- Pattern-based redaction can miss unknown credential shapes; extend the pattern set when needed.
- Retention is operator-driven; no background scheduler yet.
