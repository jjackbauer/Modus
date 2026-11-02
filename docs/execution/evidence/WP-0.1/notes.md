# WP-0.1 — Notes and five-line implementation note

## Five-line implementation note (contract §4 Step 2)

1. **Current behavior:** branch `WIP` at `d6b895f` carries 148 modified + 160 untracked porcelain entries (522 untracked files); no baseline tag, no `docs/execution/`, no WP-0.1 inventory.
2. **Intended behavior:** freeze that tree as the reviewed baseline with one commit and an annotated tag, record test inventory/known failures/waivers, and leave the tree clean except intentional untracked files.
3. **Files to change:** this turn only `docs/execution/` evidence; after approval, `.gitignore` (scratch/evidence-log rules) and the baseline commit itself.
4. **Test strategy:** WP-0.1 has no product behavior to test; verification is executable inventory (`git status`, tag existence, solution coverage, build/test inventory). No red test is applicable; the equivalent check is the pre-commit review and the post-commit build/test inventory.
5. **Rollback/feature flag:** no product code change and no flag. The baseline commit is reversible with `git revert`; the working tree was not reset or cleaned; no push.

## Actions this turn

- Read the execution contract, the plan's Phase 0/G0 sections, the report's baseline evidence, and repository rules.
- Captured a read-only pre-flight: branch, HEAD, dirty-tree counts, diffstat, solution coverage, test inventory, encoding tooling, CI workflow, guard presence.
- Created `docs/execution/` evidence only. No user file was modified, moved, or deleted; no git state-changing command was run.
- Stopped at the G0 baseline decision, per the contract's dirty-tree rule and the kickoff instruction.

## Why the stop is required

The plan's WP-0.1 first task is "Commit/stash the 308-path working tree after review". The execution contract forbids committing the user's work without explicit approval, and the kickoff says "Preserve the current working tree; stop at G0 with a baseline packet." The baseline strategy (commit vs stash vs branch vs manual review) changes repository history and must be a human decision at G0.

## Open questions for the decision

1. Approve the curated baseline commit (**Option A**) with the exclusions in the gate packet, or choose another strategy?
2. Should the 46 `goal-bones-*` session documents and the two PDFs be part of the baseline commit, or archived outside the product history?
3. Should the 207 KB untracked `bones-data/.bones/pool/pool-snapshot.json` runtime state remain untracked (recommended) or be committed?
4. Confirm the annotated tag name `wip-baseline-2026-11-02` (plan §3.2) and that no push is authorized.

## Residual risks

- A single baseline commit of 8 MB of mixed in-flight work is less reviewable than several logical commits; the plan explicitly asks for one baseline freeze, so a single commit is the smallest coherent action. Splitting would be a scope change.
- The transcript cannot prove the user wants every modified runtime JSON (strategy library seats) in the baseline; they are already tracked and modified, so the baseline captures their current state. The alternative is a partial commit, which would leave the tree dirty and fail WP-0.1.
- Contract-required evidence logs are now re-included by `!docs/execution/**/*.log`; no force-add should be needed.
- No build/test claim is made in this packet. The `dotnet test` attempt is blocked at testhost startup (F-ENV, `BLK-TESTHOST-ENV`); the inventory remains static until tests can run on an unconfined host/CI.

## Encoding

All new evidence Markdown/JSON files are written as UTF-8 without BOM and will be byte-checked before commit (see verify log after approval).
