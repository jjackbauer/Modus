---
kind: skill
mode: agent
description: Audit a C# class for thread-safety gaps and parallelization opportunities, gated by Falsify Claims and Impl Completeness.
composes:
  - falsify-claims.md
  - impl-completeness.md
---

## Input

```
CsProject:  <project folder, e.g. Lovelace.Natural>
CsFile:     <specific class file, e.g. Natural.cs â€” if omitted, all *.cs in CsProject are read>
```

---

## Procedure

### Step 1 â€” Enumerate the class surface

Read all `*.cs` files in `<CsProject>/`. List every method (public + private), field, property, and auto-property. Note accessibility, and whether each is `static`, `readonly`, or mutable.

### Step 2 â€” Build the Data Mutability Table

For each field/property:

| Member | Type | Mutable? | Shared across calls? | Thread-safety risk |
|---|---|---|---|---|
| `_digits` (`byte[]`) | reference type | Yes | Yes | High |
| `IsZero` (internal set) | `bool` | Yes | Yes | Medium |
| ... | | | | |

Risk ratings:
- **None** â€” read-only or value-type local
- **Low** â€” write-once (set in constructor only)
- **Medium** â€” reassigned after construction
- **High** â€” mutated in place (arrays, collections, counters)

### Step 3 â€” Build the Sequential Dependency Graph (per method)

For each method, identify:
- (a) which shared mutable members it reads / writes
- (b) whether it calls other methods on `this` that also mutate shared state
- (c) whether any loops contain independent iterations (no carry chain or cross-iteration data dependency)

Record each of these observations as a claim for Step 4.

### Step 4 â€” Run Falsify Claims on all data-access and dependency claims

Collect every claim from Steps 2â€“3 into a numbered list and invoke the **Falsify Claims** skill. Revise until **zero Falsified rows** remain before proceeding.

### Step 5 â€” Thread Safety Assessment Table

For each piece of shared mutable state, recommend a concrete .NET mechanism. This phase must be completed before any parallelization is applied.

| Member | Risk | Recommendation | Priority |
|---|---|---|---|
| `_digits` (`byte[]`) | High | Wrap all mutations in `lock (_syncRoot)`; use `Interlocked` for atomic scalar counters | P0 |
| `IsZero` (internal set) | Medium | Update always under the same lock as `_digits` writes | P0 |
| ... | | | |

Priority legend:
- **P0** â€” must fix before any parallelization (correctness blocker)
- **P1** â€” should fix (race condition under concurrent read+write)
- **P2** â€” optional improvement (contention reduction, e.g. `ReaderWriterLockSlim`)

### Step 6 â€” Parallelization Opportunity Table

For each method (and notable inner loop), classify:

| Method / Loop | Shared Writes | Iterations Independent? | Parallelizable? | Suggested .NET API |
|---|---|---|---|---|
| `Add` carry propagation loop | `_digits[i]` | No (carry chain) | âŒ Sequential | â€” |
| `Multiply` partial-products loop | partial sums | Yes (after operand split) | âœ… With reduction | `Parallel.For` + `Interlocked.Add` |
| `ToString` digit-extraction loop | none (read-only) | Yes | âœ… Embarrassingly parallel | `AsParallel()` / `Span` slicing |
| ... | | | | |

### Step 7 â€” Coverage check via Impl Completeness skill

Invoke the **Impl Completeness** skill with `CsProject` to confirm that every method appearing in the C# class was included in the tables above. Flag any omissions as â¬œ Missing from audit.

### Step 8 â€” Produce the Improvement Checklist

Ordered by dependency: thread safety before parallelization, prerequisites before dependents.

```
## Parallelization Audit Checklist for `<CsProject>.<ClassName>`

### Phase 0 â€” Thread Safety (complete before Phase 1)
- [ ] `_digits`: wrap all writes in `lock (_syncRoot)` [P0 â€” prerequisite for all parallelization]
- [ ] `IsZero`: ensure update is atomic under the same lock [P0]
- [ ] ...

### Phase 1 â€” Parallelization
- [ ] `Multiply` â€” parallelize partial-products with `Parallel.For` + reduction [depends on Phase 0]
- [ ] `ToString` â€” use `AsParallel()` on read-only digit extraction [independent of Phase 0]
- [ ] ...
```

---

## Output

Write all results to `harness/requirements/<CsProject>-parallelization-audit.md` using the structure below, then confirm the file path to the caller.

```markdown
# Parallelization Audit â€” `<CsProject>.<ClassName>`

## 1. Data Mutability Table
<!-- Step 2 table -->

## 2. Sequential Dependency Graph
<!-- Step 3 narrative -->

## 3. Falsify Claims Result
<!-- Step 4 table â€” confirm zero Falsified rows -->

## 4. Thread Safety Assessment
<!-- Step 5 table -->

## 5. Parallelization Opportunities
<!-- Step 6 table -->

## 6. Impl Completeness Coverage
<!-- Step 7 findings â€” flag any â¬œ Missing from audit -->

## 7. Improvement Checklist
<!-- Step 8 checklist â€” Phase 0 then Phase 1 -->
```

---

## Design Notes

- **Always writes output** â€” saves the audit report to `harness/requirements/<CsProject>-parallelization-audit.md` and confirms the path; unlike pure `skill-*` prompts this one persists results so they can be referenced by follow-up workflows.
- **`impl-completeness` as coverage gate** (Step 7) â€” referenced, not re-implemented; consistent with how `falsify-claims` is used in other skills.
- **Phase 0 / Phase 1 labels** â€” make the sequencing constraint (make thread-safe *before* parallelizing) visually unambiguous.
- **`CsFile` is optional** â€” defaults to all `*.cs` in the project, keeping input lightweight and matching the `CppClass`/`CsProject` pattern used elsewhere.
- **`async`/`await` patterns** â€” out of scope for this skill; treat `Task`-returning methods as a separate audit concern.
