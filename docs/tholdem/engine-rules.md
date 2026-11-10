# Engine rules

This is the exact ruleset implemented by `WIP/Wip.Tholdem`. The authoritative files are
`WIP/Wip.Tholdem/Engine/HoldemHandEngine.cs` (the general 2–6 seat engine),
`WIP/Wip.Tholdem/Engine/HeadsUpHandEngine.cs` (the thin two-seat wrapper),
`WIP/Wip.Tholdem/Engine/TableState.cs` (including the public `PotAward` record),
`WIP/Wip.Tholdem/Engine/PokerAction.cs`, `WIP/Wip.Tholdem/Engine/LegalActionSet.cs`,
`WIP/Wip.Tholdem/Cards/SeededDeck.cs`, `WIP/Wip.Tholdem/History/HandRecord.cs`,
`WIP/Wip.Tholdem/History/HandRecordCanonicalWriter.cs`,
`WIP/Wip.Tholdem/History/HandRecordJson.cs` and
`WIP/Wip.Tholdem/History/PlayerObservation.cs`.

## Table format

- **Two to six seats, numbered `0 .. seats-1`.** `HoldemHandEngine.Start` accepts
  `seats` in `2..6` and a `buttonSeat` in range. `TableState` is the generalized state type (the
  name is retained): it carries per-seat arrays plus `SeatCount`, `SmallBlindSeat` and
  `BigBlindSeat`. There is **no one-seat mode** and no seven-or-more-seat mode.
- **Heads-up is the two-seat case.** Everything below reduces exactly to the recorded heads-up
  behaviour when `seats == 2`: the button posts the small blind, the other seat posts the big
  blind, the button acts first preflop and the other seat acts first from the flop on.
- **Default match: 100 big blinds.** A hand starts with 1000 chips per seat and blinds of 5/10.
  These are the defaults of `HoldemHandEngine.Start(seed, seats, handNumber = 1,
  startingStack = 1000, smallBlind = 5, bigBlind = 10, buttonSeat = 0, startingStacks = null)` and
  of `HeadsUpHandEngine.Start(seed, handNumber = 1, startingStack = 1000, smallBlind = 5,
  bigBlind = 10, buttonSeat = 0)`. The batch simulator alternates the button by hand
  (`buttonSeat = (hand - 1) % 2`, `SimulationOptions.AlternateButton`); the engine itself takes the
  button as an input and does not rotate it.
- **Per-seat starting stacks.** The optional trailing `IReadOnlyList<long>? startingStacks` of
  `HoldemHandEngine.Start` lets every seat start with a different stack (2–6 values). When it is
  supplied the list must hold exactly one value per seat and every value must cover the big blind
  (otherwise `ArgumentException`/`ArgumentOutOfRangeException`); `startingStack` is then ignored.
  When it is `null`, every seat starts with `startingStack`, which is today's behaviour. This is
  what makes genuine side pots reachable through the public API: a 300-stack seat, a 600-stack seat
  and a 1000-stack seat can all get all-in in one hand.
- `TableState.StartingStacks` exposes the per-seat starting stacks. `TableState.StartingStack` is
  kept as a compatibility projection of the first seat's value; it is identical to every seat's
  stack in every equal-stack (v1) case.
- **The button posts the small blind and the next seat clockwise posts the big blind.**
  `SmallBlindSeat = ButtonSeat` and `BigBlindSeat = (ButtonSeat + 1) % seats`. `Start` subtracts the
  blinds and sets `Pot = smallBlind + bigBlind`, `CurrentBet = bigBlind`, `LastRaiseSize = bigBlind`.
- **Preflop the first seat after the big blind acts first; from the flop on the first seat after the
  button acts first.** Folded and all-in seats are skipped because they cannot act. For two seats
  this is exactly the old order: button preflop (`ActingSeat = ButtonSeat`), non-button postflop
  (`ActingSeat = 1 - ButtonSeat`). For `n` seats the preflop order is
  `BB+1, BB+2, …, button` (the big blind has the option last) and the postflop order is
  `button+1, button+2, …, button` (the button acts last).

## Dealing and determinism

- The deck is the 52-card canonical order shuffled by Fisher-Yates driven by the xorshift64\*
  generator (`SeededDeck.Create`). The all-zero seed is replaced with `0x9E3779B97F4A7C15`; there is
  no clock, culture or ambient randomness anywhere in the deal.
