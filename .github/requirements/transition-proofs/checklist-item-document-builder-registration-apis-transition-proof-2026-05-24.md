# Checklist Transition Proof: Builder Registration APIs

Date: 2026-05-24
Checklist item:

Document Builder registration APIs including duplicate capability replacement behavior and workflow/policy registration constraints [depends on Abstractions contract documentation]

## Baseline Unchecked Source Text Evidence

Exact unchecked checklist text captured in-workspace:

- [ ] Document Builder registration APIs including duplicate capability replacement behavior and workflow/policy registration constraints [depends on Abstractions contract documentation]

Deterministic source reference:

- .github/requirements/transition-proofs/baselines/checklist-item-document-builder-registration-apis.unchecked.txt:1

Deterministic fingerprint:

- Baseline unchecked text SHA256: C452C3DF1C7C86569BB17D3C41F89C67600A24A95EAA12B54832120EC01B042E

## Checked Completion Evidence

The checked checklist text for this exact item is:

- [x] Document Builder registration APIs including duplicate capability replacement behavior and workflow/policy registration constraints [depends on Abstractions contract documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-builder-registration-apis-transition-proof-2026-05-24.md]

Deterministic workspace evidence:

- Requirements file checked line: .github/requirements/WIP.Contributor-Readmes.md:84
- Requirements line capture: - [x] Document Builder registration APIs including duplicate capability replacement behavior and workflow/policy registration constraints [depends on Abstractions contract documentation] [transition-proof: .github/requirements/transition-proofs/checklist-item-document-builder-registration-apis-transition-proof-2026-05-24.md]
- Requirements SHA256: EE5764DDF5B36CD31374AAB77735A0DDFFE151EDDB70C72149C7A4C3F8836BD7

Transition proof self-reference:

- Proof artifact path: .github/requirements/transition-proofs/checklist-item-document-builder-registration-apis-transition-proof-2026-05-24.md
- This file line with exact unchecked text: 12
- This file line with exact checked text: 26

Supporting implementation and tests for this item:

- src/Wip.Builder/README.md documents builder registration APIs, duplicate capability replacement behavior, and workflow/policy registration constraints.
- tests/Wip.Builder.Tests/Builder/BuilderReadmeRegistrationContractsTests.cs contains behavior-proof tests:
  - BuilderReadme_GivenDuplicateCapabilityIdWithoutReplacement_RegistrationFailsDeterministically
  - BuilderReadme_GivenDuplicateCapabilityIdWithReplacementEnabled_NewDescriptorReplacesOldDescriptor
  - BuilderReadme_GivenInferredCapabilitySignatureAmbiguity_RegistrationFailsWithSingleSignatureRequirement
  - BuilderReadme_GivenWorkflowDisplayNameWhitespace_RegistrationFailsWithDisplayNameConstraint
  - BuilderReadme_GivenPolicyAndWorkflowRegistration_ResolvesAsSingletonsAndTracksTypedMetadata
