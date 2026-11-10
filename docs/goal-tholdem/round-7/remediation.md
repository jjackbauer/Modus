# Round 7 — remediation of the spectator-viewer review

- **Implementer**: remediation agent
- **Date**: 2026-11-06
- **Branch**: `WIP`
- **Input**: `docs/goal-tholdem/round-7/review.md` (2 P1 + 13 P2, all reproduced by the reviewer)
- **Result**: **all 15 findings fixed**, no finding waived or left open. The committed suite grew
  from **130** to **144** facts (14 new `[Fact]`s; one existing fact corrected because the review
  proved its old expectation wrong) and is green. The RV-01 mutation is caught by its new test.
- **Scope**: only `WIP/Wip.Tholdem.Web/`, `WIP/Wip.Tholdem.Tests/` (web test files and one new
  file), `docs/plan/test-waivers.json` (130 → 144) and the six round-7 deliverables. The engine,
  history, agents, learning layer, `Wip.slnx`, the architecture tests, `tools/run-wip-tests.ps1`,
  `.tmp` tooling and `review.md` were not edited. The WIP project count stays **47**.

## 1. Finding-by-finding remediation

| RV | Fix | Files | Test (committed) | Evidence |
|---|---|---|---|---|
| **RV-01** (P1) | `SpectatorSnapshot` now exposes `actingSeat` (the seat to act, `-1` when the hand is complete or the match is not running/paused) and `lastActionSeat`. `liveSeatState()` derives the blue ring from `snapshot.actingSeat` only; the last actor keeps a distinct dashed, labelled `Last action` badge. A completed hand rings no seat. | `Live/SpectatorSnapshot.cs`, `Live/LiveMatch.cs`, `wwwroot/viewer/app.js`, `wwwroot/viewer/index.html`, `wwwroot/viewer/viewer.css` | `LiveActionRing_GivenRunningSnapshot_ExpectedActingSeatEqualsEngineSeatAndUiRingsIt` | API `actingSeat` equals the engine's `ActingSeat` for the exact shown action prefix (paused match, seed 42); DOM ring seat = `actingSeat` in Chromium; `To act` badge + `aria-current=true`; complete snapshot `actingSeat=-1`. Screenshots `fix-live-1440.png`, `fix-live-390.png`. |
| **RV-02** (P1) | The retained live tail is rendered with a `Hand N` separator whenever `action.handNumber` changes (sticky, so the current hand stays labelled while scrolling); every row carries `data-hand`. Replay (single hand) adds no bogus separators. | `wwwroot/viewer/app.js`, `wwwroot/viewer/viewer.css` | `LiveActionLog_GivenMultiHandMatch_ExpectedHandSeparatorsAndEveryRowAttributable` | 3-hand seed-42 match renders `Hand 1 \| Hand 2 \| Hand 3`; every rendered row has its hand; API actions span hands 1–3. Screenshot `fix-live-1440.png` (HAND 7 / HAND 8 visible). |
| **RV-03** (P2) | Snapshot gains `potAwarded` (computed from the complete `TableState` via `HandRecord.From(state).PotAwarded`); the live pot reads `Pot awarded <n>` when the hand is complete, not `Pot 0`. | `Live/SpectatorSnapshot.cs`, `Live/LiveMatch.cs`, `wwwroot/viewer/app.js` | `LivePot_GivenCompletedHand_ExpectedPotAwardedInsteadOfPostSettlementZero` | Seed-7 one-hand match: engine pot `0`, `potAwarded=40`, equal to the deterministic recorded hand; UI shows `Pot awarded 40`; stacks sum 2000. |
| **RV-04** (P2) | Replay gets a labelled settlement row after the last action: `Result · Settlement · Seat 0 awarded · 1838 · pot awarded 1838` (server fragment and SPA), and the settlement row is scrolled into view. The fold row keeps `pot 0`; the awarded amount stays visible. | `Recording/ReplayTableRenderer.cs`, `wwwroot/viewer/app.js`, `wwwroot/viewer/viewer.css` | `ReplaySettlement_GivenFoldThatAwardsThePot_ExpectedLabelledSettlementRow` | `/viewer/table` for seed-777 hand 2 contains `data-settlement="true"`, `data-pot-awarded="1838"`, `pot awarded 1838`, after the last `Fold`; SPA replay shows the same row. Screenshot `fix-replay-1440.png`. |
| **RV-05** (P2) | Deliberate idle table: stacks/hand names show `—`, pot/street show `—`, seat card slots are empty (no card backs), and the “No live match yet — pick a seed and press Start” text stays visible. Initial markup matches the idle state, so there is no “STACK 0 + backs” flash. | `wwwroot/viewer/app.js`, `wwwroot/viewer/index.html` | `IdleTable_GivenNoLiveMatch_ExpectedDashStacksNoCardsAndNoMatchText` | Supplied tool `page 0 0 0 390 900`: stacks `—`, `card_elements=5` (board placeholders only), pot `—`. Chromium: stacks `[—,—]`, seat card counts `[0,0]`. |
| **RV-06** (P2) | `LiveMatchRegistry.ActiveCount` counts only running/paused matches; `/api/status.liveMatches` returns that; the UI label is now `active matches`. Completed/stopped matches stay readable but are not “live”. | `Live/LiveMatchRegistry.cs`, `Live/LiveMatch.cs`, `TholdemWebHost.cs`, `ViewerStatus.cs`, `wwwroot/viewer/app.js` | **Existing test corrected:** `TholdemWebViewerTests.Status_GivenConfiguredDataDirectory_ExpectedOfflineSpectatorReport` now asserts `liveMatches == 1` while running and `== 0` after DELETE and after completion (old assertion `== 1` after completion was the reviewed bug) | Chromium: `liveMatches=0` after completion; status line `active matches 0`; running match reports 1. |
| **RV-07** (P2) | `RecordedHand` carries the session manifest's `agents`; the replay JSON returns them; the server fragment renders `Agent: <name>` in the seat header; the SPA uses `hand.agents` (falling back to the selected session's manifest). `final stack` no longer occupies the agent slot. | `Recording/SessionCatalog.cs`, `Recording/ReplayTableRenderer.cs`, `wwwroot/viewer/app.js` | `ReplaySeatHeaders_GivenManifestAgents_ExpectedAgentsInAgentSlotAndHandJsonAgents` | Hand JSON `agents=["tight-aggressive","random-legal"]`; fragment `data-seat-agent="tight-aggressive"` and `Agent: random-legal`; SPA replay shows `random-legal` in both agent slots. Screenshot `fix-replay-1440.png`. |
| **RV-08** (P2) | At ≤480 px the ledger becomes a stacked labelled card list (every cell has `data-label`; CSS renders it with `::before`) and the table wrapper no longer hides columns; no 42 rem minimum width. | `wwwroot/viewer/app.js`, `wwwroot/viewer/viewer.css` | `LedgerMobile_GivenNarrowViewport_ExpectedStackedLabelledRows` | Chromium at 390: `wrapScrollWidth=366`, `wrapClientWidth=366`, `cells=27`, `labelled=27`, page scroll width 390; row display `block`. Screenshot `fix-ledger-390.png`. |
| **RV-09** (P2) | Added a `max-width: 1240px` breakpoint (17 rem sidebar, `9rem/17rem/9rem` table columns, 3 rem board cards) and a `max-width: 1024px` shell-stacking breakpoint; the base middle column grows to 21 rem. No seat clipping and all five board slots on one row at 901–1150. | `wwwroot/viewer/viewer.css` | `ResponsiveMidWidths_Given901To1150px_ExpectedShrunkTableAndStackedShellBelow1024` | Chromium measured at 1150/1024/950/901: seat 1 right edge 1112/986/912/863 ≤ viewport; `scrollWidth` = viewport; board tops = 1 row, 5 slots. |
| **RV-10** (P2) | At ≤900 px the main area is ordered before the sidebar (`order: -1`), so the view bar and table are on the first screen; controls follow. | `wwwroot/viewer/viewer.css` | `MobileFirstScreen_Given390px_ExpectedTableBeforeControls` | Chromium 390×900: view bar top `143`, table top `228`, both on the first 844 px; page has no horizontal overflow. Screenshot `fix-live-390.png`; extra idle capture `.tmp/remediator/idle-390.png`. |
| **RV-11** (P2) | Every global shortcut is now gated by `isInteractiveTarget()` (`button, a[href], input, select, textarea, contenteditable`, ARIA roles); Space is no longer `preventDefault`ed while a control has focus, so it activates the focused button. | `wwwroot/viewer/app.js` | `KeyboardSpace_GivenFocusedControl_ExpectedPauseShortcutDeferred` | Chromium with a live match running: Space on the focused Replay button selected Replay and the match stayed `running`; Enter still selected Live. |
| **RV-12** (P2) | Used the allowed alternative: removed the half-implemented tab roles (`role=tab/tablist/tabpanel`, `aria-selected`, `aria-controls`), the view bar is a labelled button group with `aria-pressed`, and the hint documents `1`–`4 switch views`. Arrow keys are covered by the same interactive guard, so a focused view button cannot scrub the replay. | `wwwroot/viewer/index.html`, `wwwroot/viewer/app.js` | `ViewNavigation_GivenButtonBar_ExpectedNoTabRoleDocumented1To4AndArrowGuard` | Chromium: no `[role=tab*]` elements, ArrowRight on the focused Replay button left `Hand 1 of 5` unchanged, `aria-pressed` tracks the active view. |
| **RV-13** (P2) | Legend colour changed from `--border` to `#cbd5e1` (kept as real explanatory text, not hidden decoration). | `wwwroot/viewer/viewer.css` | `ActionLegend_GivenDarkPanel_ExpectedAtLeastNormalTextContrast` | Chromium computed `rgb(203,213,225)` on `rgb(17,28,49)` = **11.46:1**, and the test computes the ratio from the CSS declarations (old value would be 1.64:1). |
| **RV-14** (P2) | `#status` and `#live-meta` are polite status regions; `#pot` is a polite status; `#board` is a polite atomic group; `#action-log` is a `role="log"` region fed by append-only rendering so only additions are announced. The acting seat carries `aria-current="true"` plus a visible `To act` badge; selected sessions, strategies and views expose `aria-pressed`. Updates use a `setText` guard and card render keys so identical polls cause no live-region churn. | `wwwroot/viewer/index.html`, `wwwroot/viewer/app.js`, `wwwroot/viewer/viewer.css` | `LiveRegions_GivenDynamicRegions_ExpectedStatusLogTurnAndSelectionSemantics` | Chromium: `statusRole=status statusLive=polite potLive=polite boardLive=polite logRole=log`, ring `aria-current=true`, session/strategy `aria-pressed` present. |
| **RV-15** (P2) | The live action log is appended incrementally; scrolling follows only when the container was already at the bottom (`isScrolledToBottom`, checked before and after the append). Rebuilding a view respects the same rule, and the settlement append re-checks. | `wwwroot/viewer/app.js`, `wwwroot/viewer/viewer.css` | `ActionLogScroll_GivenUserScrolledUp_ExpectedNoSnapToBottom` | Chromium seed 99 / 20 hands / 300 ms: after `scrollTop=0`, 1.6 s later `scrollTop=0` while rows grew 29 → 36 (old behaviour snapped to 517). |

