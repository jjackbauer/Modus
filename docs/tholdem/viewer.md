# Spectator viewer

`WIP/Wip.Tholdem.Web` is a loopback-only, offline ASP.NET Core spectator viewer over one frozen
artifact directory: a paced live match feed, a recorded-session replay browser, read-only
strategies/ledger views, and — since the mini-RSI delivery — a **Training** tab that can start,
observe and cancel a training or mini-RSI job and explicitly promote its verified result. Starting a
job is the only write path: the host runs the learning loop on a background queue and persists
artifacts under the same data directory. The viewer itself never contacts an external URL and has no
telemetry. **No publish, signing or deployment is performed or automated by this delivery**; it is
run from a local build output. The training/mini-RSI contract and its honest limits are documented in
[rsi.md](rsi.md); the adversarial analysis is in [rsi-threat-model.md](rsi-threat-model.md).

## How to run it

**Never start the host from `bin/Release`.** Running the DLL/EXE in place locks the loaded
assemblies and breaks the next `dotnet build` (RISK-006). Copy the build output to a scratch
directory first:

```powershell
dotnet build WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj --configuration Release

$source = 'WIP\Wip.Tholdem.Web\bin\Release\net10.0'
$target = '.tmp\tholdem-viewer'
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $target -Recurse

dotnet "$target\Wip.Tholdem.Web.dll" --urls http://127.0.0.1:5099 --data-dir artifacts\tholdem-run
```

Then open `http://127.0.0.1:5099/viewer/` (or `http://127.0.0.1:5099/`, which redirects to
`/viewer/index.html`). `--data-dir` is optional: without it the host uses
`<content root>\artifacts\tholdem`, resolved to a full path and created if missing
(`TholdemWebHost.ResolveDataDirectory`). `--urls` is honored when supplied; otherwise the default
`http://127.0.0.1:5099` is used (`TholdemWebHost.DefaultUrl`). Stop the server with Ctrl+C.

## Data directory

The viewer reads, and (only when a training/mini-RSI job is started) writes:

| Path | Used for |
|---|---|
| `sessions/<sessionId>/session.json` | session list, seed/blinds/agents/manifest hash, replay |
| `sessions/<sessionId>/hand-NNNNN.json` | replay table per hand |
| `strategies/<id>.json` | Strategies tab; written by a training job |
| `evaluations/<challenger>-vs-<champion>.json` | written by a training job; read back by promote |
| `ledger.json` | Ledger tab; hash-chained entries written by a training job |
| `active-champion.json` | champion badge; written by a verified promotion |
| `training-seeds.json` | seed-reuse register written by the training API |
| `meta/**` | mini-RSI proposer artifacts, meta evaluations, hash-chained meta ledger, active proposer pointer |

Malformed files are skipped and counted rather than crashing the page (`SessionCatalog`,
`StrategyCatalog`). Live matches are held **in memory only** — a live match started in the viewer is
not persisted under `sessions/`; use the CLI `play` command to record one. Live matches use the
simulator's rules (`SeedDerivation.Derive(seed, hand, 0, 0, 0)`, button `(hand - 1) % seats`, the
configured per-seat starting stacks); the configured delay paces the UI and never influences an
agent decision (`Live/LiveMatch.cs`).

## Tabs and controls

- **Live** — a form with seed, hands (1–500), **seats (2–6, `#seats`)**, delay (0–5000 ms), seat-0
  agent and seat-1 agent; Start/Pause/Resume/Stop buttons; poll speed (Fast 120 ms / Normal 300 ms /
  Slow 800 ms). The seed input is pre-filled with a random 64-bit value from
  `crypto.getRandomValues` (falling back to the constant `1099511627776` only when `crypto` is
  unavailable); seeds below `1099511627776` are guessable, so `#allow-weak-seed` must be ticked to
  start one for a demo or test. The table builds **one panel per seat in `snapshot.seats`**: name,
  agent, stack, that seat's hole cards, hand-strength name, dealer/active/last-action/winner (and
  folded/all-in) badges; 2–4 seats use two columns and 5–6 use three on wide screens, collapsing to
  one column on a phone. The board, pot, street and amount-to-call/pot-odds stay in the centre. The
  action log is colour-coded by action kind and labels each row with a street chip (unchanged for
  every seat count). The form sends `seats` plus one `agents` name per seat; seats 3–6 alternate the
  two visible agent selections. Seatable agents are `random-legal`, `tight-aggressive` and
  `pot-odds-equity` (`LiveAgentCatalog`). For a two-seat body, `seat0Agent` and `seat1Agent` are
  required; for 3–6 seats, `agents` must name every seat. A missing or unknown name is rejected with
  400 `unknown-agent` (`TholdemWebHost.MapLiveRoutes`), never replaced by a default.
