# Round 7 — Real poker-table viewer for `Wip.Tholdem.Web`

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **Scope delivered**: CSS-drawn card faces with proper suits, a tabbed main area (`Live` / `Replay` / `Strategies` / `Ledger`), replay rendered as a full-size table in the main area, dealer button, to-call/pot-odds, hand-strength names, winner highlighting, a colour-coded action log, keyboard shortcuts, responsive layout at 390 px, an inline SVG favicon, and six new committed HTTP-surface facts. The suite grew from **124** to **130** facts and stays green.
- **Not created / not changed**: the engine, history, agents, learning layer, `Wip.slnx`, the architecture tests, `tools/run-wip-tests.ps1`, any other `.tmp` tooling, and every `docs/goal-tholdem/` file other than this report and the two screenshots. The WIP project count stays **47**.

## Files changed

### Created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Web/HandNaming.cs` | Friendly best-five hand names (`"Two Pair, Kings and Jacks"`, `"Straight, Nine high"`, `"Full House, Kings full of Sevens"`, …) from engine cards or recorded card text; `"Preflop"` before the flop. |
| `WIP/Wip.Tholdem.Tests/TholdemViewerTableTests.cs` | Six new `[Fact]`s over the real Kestrel surface: markup, responsive CSS, replay card faces/hand names/winner/dealer/chips, live snapshot fields, session-detail hand fields + hand JSON + traversal safety, and the offline/no-`innerHTML`/empty-state scan. |
| `docs/goal-tholdem/round-7/implementation.md` | This report. |
| `docs/goal-tholdem/round-7/viewer-live.png` | Real headless-Chromium screenshot of a live match (1440×1100). |
| `docs/goal-tholdem/round-7/viewer-replay.png` | Real headless-Chromium screenshot of the replay table (1100×1000; see the screenshot note). |

### Modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` | Added `HandNames` (2 strings), `AmountToCall` (long) and `PotOdds` (double); `ButtonSeat` was already part of the record and stays in the JSON. |
| `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` | `Snapshot()` now reuses both seats' `PlayerObservation` hole cards, computes `handNames` when the board has 3+ cards, and computes `amountToCall = max(0, CurrentBet − StreetCommitted[ActingSeat])` and `potOdds = call / (pot + call)` (0 when nothing to call, 0 once the hand is complete). |
| `WIP/Wip.Tholdem.Web/Recording/SessionCatalog.cs` | `HandSummary` gained `HandNames` and `ButtonSeat`; `RecordedHand` exposes a computed `HandNames` property and `ToSummary()` passes the new fields through. |
| `WIP/Wip.Tholdem.Web/Recording/ReplayTableRenderer.cs` | Rebuilt as a real table fragment: CSS card faces (`card--<suit> card--red|black`, `data-card`, aria label, `card__rank`/`card__suit`), five board slots with `card--empty`, `card--back` padding, seat `is-winner` + `Winner` badge, `data-dealer="true"` + `D` button, friendly hand names, and an action log with street chips and `action--*` colour classes. All pre-existing contract strings (`Showdown:`, `Pot awarded:`, `Seat N`, `action-row`, `data-kind`, `data-hand-number`, `data-showdown`) are preserved. |
| `WIP/Wip.Tholdem.Web/TholdemWebHost.cs` | Added `GET /api/sessions/{sessionId}/hands/{handNumber}` (JSON for the replay UI; 404 on unknown). |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html` | Four `class="tab" data-view="…"` buttons; main-area table panel with `#board`, `#pot`, `#street`, `#to-call`, seats, `#action-log`; Strategies/Ledger panels (`#strategies`, `#strategy-detail`, nine `<th>` labels, `#ledger`); sidebar kept (live form, controls, poll speed, `#sessions`, `#replay-panel`, `#replay-scrubber`, `#replay-hand`); keyboard-hint line; inline base64 SVG favicon; empty-state copy. |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js` | Rewritten: CSS card builder, shared live/replay table renderer, dealer/winner/to-call/pot-odds, colour-coded and auto-scrolled action log, tab switching with `aria-selected`, replay loaded from the per-hand JSON endpoint, strategy list/detail cards with labelled parameter rows and Champion badge, full-width ledger with pills/chips/one-decimal numbers, empty states, keyboard shortcuts (`Space`, `←`/`→`, `1`–`4`, guarded on input/select/textarea), and **no `innerHTML` anywhere**. |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/viewer.css` | Card faces/back/empty, felt table, seats, dealer button, winner ring/badge, pot, to-call pill, tab bar, strategy/ledger layouts, monospace tables, responsive `@media (max-width: 900px)` + 480 px rules, `prefers-reduced-motion` handling. No external font or URL. |
| `WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs` | The committed offline-asset fact now distinguishes the inline `data:image/svg+xml` favicon from the two same-origin relative assets (`app.js`, `viewer.css`); the forbidden-token scan is unchanged. Intent (two relative assets, no external reference) is preserved, not weakened. |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` **124 → 130**; file still parses as JSON and stays UTF-8 without BOM. |

## Contract surface added

- `SpectatorSnapshot` JSON gains `handNames: string[2]`, `amountToCall: long`, `potOdds: double`; `buttonSeat` is exposed as before.
- `GET /api/sessions/{id}` `hands[]` gains `handNames: string[2]` and `buttonSeat: int`.
- New: `GET /api/sessions/{id}/hands/{handNumber}` returns the recorded hand with `handNames`, `buttonSeat`, board, hole cards, actions, winners, `potAwarded`, `finalStacks`.
- `GET /viewer/table?session=…&hand=…` renders the same information server-side (CSS faces, five board slots, dealer button, winner badge, hand names, colour-coded logical action log).

### Why the hand JSON endpoint exists

The viewer is forbidden from assigning `innerHTML` and no artifact content may become markup, so the replay surface is built with `document.createElement` + `textContent`. It therefore consumes the hand as JSON. `/viewer/table` remains the server-rendered fragment (and is asserted directly by the committed HTTP tests).

## Implementation notes

- **Hand names**: `HandEvaluator.Evaluate(board + hole)` over 5–7 cards, formatted by `HandNaming.Describe`. Board + 2 hole cards is always exactly 5–7 cards after the flop, so no branch can throw for recorded/live data; malformed recorded card text degrades to `"Preflop"` instead of failing the request.
- **To-call/pot-odds**: computed for the acting seat only while the hand is live; both are 0 for a complete/stopped hand. `potOdds` is 0 whenever `amountToCall` is 0, and always in `[0,1)` otherwise.
- **Shared table surface**: `Live` and `Replay` both render into the single main-area table panel (only one main panel is visible at a time; Strategies and Ledger are separate full-width panels). This keeps the DOM ids unique and makes the replay a genuine full-size table rather than a sidebar fragment. Selecting a session or moving the scrubber switches to the Replay tab.
- **Dealer/winner visibility**: the `D` and `Winner` elements are always present but carry the `hidden` attribute unless applicable; `[hidden] { display: none !important; }` guarantees only one of each renders.
- **Favicon**: the SVG must contain `xmlns="http://www.w3.org/2000/svg"` to be a valid standalone image, which would trip the offline token scan. It is therefore embedded as a base64 `data:image/svg+xml` URI: valid SVG, no external request, and no literal `http://`, `https://`, `//cdn`, `HTTP` or `@import` substring in any viewer asset. `/favicon.ico` is no longer requested (screenshot tool reports `console_errors=0`).
- **Responsive**: at ≤900 px the shell stacks (sidebar above main), the table becomes one column with the board first, cards scale with `--card-w`, and the nine-column ledger lives in an `overflow-x: auto` wrapper so the page itself never scrolls sideways at 390 px.
- **No raw JSON**: the strategy detail card shows id/version/parent, four labelled parameter rows (`Raise threshold`, `Call threshold`, `Bluff frequency`, `Sizing multiplier`), the prompt text, and a Champion pill; the ledger shows nine labelled columns, Promoted/Rejected pills, `bb/100` and `LCB Δ` at one decimal, and the reason as a chip.

## Test-first evidence (red)