No finding required an exception; there is nothing in the review that remains unfixed.

## 2. Test-first red for the two P1s

Both P1 tests were written and run before any production change. The red run used the
pre-remediation viewer/API (`SpectatorSnapshot` had no `actingSeat`; `app.js` had no separators and
still derived the ring from `actions[actions.length - 1].seat`):

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemViewerRemediationTests
FAIL TholdemViewerRemediationTests.LiveActionLog_GivenMultiHandMatch_ExpectedHandSeparatorsAndEveryRowAttributable: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "'use strict';\n\n// Tholdem spectator viewe"···
Not found: "function createHandSeparator(handNumber)"
FAIL TholdemViewerRemediationTests.LiveActionRing_GivenRunningSnapshot_ExpectedActingSeatEqualsEngineSeatAndUiRingsIt: KeyNotFoundException: The given key was not present in the dictionary.
TOTAL 2 PASSED 0 FAILED 2 SKIPPED 0
RED_EXIT=1
```

The RV-01 test is stronger than a key-presence check: it freezes a real paced match, replays the
shown action prefix through `HeadsUpHandEngine` and asserts the snapshot's `actingSeat` equals the
engine's `ActingSeat`; it then asserts the SPA's ring state is derived from `snapshot.actingSeat`
and that the old expression cannot return. The RV-02 test drives a real three-hand match through
Kestrel and checks three distinct `handNumber`s in the API, then the separator/per-row source
contract.

## 3. Test count and the one corrected existing fact

- `WIP/Wip.Tholdem.Tests/TholdemViewerRemediationTests.cs` — 14 new facts (0 theories, 0 skips).
- `WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs` — only the reviewed expectation changed:
  `Status_GivenConfiguredDataDirectory_ExpectedOfflineSpectatorReport` used to assert
  `liveMatches == 1` after a completed match (the RV-06 bug); it now observes 1 while running,
  0 after `DELETE`, and 0 after completion. No assertion was deleted or weakened; the test now
  exercises the corrected semantics.
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"]` **130 → 144**.
- Nothing else in the suite was edited. Total: **144 = 130 pre-existing + 14 new**, green.