- **Replay** — the recorded-session list, a "Replay hand" scrubber, and the full-size replay table
  loaded from the per-hand JSON endpoint (one panel per recorded seat, card faces, dealer button,
  winner badges, friendly hand names, pot layers for v2 hands, a labelled settlement row). Selecting
  a session switches to this tab.
- **Strategies** — the strategy artifacts under `strategies/`, with a detail card (id, version,
  parent, iteration, parameters, prompt text) and a Champion badge from `active-champion.json`.
- **Ledger** — the `ledger.json` rows as a table: Iteration, Champion, Challenger, Promoted, Reason,
  Win rate, bb/100, LCB Δ, Samples. Empty states are rendered when the directory has nothing yet.
- **Training** — a training-job form (seed, iterations, training hands, evaluation seeds/hands,
  margin, minimum seeds, optional starting strategy id, weak-seed opt-in), Start and Cancel buttons,
  a live phase/iteration timeline, one row per iteration (champion → challenger, mean bb/100, LCB vs
  margin, decision, promoted/rejected), artifact links, and a champion card. Progress is polled every
  250 ms; the page remains usable while a job runs. The **Promote** button is only rendered for a
  completed job whose last report the server marked promotable (`canPromote`); it asks for an explicit
  `window.confirm` and then calls the server endpoint, which independently re-verifies the persisted
  report. Cancelling requests cooperative cancellation and leaves no promotion. The same tab exposes
  a bounded mini-RSI run (seed, meta/inner iterations, meta/evaluation seeds, margin, minimum seeds)\n  with its own status line and an explicit proposer promote; it evolves proposer data only and never\n  the strategy champion.
- **Keyboard** — `Space` pause/resume, `←`/`→` previous/next replay hand, `1`–`5` switch views. The
  handler ignores events whose target is an input, select or textarea.

A status line reports mode (`spectator`), the resolved data directory, session count, strategy count,
live-match count and `offline: true` (`GET /api/status`, `ViewerStatus`).

## HTTP endpoints

All are mapped in `WIP/Wip.Tholdem.Web/TholdemWebHost.cs`:

| Method and path | Purpose |
|---|---|
| `GET /` | 302 to `/viewer/index.html` |
| `GET /viewer/` and `/viewer/*` | static viewer assets (`index.html`, `app.js`, `viewer.css`) |
| `POST /api/live` | start a paced match; body `{seed, hands, seats?, agents?, startingStacks?, seat0Agent?, seat1Agent?, delayMs?, allowWeakSeed?}` → 201; see the N-seat body below |
| `GET /api/live/{id}` | current `SpectatorSnapshot`: the legacy two-seat fields plus one `seats[]` entry per seat (404 if unknown) |
| `DELETE /api/live/{id}` | stop a match → 204 |
| `POST /api/live/{id}/pause` / `.../resume` | pause or resume → `{status}` |
| `GET /api/sessions` | recorded sessions + skipped count; each summary carries `seats` and `startingStacks` |
| `GET /api/sessions/{sessionId}` | manifest detail + per-hand summaries; each summary carries per-seat `holeCards`, per-seat `finalStacks`, per-seat `handNames` and the recorded `pots` layers |
| `GET /api/sessions/{sessionId}/hands/{handNumber}` | one recorded hand as JSON (replay UI); `holeCards` is one list per seat and `holeCardsSeat0/1` remain |
| `GET /viewer/table?session=…&hand=…` | server-rendered replay table fragment (HTML), 2–6 seat panels |
| `GET /api/strategies` / `GET /api/strategies/{id}` | strategy list/detail |
| `GET /api/ledger` | ledger entries as JSON |
| `GET /api/champion` | active champion pointer (404 if absent) |
| `GET /api/status` | viewer status |
| `POST /api/training` | start a training job (`202 {jobId, acceptedAtUtc, optionsHash}`; `400` field error; `409` one-at-a-time/seed-reuse) |
| `GET /api/training` / `GET /api/training/{jobId}` | recent jobs / one job snapshot with `status`, `phase`, `iteration`, `lastReport`, `entries`, `canPromote` |
| `DELETE /api/training/{jobId}` | cooperative cancel (`202`); terminal jobs are `409` |
| `POST /api/training/{jobId}/promote` | `{confirm:true}`; re-reads/recomputes/re-verifies the persisted report server-side (`200` or `409`) |
| `POST /api/rsi` | start a bounded mini-RSI job (`202`; same bounds/one-at-a-time rules) |
| `GET /api/rsi/{jobId}` / `DELETE /api/rsi/{jobId}` | mini-RSI snapshot / cancel |
| `POST /api/rsi/{jobId}/promote` | `{confirm:true}`; re-verified proposer promotion (never the strategy champion) |

