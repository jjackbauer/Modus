# Mutation evidence - defense removed -> focused control fails -> restored by SHA-256

Each row is a deliberate one-line removal of one defense, followed by a Release build and the
focused control type. The observed failing test and the runner's `TOTAL` line are captured from the
run. The file is then restored byte-for-byte and its SHA-256 is compared with the pre-mutation value.
This is the same procedure used for the A1-A15 controls in `docs/tholdem/rsi-threat-model.md`.

| ID | Defense removed | Focused control | Observed | Restored |
|---|---|---|---|---|
| A2 | seed-reuse admission (TrainingSeedRegistry call) | TrainingJobApiTests | exit 1; FAIL TrainingJobApiTests.Post_GivenSeedReuseWithDifferentOptions_Expected409: EqualException: Assert.Equal() Failure: Values differ; TOTAL 10 PASSED 9 FAILED 1 SKIPPED 0 | 80e01c37fd05... (True) |
| A3 | verifier margin/seed checks (PromotionVerifier.Verify) | ThreatControlTests | exit 1; FAIL ThreatControlTests.AlwaysPromoteControl_GivenReportBelowGate_ExpectedVerificationFailedAndNoPointerMove: EqualException: Assert.Equal() Failure: Strings differ; TOTAL 9 PASSED 4 FAILED 5 SKIPPED 0 | 72252cdd9bdb... (True) |
| A4 | ledger hash-chain verification (LedgerChain.Verify) | ThreatControlTests | exit 1; FAIL ThreatControlTests.Ledger_GivenTamperedEntry_ExpectedIntegrityFalseAndNextRunRefused: FalseException: Assert.False() Failure; TOTAL 9 PASSED 7 FAILED 2 SKIPPED 0 | 17449f7a07da... (True) |
| A5 | champion-vs-itself refusal (MetaLoop sameChampionEverywhere) | MetaLoopTests | exit 1; FAIL MetaLoopTests.Run_GivenIdenticalFinalChampions_ExpectedSelfComparisonRefusal: EqualException: Assert.Equal() Failure: Strings differ; TOTAL 12 PASSED 11 FAILED 1 SKIPPED 0 | 5a385d7f191e... (True) |
| A6/A15 | fresh-holdout non-inferiority ratchet (MetaLoop BuildDecision) | MetaLoopTests | exit 1; FAIL MetaLoopTests.Run_GivenCandidateWinsOnMetaSeedsButLosesFreshHoldout_ExpectedRefusal: InvalidOperationException: The meta decision is not reproducible: decision=eligible, verif...; TOTAL 12 PASSED 11 FAILED 1 SKIPPED 0 | 5a385d7f191e... (True) |
| A7 | inert-proposal guard (TrainingIteration behavior check) | ThreatControlTests | exit 1; FAIL ThreatControlTests.InertProposal_GivenGuard_ExpectedNoEvaluationFileAndInertOutcome: NullException: Assert.Null() Failure: Value is not null; TOTAL 9 PASSED 8 FAILED 1 SKIPPED 0 | 9036bef9e7ca... (True) |
| A8 | server-side promote confirmation check (TrainingEndpoints) | TrainingPromoteApiTests | exit 1; FAIL TrainingPromoteApiTests.Promote_GivenNoConfirm_Expected409ConfirmationRequired: EqualException: Assert.Equal() Failure: Values differ; TOTAL 5 PASSED 4 FAILED 1 SKIPPED 0 | 3b4a3bdf8d4e... (True) |
| A10 | meta inner-iteration budget (MetaBudget.ConsumeEpisode) | MetaLoopTests | exit 1; FAIL MetaLoopTests.Run_GivenInnerBudgetTooSmall_ExpectedBudgetExceptionAndPointerUnchanged: ThrowsException: Assert.Throws() Failure: No exception was thrown; TOTAL 12 PASSED 11 FAILED 1 SKIPPED 0 | 5a385d7f191e... (True) |
| A11 | weak-seed iteration cap (TrainingJobValidator) | TrainingJobApiTests | exit 1; FAIL TrainingJobApiTests.Post_GivenWeakSeedWithoutOptInOrOverTheCap_Expected400: EqualException: Assert.Equal() Failure: Values differ; TOTAL 10 PASSED 9 FAILED 1 SKIPPED 0 | cf8626ec742e... (True) |
| A12 | clock-free deterministic ledger entries (TrainingLoop.BuildLedgerEntry) | TrainingJobApiTests | exit 1; FAIL TrainingJobApiTests.Post_GivenSameOptionsInTwoDirectories_ExpectedIdenticalArtifactHashes: EqualException: Assert.Equal() Failure: Strings differ; TOTAL 10 PASSED 9 FAILED 1 SKIPPED 0 | 1e77e81c8bcc... (True) |
| A14 | frozen-manifest comparison (FrozenComponents.Matches) | ThreatControlTests | exit 1; FAIL ThreatControlTests.FrozenComponents_GivenTamperedBehaviorHash_ExpectedMismatchReason: FalseException: Assert.False() Failure; TOTAL 9 PASSED 8 FAILED 1 SKIPPED 0 | b1c85d37d89d... (True) |