## 4. Mutation — revert RV-01 in isolation

Mutant: only `liveSeatState()` in `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js` was changed so the
ring again follows the last actor (`acting = lastAction`) instead of `snapshot.actingSeat`. No
other file was touched.

```text
$ Get-FileHash WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js
PRE_MUTATION_SHA256=0e6d75b670f78aff80b97d9bac0ef9a5e1e3f74a82a6813b7d4bea2c030ef5a0
BACKUP_SHA256=0e6d75b670f78aff80b97d9bac0ef9a5e1e3f74a82a6813b7d4bea2c030ef5a0

MUTANT_SHA256=dd96964b606c70ff8b9a4d3286c100b91d3f85d552f08ac65157fc102e1481df
MUTANT_LINE=const acting = live && snapshot.isComplete !== true ? lastAction : -1;

$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
Build succeeded.
    2 Warning(s)
    0 Error(s)
MUTANT_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemViewerRemediationTests
FAIL TholdemViewerRemediationTests.LiveActionRing_GivenRunningSnapshot_ExpectedActingSeatEqualsEngineSeatAndUiRingsIt: ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "{\n  const live = snapshot.status === 'run"···
Not found: "snapshot.actingSeat"
TOTAL 14 PASSED 13 FAILED 1 SKIPPED 0
MUTANT_RUN_EXIT=1
```

