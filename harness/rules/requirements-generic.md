---
kind: rule
mode: plan
description: Generic requirements rule - pre-configures requirements gathering for C# projects without migration-specific assumptions.
composes:
  - ../workflows/requirements-gathering.md
  - verification-absolute-behavior.md
---

# Rule: Requirements Gathering (Generic)

## Purpose
Pre-configured specialization of requirements-gathering for a target C# project.
This rule avoids migration-specific assumptions and lets the caller define analysis source and mandatory items.

## Input (supplied by caller)

```
CsProject:      <Target C# project>
AnalysisSource: <How to derive checklist, for example invoke impl-completeness or audit interfaces>
MandatoryItems: <Mandatory checklist entries with tags, or none>
PlanType:       <requirements | generic | architecture requirements>
```

## Bindings

Follow requirements-gathering with:

- CsProject: <from caller>
- AnalysisSource: <from caller>
- MandatoryItems: <from caller>
- PlanType: <from caller>
- OutputPath: harness/requirements/<CsProject>.md
- OutputTitle: # Requirements: <CsProject>
- ClosingMessage: Requirements gathering complete. Output saved to harness/requirements/<CsProject>.md. Run iterative-implementation and provide one checklist item at a time.

## Notes

- If MandatoryItems is none, skip mandatory-item enforcement.
- Keep test planning aligned to xUnit conventions unless caller specifies otherwise.
- Enforce absolute behavior-proof verification for every checklist item and test plan section.
- Metadata-only assertions are not valid completion evidence for any plan item.