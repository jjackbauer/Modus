# Data classification, redaction and retention (WP-5.2)

**Status:** implemented. Redaction is applied before agent memory is persisted; retention is a
configuration/purge command. No release claim.

## Classification

| Class | Examples in WIP | Handling |
|---|---|---|
| **Public** | docs, README, architecture diagrams | may be published |
| **Internal** | repository structure, build logs, test names | repo/CI only |
| **Confidential** | task prompts, model answers, session transcripts, artifact metadata | redacted in memory; purge-eligible |
| **Secret** | API keys, bearer tokens, private keys, credentials in prompts/tool output | never persisted unredacted; never logged |

Session artifacts are **Confidential** by default; an artifact that could embed credentials must be
treated as **Secret** and redacted before persistence.

## Redaction rules

`Wip.Runtime.Runtime.SecretRedactor` (default implementation of `ISecretRedactor`):

- provider-style keys (`sk-…`),
- `Bearer <token>` values,
- assignments for `apiKey`, `accessToken`, `authToken`, `secret`, `password`, `clientSecret`
  (the name is preserved, the value becomes `[REDACTED]`),
- PEM private-key blocks.

Redaction is deterministic and idempotent. `ArtifactAgentMemory` redacts message content and
tool-call arguments before writing `agent-memory.json` and again on load (defence in depth for older
artifacts). The DeepSeek adapter never includes the API key in requests after the header is built and
never in exception messages.

Additional patterns can be supplied to `SecretRedactor` (configurable extension point); they are
evaluated after the built-ins.

## Retention

- Default policy: **30 days** for files under `<repository>/.wip/sessions/*/artifacts`.
- `RetentionPolicy.Disabled` keeps artifacts indefinitely.
- Shell commands:
  - `retention status` — session count, artifact count, total bytes, oldest artifact (UTC).
  - `retention purge [days]` — deletes regular files older than the boundary and reports
    `deleted`, `bytes`, `retained`.
- Purge only touches the direct artifact directories of the repository; resolved paths are checked
  to stay inside each artifacts root, and locked/unauthorised files are retained and reported.
- Empty artifact directories are left in place; no session metadata is deleted.

## Limits

- Redaction is pattern-based; a credential in an unrecognised format can pass through. Treat
  anything a model or tool emits as untrusted and add a pattern when a new credential shape appears.
- Retention is operator-driven (no background scheduler yet); it must be run by the operator or the
  deployment job.
- The shell's own console transcript is not persisted by WIP; terminal scrollback is outside this
  policy.