Restore + hash verification + forced rebuild + full run:

```text
restored_sha256=0e6d75b670f78aff80b97d9bac0ef9a5e1e3f74a82a6813b7d4bea2c030ef5a0
original_sha256=0e6d75b670f78aff80b97d9bac0ef9a5e1e3f74a82a6813b7d4bea2c030ef5a0
match_pre_mutation=True
mutant_marker_present=False

$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
Build succeeded.
    2 Warning(s)
    0 Error(s)
RESTORED_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 144 PASSED 144 FAILED 0 SKIPPED 0
RESTORED_SUITE_EXIT=0
```

The RV-01 test is the only failing test under the mutant, and the restored tree's SHA-256 matches the
pre-mutation hash byte-for-byte.

## 5. Verification commands (exact observed output)

### 5.1 Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

FINAL_TEST_BUILD_EXIT=0
```

### 5.2 Supplementary runner (full Tholdem suite; `dotnet test` not run)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 144 PASSED 144 FAILED 0 SKIPPED 0
FINAL_SUITE_EXIT=0
```

### 5.3 Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
    124 Warning(s)
    0 Error(s)

Time Elapsed 00:01:33.84
SOLUTION_BUILD_EXIT=0
```

(The 124 warnings are the pre-existing solution-wide set; no Tholdem warning is among them.)

### 5.4 Architecture runner

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
ARCH_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
ARCH_RUN_EXIT=0

Assert.Equal(47, graph.Count);   # the passing graph fact; coverage independently prints 47
```