- **There are no burn cards.** For `round` 0 and 1 and every `seat` in
  `0 .. seats-1`, seat `seat` receives `deck[round * seats + seat]`; the hole-card arrays are
  `[deck[seat], deck[seats + seat]]` and `DeckPosition = 2 * seats`. The board is
  `deck[2*seats .. 2*seats+5)`: the flop is three cards, the turn one and the river one. A board
  that would need more cards than remain throws `invalid-state` ("the deck was exhausted").
- For two seats this is byte-for-byte the old deal: seat 0 gets `deck[0]` and `deck[2]`, seat 1
  gets `deck[1]` and `deck[3]`, `DeckPosition = 4`, the flop is `deck[4..6]`, the turn `deck[7]` and
  the river `deck[8]`.
- The board is empty preflop, three cards on the flop, four on the turn and five on the river.

## Action semantics

`PokerAction` has six kinds (`ActionKind`): `Fold`, `Check`, `Call`, `Bet`, `Raise`, `AllIn`.

| Kind | Amount | Meaning |
|---|---|---|
| `Fold` | 0 | surrender the hand; only legal when facing a bet |
| `Check` | 0 | pass when there is nothing to call |
| `Call` | 0 | match the current bet; chips moved are computed by the engine |
| `Bet` | "to" total | open the betting when `CurrentBet == 0` |
| `Raise` | "to" total | increase an existing bet |
| `AllIn` | 0 | commit the whole remaining stack; the engine computes the total |

For `Bet` and `Raise`, `PokerAction.Amount` is the player's **total street commitment after the
action** ("bet/raise to"), not the increment. Every other kind must carry amount 0; a non-zero amount
is rejected with the `amount-not-allowed` reason (`HoldemHandEngine.Apply`). The recorded
`HandActionRecord.Amount` follows the same convention.

## Legality

`HoldemHandEngine.GetLegalActions` computes the acting seat's complete legality projection; the same
formulas apply to every seat count:

- `callAmount = max(0, CurrentBet - StreetCommitted[seat])`.
- `CanFold = callAmount > 0`; **a player may fold only when facing a bet**. Folding when a check is
  available is rejected (`fold-not-facing-bet`).
- `CanCheck = callAmount == 0`.
- `CanCall = callAmount > 0 && stack > 0`; the chips moved are `min(callAmount, stack)`, so a stack
  shorter than the bet can call all-in. That is a short call, not a short raise.
- `MinRaiseTo = CurrentBet == 0 ? BigBlind : CurrentBet + LastRaiseSize`. **The minimum bet is the
  big blind; the minimum raise is the current bet plus the last full raise size.** The last raise
  size starts at the big blind and resets to the big blind on every new street.
- `MaxRaiseTo = StreetCommitted[seat] + stack` (the "to" amount that commits the whole stack).
- `CanBet = CanCheck && CurrentBet == 0 && stack > 0 && MaxRaiseTo >= MinRaiseTo`.
- `CanRaise = CurrentBet > 0 && stack > 0 && MaxRaiseTo >= MinRaiseTo`.
- `CanAllIn = stack > 0`.
- `LegalActionSet.Actions` holds one canonical representative per legal kind in the order Fold,
  Check, Call, Bet, Raise, AllIn; `Bet`/`Raise` representatives carry `MinRaiseTo`.

`Apply` enforces the projection: a `Bet` below the minimum, a `Raise` below the minimum, an amount
above `MaxRaiseTo`, a `Call` with nothing to call, a `Check` facing a bet, a `Fold` with nothing to
call, or any action after the hand is complete is rejected with an `IllegalActionException` carrying
a stable reason token (`raise-below-min`, `raise-above-stack`, `call-not-facing-bet`,
`check-facing-bet`, `fold-not-facing-bet`, `hand-complete`, …).

### Short all-in

A player whose whole stack is below the minimum raise cannot `Bet` or `Raise` — `MaxRaiseTo` is less
than `MinRaiseTo`, so those kinds disappear from the projection — and **the only way to move in is
`AllIn`**. The engine accepts an all-in whose total is below the minimum raise as a short all-in:

- if the all-in total is **not greater** than the current bet, it is a short all-in call: it commits
  the stack, never raises `CurrentBet`, and does not change `LastRaiseSize`;
- if the total **is greater** than the current bet but below the minimum raise, `CurrentBet` becomes
  that total but `LastRaiseSize` keeps the previous full-raise size (the short all-in does not
  re-open the raising size);
- if the total reaches the minimum raise, it is a full raise and `LastRaiseSize` becomes
  `total - oldCurrentBet`.

`AllIn` never takes an amount; a caller cannot choose a different size.

## Betting-round and street progression

- An `ActedMask` tracks who has acted on the current street. A check or call adds the acting seat to
  the mask; an aggressive action (`Bet`, `Raise`, or an all-in above the current bet) resets the mask
  to the acting seat, so every other seat must respond. A fold removes the seat from the hand and
  does not need a mask bit.
- **A betting round ends when every non-folded, non-all-in seat has acted since the last aggression
  and every non-folded seat's `StreetCommitted` equals `CurrentBet` (or the seat is all-in).** An
  all-in seat may sit below `CurrentBet`; its short contribution is settled by the side-pot layers.
  A non-all-in seat that has matched but not yet acted — most importantly the big blind's preflop
  option — still gets to act.
- When the round ends: reset every street commitment to zero, `CurrentBet = 0`,
  `LastRaiseSize = big blind`, reset the acted mask, deal the next street and set the acting seat to
  the first non-folded, non-all-in seat after the button. Preflop → flop → turn → river; after the
  river the hand goes to showdown.
- **If only one seat remains unfolded, it wins the pot immediately.** The win is not a showdown
  (`ShowdownReached = false`); the whole pot is awarded in one `PotAward`, the winner's stack grows by
  `Pot`, and `Pot` becomes zero. This is the old two-seat fold behaviour, including the fact that the
  winner's own uncalled last bet is part of the awarded pot (the net result is the same as returning
  it first).
- **If no non-all-in seat remains (or only one does, which cannot be called), the engine deals the
  remaining board with no further actions and resolves showdown** (`RunOutBoard`). In a multiway
  hand with at least two seats that still hold chips, betting continues on later streets into the
  side pot even while a short all-in seat is already all-in.
- `Street` numbers the phases `Preflop = 0`, `Flop = 1`, `Turn = 2`, `River = 3`, `Showdown = 4`,
  `Complete = 5`. The engine never assigns `Street.Showdown`; it resolves directly from `River` to
  `Complete`, and `ShowdownReached` records whether a showdown was reached.

## Side pots and showdown

- Every seat has a `TotalCommitted` that is the sum of all chips it has put in across all streets.
  While the hand is live `Pot == sum(TotalCommitted)`.
- Side pots are reachable through the public API: `Start(..., startingStacks: [1000, 600, 300])`
  followed by legal actions can put the three seats all-in for different totals. The tests play
  exactly that hand (plus the folded-dead-money, uncalled-excess, odd-chip and six-seat cases) and
  never synthesize a final state.
- The internal `ResolveShowdown` (called by `RunOutBoard`, not part of the public surface) requires
  a complete five-card board and builds the pot layers from
  **ascending distinct contribution levels over every seat with `TotalCommitted > 0`** (folded seats
  included). For each level after the previous one, the layer amount is
  `(level - previousLevel) * count(seats with TotalCommitted >= level)`.
