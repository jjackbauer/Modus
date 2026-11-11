# Round 18 design — D5 diagnostics repair slice (item 3a)

**Objective:** fix the two cheapest, highest-leverage diagnostics defects without changing any policy
behavior: fold-to-raise must reflect the *outstanding* aggressive action, and zero observations must
be distinguishable from zero failures.

## 1. FailureSignatureExtractor.FacedRaise

Replace the previous-action test with a per-street outstanding-aggression scan:
- reset the outstanding flag when the street changes;
- an opponent Bet/Raise/AllIn sets it;
- a seat-0 action while it is set counts as facing aggression (fold if that action is Fold), then
  clears it;
- a hand contributes at most one sample, so multiple decisions cannot double count.

This preserves the existing synthetic fixture's sample of 3 while counting a fold after an
intervening caller that the old previous-action test missed.

## 2. FailureSignal.Observed

Add a computed property without changing the positional record or the canonical JSON:
~~~csharp
public bool Observed => Sample > 0;
~~~
Zero observations now have Sample 0, Count 0, Observed false; a sampled zero-failure hand has
Observed true and Count 0. No canonical bytes change.

## 3. Tests — WIP/Wip.Tholdem.Tests/Learning/DiagnosticsRepairTests.cs (three)

1. Extract_GivenCallBetweenRaiseAndFold_CountsFoldToRaise: actions seat1 Raise, seat0 Call,
   seat1 Raise, seat2 Call, seat0 Fold -> fold-to-raise Count 1, Sample 1.
2. Extract_GivenNoHands_AllSignalsUnobserved: Extract(empty) -> six signals, all Sample 0, Count 0,
   Observed false, Rate 0.
3. Extract_GivenCheckedHand_SampledZeroFailuresAreObserved: one checked-down hand with a postflop
   action -> aggression Observed true with Count 0; fold-to-raise Observed false.

Build synthetic HandRecord values directly (HandNumber, blinds, hole cards, board, actions,
showdown, winners, finalStacks), using TestCards.Parse for cards.

## 4. Forbidden

Do not change the FailureSignal positional shape, canonical JSON, SignatureDrivenRefinementPolicy,
thresholds, existing tests or any other diagnostic. No seed/card/name leakage. No clock/GUID.