### 5.5 Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=47
solution_wip_project_count=47
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
COVERAGE_EXIT=0
```

### 5.6 Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
HARNESS_EXIT=0
```

`HarnessSync --check` prints nothing on success; the exit code is the observation (it also confirms
the new `.cs`, `.js`, `.css`, `.html` and this Markdown file are UTF-8 without BOM).

## 6. Screenshots (supplied Screenshot tool, server from a copied Release build)

The web project was built (`dotnet build WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj -c Release`,
0 errors), `WIP/Wip.Tholdem.Web/bin/Release/net10.0` was copied to `.tmp/remediator-run`, and the
server was started from that copy with
`dotnet Wip.Tholdem.Web.dll --urls http://127.0.0.1:5188 --data-dir C:\Users\ricar\dev\Modus\artifacts\tholdem`.
`TEMP`/`TMP` were set to `.tmp\pw-temp`; the server was stopped afterwards.

### 6.1 Live, 1440×1100 — `docs/goal-tholdem/round-7/fix-live-1440.png`

```text
$ .../Screenshot.csproj -c Release -- http://127.0.0.1:5188 docs/goal-tholdem/round-7/fix-live-1440.png live 42 8 150 1440 1100
mode=live title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · active matches 1
board=7♦5♦4♠
pot=Pot 20
card_elements=9
seat0=Seat 0
                tight-aggressive
                …
              990
              Q♣8♦
              High Card, Queen
seat1=Seat 1
                …
              1010
              3♦5♥
              One Pair, Fives
action_log_entries=24
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\docs\goal-tholdem\round-7\fix-live-1440.png
SHOT_LIVE_1440_EXIT=0
```

The PNG shows `Live — hand 8`, a **TO ACT** badge on the acting seat, a distinct **LAST ACTION**
cue on the previous actor, the `Hand 7` / `Hand 8` separators, and `active matches 1`.

### 6.2 Live, 390×900 — `docs/goal-tholdem/round-7/fix-live-390.png`

```text
$ .../Screenshot.csproj -c Release -- http://127.0.0.1:5188 docs/goal-tholdem/round-7/fix-live-390.png live 42 8 150 390 900
mode=live title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · active matches 1
board=7♦5♦4♠
pot=Pot 20
seat0=Seat 0
                tight-aggressive
                …
              990
              Q♣8♦
              High Card, Queen
seat1=Seat 1
                …
              1010
              3♦5♥
              One Pair, Fives
action_log_entries=24
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\docs\goal-tholdem\round-7\fix-live-390.png
SHOT_LIVE_390_EXIT=0
```

The PNG shows the view bar and table first at 390 (controls below), the **TO ACT** badge, and the
`Hand 6` / `Hand 7` separators.

### 6.3 Replay, 1440×1100 — `docs/goal-tholdem/round-7/fix-replay-1440.png`

