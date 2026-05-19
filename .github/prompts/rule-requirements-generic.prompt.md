````prompt
---
agent: plan
description: Generic requirements rule - pre-configures requirements gathering for C# projects without migration-specific assumptions.
---

#file:.github/prompts/workflow-requirements-gathering.prompt.md

# Rule: Requirements Gathering (Generic)

## Purpose
Pre-configured specialization of workflow-requirements-gathering for a target C# project.
This rule avoids migration-specific assumptions and lets the caller define analysis source and mandatory items.

## Input (supplied by caller)

```
CsProject:      <Target C# project>
AnalysisSource: <How to derive checklist, for example invoke skill-impl-completeness or audit interfaces>
MandatoryItems: <Mandatory checklist entries with tags, or none>
PlanType:       <requirements | generic | architecture requirements>
```

## Bindings

Follow workflow-requirements-gathering with:

- CsProject: <from caller>
- AnalysisSource: <from caller>
- MandatoryItems: <from caller>
- PlanType: <from caller>
- OutputPath: .github/requirements/<CsProject>.md
- OutputTitle: # Requirements: <CsProject>
- ClosingMessage: Requirements gathering complete. Output saved to .github/requirements/<CsProject>.md. Run workflow-iterative-implementation and provide one checklist item at a time.

## Notes

- If MandatoryItems is none, skip mandatory-item enforcement.
- Keep test planning aligned to xUnit conventions unless caller specifies otherwise.
````
