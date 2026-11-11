# Round 26 design — signature-guarded step proposer

**Objective:** a conservative proposal mechanism: signatures choose the parameter/direction, but the
step stays at the deterministic 0.02 instead of the diagnosis policy's 0.08 jump.

## API

~~~csharp
public sealed class SignatureGuardedProposer : IStrategyProposer
{
    public const double Step = 0.02;
    public SignatureGuardedProposer(SessionSignatures signatures, IStrategyProposer? fallback = null);
    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration);
}
~~~

Rules:
- If a fold-to-raise signal is observed (Sample >= 10) and its direction is high, lower the call
  threshold by Step (clamped); if that edit is behaviorally inert, fall back to the deterministic
  proposer.
- Every other signal, an empty signature set, or a below-floor signal falls back to the deterministic
  proposer, so the class is strictly a conservative overlay.

## Tests — WIP/Wip.Tholdem.Tests/Learning/SignatureGuardedProposerTests.cs (four)

1. Propose_GivenFoldToRaiseHigh_LowersCallByDeterministicStep (0.45 -> 0.43).
2. Propose_GivenEmptySignatures_EqualsDeterministicProposer.
3. Propose_GivenBelowSampleFloor_EqualsDeterministicProposer.
4. Propose_GivenSameInputs_IsDeterministic.

## Forbidden

Do not change the diagnosis policy, the deterministic proposer, existing tests, or the evaluator.
No ambient randomness.