- A layer is awarded to the best `HandRank` (evaluated over that seat's hole cards plus the board)
  among the **non-folded** seats with `TotalCommitted >= level`. Folded seats contribute chips to the
  layers but are never eligible.
- Ties split the layer into equal integer shares. Any odd chips are dealt one chip at a time to the
  winning seats in **clockwise order starting with the first eligible seat after the button**. For
  two seats this is exactly the old `SplitPot` rule: an odd chip goes to the non-button seat.
- **Every layer is recorded as its own `PotAward`.** Consecutive layers won by the same winner set
  are deliberately not merged, so each layer amount stays independently checkable. `PotAward.Winners`
  is always in ascending seat order. A fold-win records one `PotAward` for the whole pot.
- `Winners` is the ascending union of every layer's winners; `AwardedPot` is the sum of all layer
  amounts; `Pot` becomes zero and `IsComplete = true`.
- **The top layer returns uncalled excess to its only eligible contributor.** If a seat contributed
  more than any other remaining seat could match, the highest layer's eligible set is that seat
  alone, so the whole layer is awarded to it. That is how the engine settles an uncalled bet; there
  is no separate early-refund path (see the conservation invariant below).
- For two seats with equal starting stacks (every pre-multiway case) the showdown contributions are
  equal, so there is exactly one contribution level and one layer equal to the whole pot: the higher
  rank takes it, or an exact tie splits it with the odd chip to the non-button seat.
  `HeadsUpHandEngine.SplitPot` is retained as the pinned two-seat helper and agrees with the general
  rule for that equal-stack case. A two-seat hand with unequal starting stacks uses the general
  layered rule (for example `[1000, 600]` all-in produces a 1200-chip layer at 600 and a 400-chip
  top layer returned to the 1000-stack seat).

## Chip conservation and termination

- Every state returned by `Start`, `Apply` and `Replay` satisfies the exact invariant

  ```text
  sum(Stacks) + Pot == sum(StartingStacks) - returned
  ```

  where `returned` is the uncalled excess already returned to its contributor. In this engine
  `returned` is always `0`: uncalled excess is not refunded mid-hand, it is settled at showdown as the
  top pot layer awarded to its only eligible contributor, so that chip appears in the final stack.
  The invariant therefore reduces to `sum(Stacks) + Pot == sum(StartingStacks)` after every action,
  and to `Stacks[0] + Stacks[1] + Pot == 2 * StartingStack` for the pinned equal-stack two-seat case.
- A hand terminates either by fold (one winner takes the pot, `ShowdownReached = false`) or by
  showdown (`Pot` awarded and zero, `IsComplete = true`). Every hand reaches one of those states; the
  property test plays 300 deterministic random hands over 2–6 seats with deterministic per-seat
  starting stacks and asserts conservation after every action, termination within 500 actions, and
  pot awards matching an independent brute-force calculator.
- `HoldemHandEngine.Apply` is pure: it never mutates its input state and returns a new snapshot.

## Replay and history

`HoldemHandEngine.Replay(record)` starts a fresh hand from the record's seed, seat count, hand
number, blinds, button and **per-seat starting stacks**, applies the recorded actions through the
same legality checks, and then verifies the recorded street, seat, `PotAfter`, final board, **every
seat's** hole cards, final stacks, winners, showdown flag, awarded pot and — for three or more
seats — the recorded pot layers. Any difference throws `InvalidOperationException`; the CLI maps
that to exit code 5. `HeadsUpHandEngine.Replay` is a thin delegation, so an old two-seat file, a
two-seat unequal-stack file and a new multiway file are all replayed through the same verifier.

`HandRecord` keeps its original two-seat constructor members, including
`HoleCardsSeat0`/`HoleCardsSeat1`, and adds `SeatCount`, `StartingStacks`, the per-seat
`HoleCards` and `Pots`:

- **A two-seat record whose starting stacks are all equal serializes exactly as before (v1).**
  Property order, field names, card text and the SHA-256 fingerprint are unchanged, so every
  recorded two-seat history still verifies. The v1 form has no `startingStacks` member; the parser
  reconstructs the equal per-seat array from `startingStack`.
- **Every other record serializes as v2** — three to six seats, or two seats with unequal starting
  stacks — with this fixed order:
  `{"schema":"tholdem.hand.v2","handNumber":…,"seed":…,"smallBlind":…,"bigBlind":…,
  "startingStack":…,"startingStacks":[……],"buttonSeat":…,"seats":…,"holeCards":[[…],…],"board":[…],
  "actions":[{"street","seat","kind","amount","potAfter"}…],"pots":[{"amount","winners"}…],
  "showdown":…,"winners":[…],"potAwarded":…,"finalStacks":[…]}`.
  `startingStack` remains the first seat's stack for compatibility; `startingStacks` is the
  authoritative per-seat array used by `Replay`. `Hash` is SHA-256 over that JSON.
- `HandRecordJson.Parse` detects v2 by the presence of the `"seats"` property; it keeps the v1 parser
  unchanged for files without it, so every recorded v1 hand still parses and round-trips. The v2
  parser accepts `seats` 2–6 (a two-seat unequal-stack hand is v2), requires a `startingStacks`
  array with one value per seat covering the big blind, and rejects malformed input with the usual
  stable `FormatException` reasons.

## Observation boundary

`PlayerObservation` is the player-visible projection an agent receives
(`WIP/Wip.Tholdem/History/PlayerObservation.cs`) and it covers every seat count. `ForSeat(state,
seat)` returns the requesting `Seat`, `SeatCount`, its own hole cards, the board, its own and the
opponents' public stacks and street commitments (`OpponentStack`/`OpponentStreetCommitment` are the
single opponent in a two-seat hand and the sum over all other seats in a multiway hand), the pot, the
acting seat's amount to call, min/max raise-to and legal action kinds, the completion flag, the
winners, and a per-seat public array `Seats` with `Seat`, `Stack`, `StreetCommitment`, `Folded` and
`AllIn`.