```text
$ .../Screenshot.csproj -c Release -- http://127.0.0.1:5188 docs/goal-tholdem/round-7/fix-replay-1440.png replay 0 2 1440 1100
mode=replay title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · active matches 0
board=10♣J♦K♣J♣
pot=Pot awarded 1838
card_elements=9
seat0=Seat 0
                random-legal
                …
              1890
              6♠4♠
              One Pair, Jacks
seat1=Seat 1
                random-legal
                …
              110
              7♦10♠
              Two Pair, Jacks and Tens
action_log_entries=7
replay_panel=Replay hand
            
            Hand 2 of 5
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\docs\goal-tholdem\round-7\fix-replay-1440.png
SHOT_REPLAY_EXIT=0
```

The PNG shows the manifest agents (`random-legal`) in both seat headers, `Pot awarded 1838`, the six
recorded actions, and the final labelled `RESULT · Settlement · Seat 0 awarded · 1838 · pot awarded 1838`.

### 6.4 Base page at 390 — the supplied-tool half of the Ledger/Strategies evidence

```text
$ .../Screenshot.csproj -c Release -- http://127.0.0.1:5188 .tmp/remediator/page-390.png page 0 0 0 390 900
mode=page title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · active matches 0
board=
pot=—
card_elements=5
seat0=Seat 0
                —
                
                D
                Winner
              
              —
              
              —
seat1=Seat 1
                —
                …
              —
              
              —
action_log_entries=0
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\.tmp\remediator\page-390.png
SHOT_PAGE_390_EXIT=0
```

This is the idle state used by RV-05 (no card backs, `—` stacks/pot, five board placeholders only).

### 6.5 Ledger and Strategies at 390 — scripted tab captures

The provided tool captures a `page` view, so the two tab views were captured with a short
headless-Chromium script that clicks the real Ledger/Strategies buttons and saves full-page PNGs
(`docs/goal-tholdem/round-7/fix-ledger-390.png`, `fix-strategies-390.png`). The same run printed:

```text
CHECK ok: RV-08 the 390px ledger has no clipped horizontal overflow
CHECK ok: RV-08 ledger cells render their column labels in the stacked layout
CHECK ok: RV-08 the 390px page itself does not scroll sideways
OBSERVED ledger wrapScrollWidth=366 wrapClientWidth=366 cells=27 labelled=27 rowDisplay=block
CHECK ok: RV-10 the 390px Strategies view fits without horizontal overflow
OBSERVED strategies pageScrollWidth=390 detail=366/366 championPills=2
CHECK ok: console_errors=0
CHECK ok: page_errors=0
CHECK ok: no response >= 400
CHECK ok: every request went to http://127.0.0.1:5188
OBSERVED console_errors=0 page_errors=0 bad_responses=0 origins=[http://127.0.0.1:5188]
RESULT PASS
```

`fix-ledger-390.png` is 390×1529 and shows all nine labelled fields per promotion row (Iteration,
Champion, Challenger, Promoted, Reason, Win rate, bb/100, LCB Δ, Samples), stacked inside the page —
no clipped columns. `fix-strategies-390.png` is 390×1493 and shows the list, the Champion pill and
the full strategy detail with no horizontal overflow.

## 7. Headless-Chromium DOM / geometry / ARIA observations

The same scripted run (real Kestrel server, real Chromium 1155, 0 console errors, every request to
`http://127.0.0.1:5188`) also observed the following values, which are the DOM-level evidence for
the findings whose committed fact is a server/source contract:

