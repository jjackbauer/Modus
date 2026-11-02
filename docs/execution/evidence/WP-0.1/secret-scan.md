# WP-0.1 — Secret scan (redacted)

**Scanned:** 2026-11-02T10:42:00-03:00
**Scope:** 148 modified tracked files + 522 untracked non-ignored files, filtered to text extensions (656 files scanned).
**Rule:** no matched value is reproduced in this evidence. Only path, line, pattern name, and a placeholder heuristic are recorded.

## Patterns

- OpenAI-style key: `sk-[A-Za-z0-9_-]{20,}`
- AWS access key: `AKIA[0-9A-Z]{16}`
- GitHub token: `gh[pousr]_[A-Za-z0-9]{20,}`
- Assigned secret: `(?i)(api[_-]?key|apikey|secret|password|passwd|token)\s*[:=]\s*["'][^"']{12,}["']`
- Environment secret: `(?i)(DEEPSEEK|OPENAI|ANTHROPIC|AZURE)_[A-Z_]*(KEY|TOKEN|SECRET)\s*[:=]\s*\S{8,}`

## Findings (3 files, 7 pattern hits)

| File | Line(s) | Pattern | Classification |
|---|---|---|---|
| `WIP/Wip.Bones.Host/README.md` | 165, 170 | assigned-secret, env-secret | **Documentation placeholder** (example environment assignment; placeholder-like). Not a credential. |
| `WIP/Wip.Bones.Web.Tests/Viewer/BonesViewerLearningLoopStatusScriptTests.cs` | 519, 541 | openai-style key | **Pre-existing test fixture at HEAD** (the exact token is present in `git show HEAD:…`). The test injects a fake provider payload and asserts the secret never appears in rendered HTML. Not introduced by the working tree. |
| `WIP/Wip.Runtime.Tests/Runtime/DeepSeekModelProviderTests.cs` | 226 | assigned-secret | **Test placeholder** (placeholder-like). Not a credential. |

## Added-line scan

A separate scan of all added diff lines (`git diff -U0`, lines beginning with `+`) found **0** secret-shaped matches. The working tree therefore introduces no new secret-looking value.

## Residual risk

- Pattern scanning is not proof of absence. A real secret that does not match these shapes (for example, a bare opaque token assigned without a keyword) would not be caught.
- The test fixture key at line 519 is already committed at HEAD; if the repository is ever published externally, its provenance should be confirmed by the owner even though it is used as a negative-test fixture.
- No environment variables or `appsettings` values were printed; the modified `WIP/Wip.Bones.Host/appsettings.json` was scanned as a whole file and produced no hit.

## Recommendation

1. Human reviewer confirms the three files above during the baseline review (no values needed; the paths are sufficient).
2. Do not place real credentials in `appsettings.json`; the existing configuration uses environment references.
3. Re-run this scan immediately before the baseline commit, because the index will be built from the same working tree plus any late changes.

## Commands

- Scan implementation: PowerShell `[regex]::IsMatch` over `git diff --name-only` + `git ls-files -o --exclude-standard`, text extensions only.
- Added-line scan: `git diff -U0` parsed by file; matched lines counted, not printed.
