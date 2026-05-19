---
agent: agent
description: Re-analyze production and test C# files to extract or update the codebase patterns reference document.
---

#file:.github/prompts/skill-falsify-claims.prompt.md

# Skill: Codebase Patterns Regeneration

## Purpose

Re-analyze all production and test C# files in the workspace to extract or update
.github/prompts/codebase-patterns.md. Run this skill when code conventions evolve,
new modules/plugins are added, or the reference drifts from implementation reality.

## Depends On

- .github/prompts/codebase-patterns.md
- .github/prompts/skill-falsify-claims.prompt.md

## Procedure

### Step 1 - Catalog production patterns

Read all production .cs files and extract:

1. Project and namespace structure
2. Module boundaries and dependency direction
3. Plugin contract design and versioning patterns
4. Class/member naming and visibility conventions
5. Constructor and DI patterns
6. Error handling and guard clause conventions
7. Thread-safety and shared-state patterns
8. Host startup and plugin registration lifecycle patterns

### Step 2 - Catalog test patterns

Read all test .cs files and extract:

1. Test file layout and namespace style
2. Test naming convention adherence
3. xUnit Fact/Theory usage patterns
4. Assertion style and helper usage
5. Coverage categories (contracts, lifecycle, failures, integration)
6. Setup/teardown or fixture usage patterns

### Step 3 - Diff against existing reference

If codebase-patterns.md exists, compare extracted patterns against documented rules.
Flag:

- New patterns
- Contradictions
- Inconsistencies

### Step 4 - Falsify Claims

Run Falsify Claims on every candidate pattern from Step 3.
Only Supported patterns are kept.

### Step 5 - Write updated reference

Update .github/prompts/codebase-patterns.md with a clear sectioned structure for:

1. Project structure
2. Dependency and composition model
3. Module and plugin contract patterns
4. Error handling and thread safety patterns
5. Test structure and standards
6. Anti-patterns to avoid

## Output

Updated .github/prompts/codebase-patterns.md with a short trailing note:
regeneration date and summary of changes.