```text
OBSERVED ring actingSeat=1 ringedSeat=1 lastActionSeat=0 lastActionSeatDom=0 turnBadge='To act' ariaCurrent=true
OBSERVED completed pot='Pot awarded 40' potAwarded=40 liveMatches=0
OBSERVED separators=[Hand 1, Hand 2, Hand 3] rows=5 handAttrs=[1,2,3] apiHands=[1,2,3]
OBSERVED replayAgents=[random-legal, random-legal] settlementRows=1 settlement='ResultSettlementSeat 0 awarded1838pot awarded 1838'
OBSERVED scroll beforeTop=0 beforeRows=29 scrollHeight=725 clientHeight=208 afterTop=0 afterRows=36
CHECK ok: RV-11 Space on the focused Replay button activates it
CHECK ok: RV-11 Space on a focused button did not hijack the pause shortcut
CHECK ok: RV-12 ArrowRight on a focused view button does not scrub the replay ('Hand 1 of 5' -> 'Hand 1 of 5')
CHECK ok: RV-12 the view bar no longer claims the ARIA tabs pattern
OBSERVED aria statusRole=status statusLive=polite potLive=polite boardLive=polite logRole=log tabRoles=[null,null,null,null] legendColor=rgb(203, 213, 225) panelColor=rgb(17, 28, 49) contrast=11.46
OBSERVED idle stacks=[—,—] seatCards=[0,0] pot='—' tabsTop=143 tableTop=228
OBSERVED width=1150 seatLeft=890 seatRight=1112 pageScrollWidth=1150 boardTops=1 boardSlots=5
OBSERVED width=1024 seatLeft=722 seatRight=986 pageScrollWidth=1024 boardTops=1 boardSlots=5
OBSERVED width=950 seatLeft=670 seatRight=912 pageScrollWidth=950 boardTops=1 boardSlots=5
OBSERVED width=901 seatLeft=635 seatRight=863 pageScrollWidth=901 boardTops=1 boardSlots=5
RESULT PASS
```

The mid-width captures were written to scratch (`.tmp/remediator/idle-390.png`, `mid-1024.png`) and
are not deliverables; the required deliverables are the five PNGs named in §6 plus this report.

## 8. Limitations, honest notes

- **No committed browser DOM harness.** `docs/goal-tholdem/goal.md` keeps the Playwright check
  outside the committed suite, so DOM-only behaviours (RV-01 ring, RV-02 separators, RV-05 idle
  table, RV-08/09/10 layout, RV-11/12 keyboard, RV-14 semantics, RV-15 scroll) are pinned in the
  suite by concrete API/source/CSS facts — including the exact source lines that produced each
  finding — and proved visually/behaviourally by the headless-Chromium run above. This is a test
  architecture limit, not an unfixed finding.
- **RV-12** uses the review's allowed alternative: the ARIA tabs pattern (roving tabindex, arrow
  navigation, unique `aria-controls`) was removed rather than implemented, and `1`–`4` remains the
  documented keyboard model. The four view buttons remain ordinary button tab stops.
- **RV-06** keeps the JSON field name `liveMatches` (now documented as the running/paused count) and
  relabels the UI to `active matches`; both halves of the review's “or” are satisfied.
- **RV-09** fixes the 901–1150 px range by shrinking the sidebar/columns and stacking the shell at
  ≤1024; the ≥1241 px base layout still uses the 21 rem middle column. No unrelated breakpoints
  were changed.
- The 390 px ledger is a stacked layout at ≤480 px; at 481–900 px it remains a wrapped horizontally
  scrollable table inside `.table-wrap`, which the review did not flag.
- `Wip.slnx`, the architecture project, the runner, `.tmp` tooling and every non-listed doc were not
  modified; the six files in scope are the only new round-7 artifacts. The WIP project count remains
  47 (`verify-solution-coverage.ps1` prints 47/47).

## 9. Deliverables

- `docs/goal-tholdem/round-7/remediation.md` (this report)
- `docs/goal-tholdem/round-7/fix-live-1440.png`
- `docs/goal-tholdem/round-7/fix-live-390.png`
- `docs/goal-tholdem/round-7/fix-replay-1440.png`
- `docs/goal-tholdem/round-7/fix-ledger-390.png`
- `docs/goal-tholdem/round-7/fix-strategies-390.png`
