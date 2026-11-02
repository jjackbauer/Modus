# Round 1 Verdicts — API Probe

Method: the orchestrator opened each cited source range; for `.tmp/bench` facts it re-parsed the JSON and file metadata with PowerShell. No code was built or run by the Observer; no counterexample was found.

| Batch | OBS rows | Verdict | Verification performed |
|---|---|---|---|
| Domain and identifiers | OBS-001..007 | Supported | Read the cited enum, id, and table-config ranges. |
| Artifact and effectiveness | OBS-008..010, 033..034 | Supported | Read the artifact ctor/validation and effectiveness ctor/record methods. |
| Script host | OBS-011..020 | Supported | Read the host ctor, `TryCompile`, result type, compiled strategy, namespace restrictions, and prologue. |
| Slot factory | OBS-022..026 | Supported | Read the factory ctor, `ResolveDispatchKind`, and `CreatePlayerSlot`. |
| Match engine | OBS-027..032 | Supported | Read `BonesMatchConfig`, `BonesMatchResult`, `RunMatch`, and round scoring/seed derivation. |
| Strategy library | OBS-035..043 | Supported | Read the library ctor, load/rank/upsert/candidate paths, atomic write, and ranking comparator. |
| Benchmark model and arena | OBS-044..064 | Supported | Read `Model.cs`, `Arena.cs`, `DeterministicOpponents.cs`, `Program.cs`, and the benchmark csproj. |
| Seed derivation | OBS-065..066 | Supported | Read `BonesSeedDerivation` signatures and non-negative output contract. |
| Test fixtures | OBS-067..070 | Supported | Read the library test temp-directory pattern and the `TempDirectory` helper. |
| History and arena inputs | OBS-071..075, 084 | Supported | Recomputed: `SessionCount=1973`, `StrategyArtifactCount=15096`, `DistinctContentPairCount=6106`, 65 strategies, 92,059 lines, 3,565,640 bytes, dev seed 910001, heldout seed 910002, 65/65 ids present in the 70-name field, 5 archetype extras. |
| Serialization and CLI | OBS-076..078, 083, 085 | Supported | Read the serializer options and command switch; `rg` over `WIP/Wip.Bones.Benchmark` found no `BonesStrategyLibrary` or `Wip.Bones.Agents.Knowledge` reference. |
| Gate fixed-seat helper | OBS-079..082 | Supported | Read `GateStudy.RunEvaluationMatch` and `CreateSlot`; confirmed `private static` and fixed learning seat. |

**Falsified: 0 rows.** The only limitation is that the Observer did not execute builds, tests, or matches; those are execution questions, not source contradictions.