The training and mini-RSI bodies, phases, bounds, seed domains and honest limits are specified in
[rsi.md](rsi.md).

### N-seat live request body

```json
{
  "seed": 42,
  "hands": 3,
  "seats": 4,
  "agents": ["random-legal", "tight-aggressive", "pot-odds-equity", "random-legal"],
  "startingStacks": [1000, 800, 600, 400],
  "delayMs": 0,
  "allowWeakSeed": true
}
```

- `seats` is optional and defaults to `2`; it must be `2..6` or the response is
  `400 {"error":"invalid-seats","min":2,"max":6,"seats":…}`.
- `agents` must hold exactly one of `random-legal`, `tight-aggressive`, `pot-odds-equity` per seat;
  a count mismatch is `400 {"error":"invalid-agents", …}` and an unknown name is
  `400 {"error":"unknown-agent","seat":…,"agent":…}`. For `seats: 2` the pre-multiway
  `seat0Agent`/`seat1Agent` fields are still accepted; `agents` wins when both are present.
- `startingStacks` is optional; when present it must hold exactly one value per seat and every
  value must cover the big blind (`10`), otherwise
  `400 {"error":"invalid-starting-stacks", …}`.
- The seed guard is unchanged: a seed below `1099511627776` without `"allowWeakSeed": true` is
  `400 {"error":"weak-seed","minimum":…}`.

`GET /api/live/{id}` returns the existing fields plus `seats`, one entry per configured seat for
every match status:

```json
{"seats":[{"seat":0,"agent":"random-legal","stack":1000,"holeCards":["2h","Ah"],
           "folded":true,"allIn":false,"handName":"One Pair, Threes",
           "winner":false,"dealer":false}, …]}
```

`stacks`, `holeCardsSeat0`, `holeCardsSeat1` and `handNames` remain present; the two hole-card
fields describe seats 0 and 1 and `stacks`/`handNames` hold every seat, so old two-seat clients keep
reading the same fields.

## Spectator view vs agent surfaces

The spectator projection **deliberately shows every seat's hole cards**: `SpectatorSnapshot` carries
one `SpectatorSeat` per configured seat (2–6), and the legacy `HoleCardsSeat0`/`HoleCardsSeat1`
members describe seats 0 and 1. It is produced only inside the web host for `/api/live/{id}`
(`Live/SpectatorSnapshot.cs`, `Live/LiveMatch.cs`). It is never passed to an agent.

Agents receive `PlayerObservation` (`WIP/Wip.Tholdem/History/PlayerObservation.cs`), which has the
acting seat's own hole cards and nothing hidden, plus an `AgentTurnContext`
(`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`) carrying only the hand number, the acting seat and the
per-seat action index. The context has no seed of any width, no digest and no other secret-derived
value, so no agent input contains a stored value that can be inverted to the session seed or the
deck. That is a statement about the agent surface, not about the deal: the deal is deterministic
from the session seed, so an observer that holds its own cards can enumerate candidate seeds and
rebuild the deck when the seed is guessable. Entry points therefore require seeds at or above
`1,099,511,627,776` unless a demo opt-in is used; see the
[threat model](known-limits.md#threat-model). Agent randomness is derived from the agent's own
observation: every baseline and `StrategyAgent` computes
`SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), hand, seat, actionIndex, stream)`
(first eight big-endian bytes of
`SHA-256("tholdem-agent-seed|<canonicalObservation>|<handNumber>|<seat>|<actionIndex>|<stream>")`,
invariant formatting; `WIP/Wip.Tholdem.Agents/SeedDerivation.cs`). The observation itself contains no
seed, no deck order and no opponent cards. The runtime-agent sources are scanned for `TableState`,
`SpectatorSnapshot`, `HoleCardsSeat0`, `HoleCardsSeat1` and `Deck` by `RuntimeSourceBoundaryTests`,
so the agent path cannot reach the spectator projection.

## Offline and loopback

The host binds `127.0.0.1` by default and the viewer assets reference only same-origin paths
(`viewer.css`, `app.js`, `data:` favicon); there is no external script, font, CDN or telemetry. With
the default binding the only socket is the loopback HTTP listener; for a live match, agents run
in-process. The viewer is a developer/spectator tool: opening it does not start, fund or publish
anything.