The six new facts were written before any production change. To observe red against the **pre-change** implementation, the new test assembly was compiled without rebuilding its project references and the pre-change viewer assets were restored into the test output, so the real Kestrel host served the old `Wip.Tholdem.Web.dll` (built at 16:45:16, before the implementation) and the old `index.html`/`app.js`/`viewer.css`:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-dependencies
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TholdemViewerTableTests
FAIL 
TholdemViewerTableTests.LiveSnapshot_GivenPacedMatchOnFlop_ExpectedHandNamesButtonToCallPotOddsAndCompletedWinner: 
KeyNotFoundException: The given key was not present in the dictionary.
FAIL 
TholdemViewerTableTests.ReplayTable_GivenHandWithFourCardBoard_ExpectedCardFacesHandNamesDealerWinnerAndActionChips: 
ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "<article class="hand-replay" data-session"···
Not found: "data-card="Tc""
FAIL 
TholdemViewerTableTests.SessionDetail_GivenRecordedHands_ExpectedHandNamesButtonSeatHandEndpointAndTraversalSafety: 
KeyNotFoundException: The given key was not present in the dictionary.
FAIL TholdemViewerTableTests.ViewerIndex_GivenTableExperienceMarkup_ExpectedTabsCardsFaviconLedgerAndSidebarControls: 
ContainsException: Assert.Contains() Failure: Sub-string not found
String:    "<!DOCTYPE html>\n<html lang="en">\n<head>\n "···
Not found: "class="tab" data-view="live""
FAIL TholdemViewerTableTests.ViewerScript_GivenArtifactContent_ExpectedNoInnerHtmlNoExternalUrlAndExactEmptyStates: 
DoesNotContainException: Assert.DoesNotContain() Failure: Sub-string found
String: ···"n;\n    }\n\n    panel.innerHTML = await res"···
Found:  "innerHTML"
FAIL TholdemViewerTableTests.ViewerStyles_GivenResponsiveTable_Expected900pxBreakpointReducedMotionAndCompactCards: 
ContainsException: Assert.Contains() Failure: Sub-string not found
String:    ":root {\n  color-scheme: dark;\n  font-fami"···
Not found: "max-width: 900px"
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
RED_RUN_EXIT=1
```

Two process notes, for honesty:

1. The **first** red run also exposed a fixture assumption in the new test (seed 7 with tight-aggressive never produced a four-card board). The fixture was corrected in the test to seed 777 / five hands / two `random-legal` seats — the same deterministic deal the frozen `probe-777` artifact contains — and the red above is the re-run. No production assertion was changed to make anything pass.
2. A PowerShell text round-trip used while editing the test file corrupted its non-ASCII literals (mojibake); a character audit detected it, the literals were repaired, and the file was re-verified as UTF-8 without BOM before the green runs. This is why the report's red re-run is the authoritative one.
3. After the red run the live fact was strengthened to also assert the **delay-0 completed** snapshot (`handNames`, `buttonSeat`, `amountToCall`, `potOdds`, winners, six hand hashes) while keeping the paced flop sample. Its red failure mode is unchanged: the pre-change snapshot had no `handNames` key at all (`KeyNotFoundException`), which the quoted red output shows. The final suite was re-run after that test edit (130/130 green).

## Mutation

Mutant: `ReplayTableRenderer.AppendCard` keeps the card's rank but renders an empty `card__suit` span (a board card without its suit glyph).

Pre-mutation SHA-256 of `WIP/Wip.Tholdem.Web/Recording/ReplayTableRenderer.cs`:
`b11f067888dec688abc72804d8ab0de8bdb12b959bcf298257c2c45a6be43686`

A copy of the source was kept at `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/round7-mutation/ReplayTableRenderer.cs.bak` (build output, outside source control). Forced rebuild (`--no-incremental`) and full suite:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
Build succeeded.
    0 Error(s)
MUTANT_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
FAIL 
TholdemViewerTableTests.ReplayTable_GivenHandWithFourCardBoard_ExpectedCardFacesHandNamesDealerWinnerAndActionChips: 
ContainsException: Assert.Contains() Failure: Sub-string not found
Not found: "♣"
TOTAL 130 PASSED 129 FAILED 1 SKIPPED 0
MUTANT_EXIT=1 wall_seconds=108.8
```

The card-markup fact is the only failure, as required. Restored from the backup and verified by hash:

```text
restored_sha256=b11f067888dec688abc72804d8ab0de8bdb12b959bcf298257c2c45a6be43686
original_sha256=b11f067888dec688abc72804d8ab0de8bdb12b959bcf298257c2c45a6be43686
match_pre_mutation=True
mutant_marker_removed=True
```

Forced rebuild and re-run after the restore:

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
Build succeeded.
    0 Error(s)