The observation itself contains **no seed, no deck order, no undealt cards and no other seat's hole
cards** for any seat count. The canonical JSON (`PlayerObservation.ToCanonicalJson`) keeps the exact
pre-change field list and bytes for two seats — `seatCount`, `seat` and `seats` are **not emitted**
for two seats — and appends those three members only for three or more seats. This keeps every
recorded agent-seed fixture stable.

Every turn an `IPokerAgent` also receives an `AgentTurnContext`
(`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`) carrying only the hand number, the acting seat and the
per-seat action index: public turn coordinates. The context has **no seed of any width, no digest and
no other secret-derived value**, so no agent input contains a stored value to invert. That is a
statement about the agent surface, not about the deal: the deal is deterministic from the session
seed, so an observer holding its own cards can enumerate candidate seeds and reconstruct the deck
when the seed is guessable. The operator entry points therefore require seeds at or above
`1,099,511,627,776` unless the explicit demo opt-in is used; see the
[threat model](known-limits.md#threat-model). Agent randomness is derived from the agent's own
observation instead: every baseline and `StrategyAgent` computes
`SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), context.HandNumber, context.Seat,
context.ActionIndex, stream)` (`WIP/Wip.Tholdem.Agents/SeedDerivation.cs`), the first eight big-endian
bytes of
`SHA-256("tholdem-agent-seed|<canonicalObservation>|<handNumber>|<seat>|<actionIndex>|<stream>")`
with invariant formatting. The observation is the agent's input (its own hole cards plus public
state), so its derived value is reproducible for an honest replay and depends only on that
observation, not on the session seed. The engine's hand-seed formula
`SeedDerivation.Derive(sessionSeed, hand, 0, 0, 0)` is unchanged — every recorded replay is
identical, and a two-seat observation's canonical JSON is byte-identical to its pre-multiway form.

`TableState` keeps `Seed`, `Deck`, `HoleCards` (per seat, with
`HoleCardsSeat0`/`HoleCardsSeat1` compatibility projections), `DeckPosition`, `ActedMask` and
`ActionLog` as `internal` members. `Wip.Tholdem` grants `InternalsVisibleTo` only to
`Wip.Tholdem.Tests`, so production callers cannot read them. `SessionHandStore` recovers a
historical hand by replaying the record and projecting through `PlayerObservation.ForSeat`, and
`RuntimeSourceBoundaryTests` scans the runtime-agent sources for the forbidden identifiers
`TableState`, `SpectatorSnapshot`, `HoleCardsSeat0`, `HoleCardsSeat1` and `Deck`.
`AgentInformationBoundaryTests.AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers`
rejects hidden-information identifiers in `IPokerAgent.cs`, `AgentTurnContext.cs`, `Baselines/*.cs` and
`Learning/StrategyAgent.cs`;
`AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember` asserts by
reflection that the context exposes exactly `HandNumber`, `Seat` and `ActionIndex` and no seed-valued
property;
`AgentInformationBoundaryTests.AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateSeedMatchAndDifferentDeck`
replays the round-11 falsifier enumeration (candidate session seeds 0..100000 against the recorded
seed-42 context and observation) and asserts that no agent input equals any value the deleted one-way
digest could produce, and that a deck rebuilt from the agent's derived seed differs from the recorded
deal; and
`DeriveAgentSeed_GivenObservationAndTurnCoordinates_ExpectedStableTwiceAndChangedForDifferentObservation`
pins the same-seed/same-action/different-observation determinism fact.
