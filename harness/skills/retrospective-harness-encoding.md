---
kind: skill
mode: agent
description: After a task completes, reflect on failures and friction to create or update harness rules/skills that prevent recurrence. Encodes hard-won lessons into the agent's permanent capability set.
---

# Skill: Retrospective Harness Encoding

## Purpose

After completing a non-trivial task (especially one that involved failed attempts, debugging, or unexpected friction), analyze what went wrong and encode the lessons as harness rules or skills. This turns one agent's painful experience into permanent guardrails for all future agents.

## Trigger

Invoke this skill when:

- A task required 3+ repair rounds or dead-end attempts
- The agent modified the wrong class or dead code
- A framework behavior (DI, runtime path, file locking) caused silent failures
- User data was accidentally destroyed or modified
- Unit tests passed but runtime integration failed
- A fix that "should have worked" didn't

## Procedure

### Step 1 — Collect failure events

List every distinct failure mode encountered during the task:

| # | What happened | Why it happened | Root cause category |
|---|--------------|-----------------|---------------------|
| 1 | Modified wrong class, change had no effect | Didn't trace the runtime path first | Process gap |
| 2 | DI factory registration silently ignored | Framework overrides with type-based registration | Framework knowledge |
| 3 | User data deleted by test script | Destructive command in test setup | Safety gap |

### Step 2 — Classify each failure

| Category | Response |
|----------|----------|
| **Process gap** | Create a new `harness/skills/` that formalizes the missing step |
| **Framework knowledge** | Create a `harness/rules/` that encodes the known pitfall |
| **Safety gap** | Create a `harness/rules/` with an absolute prohibition |
| **Verification gap** | Create a `harness/rules/` that mandates a verification step |

### Step 3 — Create the harness file

For each failure, create exactly one harness file:

**Skills** (`harness/skills/<name>.md`):
```yaml
---
kind: skill
mode: agent
description: <one-line summary>
composes:
  - <dependencies>
---
# Skill: <Title>
## Purpose
<What problem this prevents>
## Hard Rules
<Non-negotiable constraints>
## Procedure
<Step-by-step>
```

**Rules** (`harness/rules/<name>.md`):
```yaml
---
kind: rule
mode: agent
description: <one-line summary>
---
# Rule: <Title>
## The Problem
<What happens when this is violated>
## The Rule
<The absolute constraint>
## Detection
<How to spot this happening>
## Correct Approach
<What to do instead>
```

### Step 4 — Wire into compose chains

Every rule or skill is dead unless it appears in a `composes:` block of a skill or workflow that is actually invoked.

1. Identify which existing skills/workflows would have prevented this failure.
2. Add the new rule/skill to their `composes:` list.
3. Use relative paths: `../rules/rule-name.md` or `../skills/skill-name.md`.

### Step 5 — Verify wiring

Read back the modified `composes:` blocks to confirm paths resolve correctly.

### Step 6 — Report

Output a table of what was created and where it's wired.

## Output Format

```markdown
## Retrospective Harness Encoding

### Failures analyzed
| # | Failure | Category | Encoded as |
|---|---------|----------|------------|
| 1 | ... | Process gap | skills/trace-runtime-path.md |
| 2 | ... | Framework knowledge | rules/di-factory-override.md |
| 3 | ... | Safety gap | rules/protect-user-workspace-data.md |

### Wiring
| Rule/Skill | Composed into |
|------------|--------------|
| rules/di-factory-override.md | iterative-implementation-orchestrator, iterative-implementation |
| skills/trace-runtime-path.md | iterative-implementation |

### Gaps remaining
<Any failure modes not yet addressed, and why>
```