RESTORED_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 130 PASSED 130 FAILED 0 SKIPPED 0
RESTORED_EXIT=0 wall_seconds=124.7
```

## Test count

**130 test cases**: 124 pre-existing (all still green) + **6 new `[Fact]` methods**, 0 `[Theory]`, 0 skips, 0 waived failures. The pre-existing `ViewerAssets_GivenOfflineViewerDirectory_…` fact was updated in place for the inline favicon (same intent: exactly two same-origin relative assets, no external reference), so it is not a new test.

| New fact | Covers |
|---|---|
| `ViewerIndex_GivenTableExperienceMarkup_ExpectedTabsCardsFaviconLedgerAndSidebarControls` | four `data-view` tabs, `card--`/`card--empty`, `data:image/svg+xml` favicon, nine ledger labels, `app.js`/`viewer.css`, sidebar ids, empty-state copy |
| `ViewerStyles_GivenResponsiveTable_Expected900pxBreakpointReducedMotionAndCompactCards` | `@media` + `max-width: 900px`, `prefers-reduced-motion`, card/dealer/winner/tab/pill/action classes |
| `ReplayTable_GivenHandWithFourCardBoard_ExpectedCardFacesHandNamesDealerWinnerAndActionChips` | `/viewer/table` for the four-card-board hand: faces with exact `data-card`/glyph/aria-label/suit class, `card--empty` count, hand names, `is-winner` + `Winner`, dealer button, action kinds/chips |
| `LiveSnapshot_GivenPacedMatchOnFlop_ExpectedHandNamesButtonToCallPotOddsAndCompletedWinner` | a delay-0 live match completing with `handNames`, `buttonSeat`, `amountToCall`, `potOdds` and named winners, plus a paced run sampled on the flop where both names are evaluated hand strengths |
| `SessionDetail_GivenRecordedHands_ExpectedHandNamesButtonSeatHandEndpointAndTraversalSafety` | `/api/sessions/{id}` summaries, the per-hand JSON endpoint, and six traversal/unknown-path 404s |
| `ViewerScript_GivenArtifactContent_ExpectedNoInnerHtmlNoExternalUrlAndExactEmptyStates` | `app.js` has no `innerHTML`, uses `textContent`/`createElement`, contains the per-hand endpoint and all four exact empty states; all three assets are offline |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 130`.

## Verification commands (final tree, exact observed output)

### 1. Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    2 Warning(s)
    0 Error(s)

FINAL_BUILD_EXIT=0
```

The two warnings are the pre-existing `Wip.Runtime` warnings (`CS8425` in `AgentExecutor.cs`, `CA2024` in `DeepSeekChatModel.cs`); the Tholdem projects build warning-free. The baseline build before this round showed the same two warnings.

### 2. Tholdem suite (supplementary runner; `dotnet test` is forbidden here)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 130 PASSED 130 FAILED 0 SKIPPED 0
FINAL_RUN_EXIT=0 wall_seconds=95.5
```

Baseline before this round, same runner: `TOTAL 124 PASSED 124 FAILED 0 SKIPPED 0`, `BASELINE_EXIT=0`.

### 3. Whole solution

```text
$ dotnet build Wip.slnx --configuration Release
Build succeeded.
    124 Warning(s)
    0 Error(s)

SOLUTION_BUILD_EXIT=0 wall_seconds=82.1
```

(An earlier full solution build of the same tree reported `126 Warning(s) / 0 Error(s)`, exit 0, in 142.8 s; the final incremental re-run recompiled fewer projects.) The warnings are the pre-existing solution-wide set; no Tholdem warning is among them.

### 4. Architecture tests

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)

ARCH_BUILD_EXIT=0

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
ARCH_RUN_EXIT=0
```

`ArchitectureReferenceTests.ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` asserts `Assert.Equal(47, graph.Count)` and passed; the solution coverage script below independently prints 47.

### 5. Solution coverage

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

### 6. Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
HARNESS_EXIT=0
```

`HarnessSync --check` prints nothing on success; the exit code is the observation. It also confirms every new file is UTF-8 without BOM.

## Screenshots (real headless Chromium)

The web project was built (`dotnet build WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj -c Release`, 0 errors), the Release output was copied to `.tmp/viewer-shot`, and the server was started from that copy with `--urls http://127.0.0.1:5188 --data-dir C:\Users\ricar\dev\Modus\artifacts\tholdem` (`/api/status` observed: `{"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}`). `TEMP`/`TMP` were set to `.tmp\pw-temp`; the server was stopped afterwards.

