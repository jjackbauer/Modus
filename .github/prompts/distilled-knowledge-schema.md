# Distilled Knowledge Schema

> Usage: include this file in prompts that read or write distilled docs under .github/distilled/.
> This is a reference-only document.

---

## 1. Header Template

Every distilled file begins with:

```markdown
# {Document Title}

> **Scope**: {Area covered}
> **Confidence**: {High | Medium | Low}
> **Last updated**: {YYYY-MM-DD}
> **Source entries**: {Journal IDs, for example OBS-001, VAL-002}
```

---

## 2. Uncertainty Markers

Each factual claim must start with one marker:

| Marker | Meaning | Requirement |
|---|---|---|
| ✅ | Verified | Supported VAL evidence exists |
| ⚠️ | Tentative | OBS evidence exists but no Supported VAL |
| ❓ | Unverified | Inferred, incomplete, or weak evidence |

Rules:
- Every factual claim has exactly one marker.
- Include supporting journal IDs inline.
- Promote or demote markers only with evidence changes.

---

## 3. Update Criteria

Update a distilled doc when:

1. A claim is falsified or revised.
2. A tentative claim gains Supported validation.
3. Completeness review reveals a gap.
4. New observations extend coverage.

Update procedure:

1. Identify trigger and affected claims.
2. Apply marker updates.
3. Update inline citations.
4. Refresh header fields and date.
5. Add DEC entry for major synthesis decisions.

---

## 4. Recommended Distilled Docs

| Document | Scope |
|---|---|
| system-overview.md | High-level architecture and boundaries |
| module-map.md | Module responsibilities and interfaces |
| domain-concepts.md | Core domain concepts and invariants |
| runtime-flows.md | Startup, request, and lifecycle flows |
| dependencies.md | Internal and external dependencies |
| invariants-and-risks.md | Invariants and known risks |
| trusted-facts.md | High-confidence verified claims only |
| unresolved-areas.md | Gaps, weak evidence, and open questions |
| glossary.md | Domain terminology and contract vocabulary |

Special rules:

- trusted-facts.md may contain only ✅ claims.
- unresolved-areas.md should prioritize items that block convergence.