### Live

```text
$ dotnet run --project .tmp/tholdem-verify/Screenshot/Screenshot.csproj -c Release -- http://127.0.0.1:5188 docs/goal-tholdem/round-7/viewer-live.png live 42 10 120 1440 1100
mode=live title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · live 1
board=
pot=Pot 20
card_elements=9
seat0=Seat 0
                tight-aggressive
                D
                Winner

              990
              2♣5♠
              Preflop
seat1=Seat 1
                random-legal
                D
                Winner

              990
              9♦K♥
              Preflop
action_log_entries=18
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\docs\goal-tholdem\round-7\viewer-live.png
SHOT_LIVE_EXIT=0
```

The captured frame is a live flop (`Live — hand 8`, board `8♥ 8♣ J♦`, pot 1010, to-call 990 · pot odds 49.5%, dealer `D` on seat 1, 18-row colour-coded action log). The tool prints its text probes a few hundred milliseconds before the screenshot, so its `pot=`/`board=` line reflects the earlier preflop instant of the same running match. The `D`/`Winner` lines in the text dump list both seats because `textContent` also reads `hidden` elements; the PNG renders only the applicable one per seat.

### Replay

```text
$ dotnet run --project .tmp/tholdem-verify/Screenshot/Screenshot.csproj -c Release -- http://127.0.0.1:5188 docs/goal-tholdem/round-7/viewer-replay.png replay 0 2 1440 1100
mode=replay title=Tholdem Spectator Viewer
status=Spectator · C:\Users\ricar\dev\Modus\artifacts\tholdem · sessions 2 · strategies 4 · live 1
board=10♣J♦K♣J♣
pot=Pot awarded 1838
card_elements=9
seat0=Seat 0
                final stack
                D
                Winner

              1890
              6♠4♠
              One Pair, Jacks
seat1=Seat 1
                final stack
                D
                Winner

              110
              7♦10♠
              Two Pair, Jacks and Tens
action_log_entries=6
replay_panel=Replay hand

            Hand 2 of 5
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\docs\goal-tholdem\round-7\viewer-replay.png
SHOT_REPLAY_EXIT=0
```

The replay table shows the four visible board cards plus one empty slot, both seats' hole cards, the `Winner` badge and gold ring on seat 0, the `D` button on seat 1, the friendly hand names, the pot, and the six-row action log with street chips and colour classes. The screenshot tool's positional layout places `1440` in an argument replay mode ignores and uses `1100` as the width, so the replay PNG is 1100×1000 rather than 1440×1100; the specified command was run exactly.

### Extra responsive check (not a deliverable)

A 390 px page-mode capture was taken for design verification only:

```text
$ dotnet run --project .tmp/tholdem-verify/Screenshot/Screenshot.csproj -c Release -- http://127.0.0.1:5188 .tmp/narrow-390.png page 0 0 0 390 900
mode=page title=Tholdem Spectator Viewer
card_elements=9
console_errors=0
screenshot=C:\Users\ricar\dev\Modus\.tmp\narrow-390.png
NARROW_EXIT=0
```

Result: a 390×1514 PNG with the sidebar stacked above the main area, board and seats stacked, scaled cards, and no page-level horizontal scrolling.

## What I could not verify / could not make pass

- **Nothing in the required verification set failed.** All six commands were observed green on the final tree, and both screenshots were captured with `console_errors=0`.
- `dotnet test` was not run (forbidden by the dispatch; local VSTest aborts). Local evidence is the supplementary `RunXunit` runner over the compiled assembly; CI is not observable from this machine, so this is a local green claim.
- The supplementary runner executes tests sequentially and ignores xUnit collections; unchanged from previous rounds.
- The 390 px "no horizontal scrolling" property is verified visually with the extra page screenshot and by the committed CSS assertions (`max-width: 900px`, table wrapper with `overflow-x: auto`); the suite has no browser harness, so it is not a committed browser pixel test.
- The replay screenshot is 1100×1000 because of the screenshot tool's replay argument layout (documented above), not because the viewer constrains the width.
- The `D`/`Winner` text in the tool dump appears for both seats because hidden elements still contribute `textContent`; the visual screenshots confirm only the applicable badge renders.
- The live screenshot is sampled at the tool's fixed 3.5 s wait; it happens to show a flop, but the street is not controlled by the viewer.
