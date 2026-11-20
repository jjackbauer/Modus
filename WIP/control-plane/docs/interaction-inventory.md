# Interaction inventory and critical operator journeys

Status: normative PR 0 artifact. This document is the interaction contract for every route. A page
that cannot answer the three IA-01 questions below is not finished, regardless of how it looks.

Verified against branch `feature/control-plane-ux-remediation` at `bce73a5`. File references are
`path:line` in this worktree. Status values are governed by `status-glossary.md`; visual rules by
`design-system.md`.

## 1. The IA-01 questions

Every surface answers, in this order and without leaving the page:

1. **What is true now?** One authoritative state, labelled in words, plus the time and source of the
   fact. Derived indicators are secondary and never replace the authoritative state.
2. **What can I do?** The legal next actions for this operator's role, with the reason a control is
   unavailable. A disabled control states why; it never looks merely decorative.
3. **What evidence supports that?** The exact object, immutable identity (digest/SHA), verdict and
   timestamp behind the claim, available on the same surface or one deliberate disclosure away.
4. **What changed?** Only where it aids diagnosis: last transition, last failure, last successful
   update. Never a decorative activity stream.

Invariant: none of these may require the operator to infer a factory capability. If the adapter does
not expose the fact, the UI says "Not measured" or omits the claim; it does not fabricate it.

## 2. Route inventory

`web/src/lib.rs:24-37` declares the SPA routes; `/login`, `/docs` and `/` are server-rendered
(`server/src/app.rs:29-36`). Every SPA route is prefixed by the factory instance.

| Route | Operator job | What is true now | What can I do (primary) | Decision-critical evidence | Required states | Source |
|---|---|---|---|---|---|---|
| `/login` | Prove identity; establish a session | Signed-out; no factory data is visible | Sign in (primary) | The login result and its error code; next URL | default, invalid credentials, disabled account, server error, session-expired redirect | `server/src/app.rs:30`, `:397-410`, `:530-545` |
| `/` | Reach the operator's instance | Instance selected by session | Continue to `/i/{instance}/overview` | None | signed-in redirect, signed-out redirect | `server/src/app.rs:29`, `:160-175` |
| `/i/:instance/overview` | Decide what needs a human now; see what the factory is doing | Six KPI tiles, attention feed, dispatch status, capacity, recent releases, live activity | Start dispatch cycle (write); inspect one attention item; open pipeline/releases | Attention `what/why/safe action/consequence`, exact approval digests (short), event evidence, serving state | populated, empty factory, loading, error, stale projection, disconnected dispatch, denied write | `web/src/pages/overview.rs:52-289` |
| `/i/:instance/work` | Find, filter, create and open work | Table/Kanban/Priority projection; counts of Shown, Awaiting authorization, Active execution, Terminal failures; optional intake form | New work item; search/filter/sort/save view; open item | Factory state, derived indicator, priority, event count, updated, last failure | populated, no matches, empty factory, loading, error, denied write, intake preview rejected | `web/src/pages/work.rs:163-248`, `:271-475`, `:478-748` |
| `/i/:instance/work/:id` | Decide the next legal action for one work item, with evidence | State, derived indicator, health, version, repository and digests; workflow, contract, review matrix, release, serving, planning | Context-dependent legal action (dispatch, freeze/reject contract, authorize merge, start/stop serve) | Frozen contract + scenarios + digests, four mandatory checks, review/release digests, merge authorization object, events, diff, lineage | populated, loading, error, not found, denied role, stale resource, disconnected stream, empty tab | `web/src/pages/work_detail.rs:264-396`, `:400-496`, `:550-607`, `:707-742` |
| `/i/:instance/quality` | Decide pending authorizations and read review evidence | Counts of contract/merge/template approvals; approval inbox; review matrix; acceptance boundary | Review and authorize contract/merge on the work item; approve a template policy | Exact object digest, binding conditions, effect, stale reason, four-check matrix | pending, empty inbox, loading, error, stale authorization, denied (template hidden for non-admin), disconnected | `web/src/pages/quality.rs:43-101`, `:259-309`, `:312-359` |
| `/i/:instance/releases` | Distinguish reviewed candidate, merged revision, verified release and running process | One row per release with state, merged revision, verification, serving, merged time | Open a release | Factory state, merged SHA, verification verdict, serving state, merged timestamp | populated, empty, loading, error, stale serving journal, disconnected | `web/src/pages/releases.rs:29-100` |
| `/i/:instance/releases/:id` | Verify the release record and govern its supervised process | Header state/verified/serving, candidate→merged→verified→serving chain, verification, process, boundaries | Start or stop supervised serve | Merged/tree/head/base SHAs, build/regression/contract digests, record digest, process identity, last probe, serving state | verified / not verified / failed, serving / starting / stopping / stopped / expired / unknown, loading, error, not found, denied | `web/src/pages/release_detail.rs:24-251` |
| `/i/:instance/pipeline` | Diagnose where work is queueing and where failures cluster | Stage overview (9 stages) with queue/in-flight/waiting/failed, derived durations, per-stage work list, optional replay | Replay at an instant; return to live; open a work item | Stage counts, measured/not-measured durations, completed-in-window, generated timestamp | live, replay, empty, loading, error, not measured | `web/src/pages/pipeline.rs:34-164` |
| `/i/:instance/agents` | Separate roles, profiles, sessions and attempts | Metric strip (roles, sessions, attempts, tokens), nine role cards | Open a role | Session/attempt counts, active profile, last activity, token measurement basis | roles-only, zero sessions, loading, error, not measured, disconnected | `web/src/pages/agents.rs:31-101` |
| `/i/:instance/agents/:definition_id` | Distinguish role definition from actual execution; govern profile version | Role constraints, profile versions, session evidence, improvement/adaptation, measured performance, attempt history | Publish a profile version (admin); promote an evaluated version; refresh | Profile versions/promotion history, sessions/tool traces, attempt outcomes, measured/not measured performance | populated, no profiles, loading, error, denied non-admin, not measured | `web/src/pages/agent_detail.rs:83-197`, `:394-440` |
| `/i/:instance/analytics` | Diagnose throughput, quality, cost and missing telemetry | Window, measured/unmeasured counts, 12 measured metrics, 5 explicit Not measured, four series panels, definitions | Refresh; read a metric definition | Window bounds, metric definition, numerator/denominator, series points | measured, insufficient data, unmeasured, loading, error, stale | `web/src/pages/analytics.rs:34-174` |
| `/i/:instance/improvement` | Inspect adaptation evidence without claiming self-improvement | Role selector, immutable profile history, adaptation events, session outcomes, active profile | Select a role; inspect the role's profile history | Version chronology, proposal/applied/observed events, session outcomes | no versions, no events, loading, error, denied | `web/src/pages/improvement.rs:57-201` |
| `/i/:instance/settings` | Administer instance, operators, templates and audit; see what is immutable | Instance facts, settings table (value/source/mutable), policy versions, registered instances, templates, users, password, two audit tables | Register instance; create operator; reset/change password; register and approve template; refresh | Source and mutability per setting, template digest, audit operation ids and results | admin, denied non-admin, empty audits/templates/users, loading, error, stale registry | `web/src/pages/settings.rs:81-178`, `:181-256`, `:258-368`, `:408-498` |
| `/docs` | Navigate deployment/reproduction support | Static documentation page | Follow corrected commands/links | None | static | `server/src/app.rs:35`, `server/static/docs.html` |
| unmatched route | Recover without a dead end | "Not found" | Return to the factory | None | SPA fallback and server 404 | `web/src/pages/not_found.rs:3-12`; `server/src/app.rs:91` |

### 2.1 Role and authority facts

- The session exposes one role: `viewer`, `operator` or `admin` (`web/src/context.rs`), mirrored by
  the server (`server/src/auth.rs`). Server-side checks are authoritative; UI disabling is a
  courtesy, never the control.
- Write actions are hidden or disabled by `context.can_write()`; administration by
  `context.is_admin()` (for example `settings.rs:522`, `agent_detail.rs:425`,
  `work_detail.rs:283-297`).
- An unavailable action must state the reason next to the control
  (`work_detail.rs:444-462` is the model for this).
- No UI path may create a factory transition the adapter does not expose. The empty
  "Next legal actions" state says exactly that (`work_detail.rs:445`).

## 3. Required interaction states

`P` populated, `E` empty (valid, no rows), `L` loading, `X` error, `D` authorization denied,
`S` stale (fact exists but may not be current), `N` network/stream disconnected.

| Surface | P | E | L | X | D | S | N | Current gaps |
|---|---|---|---|---|---|---|---|---|
| Overview | yes | yes (attention/approvals/releases) | yes | yes | dispatch disabled | warn notice (`overview.rs:132-138`) | dispatch shows unavailable (`overview.rs:251`) | No last-successful-sync time; disconnected is transport-only text |
| Work queue | yes | "no matches" only (`work.rs:300-305`) | yes | yes | new-item control still offered | none | none | Empty-factory and no-matches share one message; no URL persistence; no clear-filters |
| Work detail | yes | per tab | yes | yes | disabled with reason | none | stream state only in shell | No stale marker when contract/review changes; header duplicates status |
| Quality | yes | yes | yes | yes | template approve hidden, no reason | stale card notice (`quality.rs:282-284`) | none | Contract/merge row requires a second navigation to see the object |
| Releases | yes | yes | yes | yes | n/a | stale journal rendered as serving until A-04 | none | Verification and serving are both chips; no "what changed" |
| Release detail | yes | n/a | yes | yes | serve disabled | serving-view detail (`labels.rs:161-167`) | none | Two different serve confirmation implementations |
| Pipeline | yes | per stage | yes | yes | n/a | replay banner (`pipeline.rs:81-87`) | none | Nine stage cards plus nine lists duplicate the same counts |
| Agents | yes | zero sessions shown | yes | yes | n/a | token basis shown | none | Metric strip duplicates card metadata |
| Agent detail | yes | yes | yes | yes | publish disabled, no reason | none | none | "Denied" reason missing |
| Analytics | yes | insufficient-data path | yes | yes | n/a | window shown as data, not freshness | none | Missing last refresh time |
| Improvement | yes | yes | yes | yes | n/a | none | none | Role selector duplicates agent list |
| Settings | yes | audits/templates/users | yes | yes | admin sections gated | none | none | 390px document overflow; no section navigation |
| Dialogs | yes | n/a | busy guard | typed error inline (`work_detail.rs:1390-1415`) | role check before opening | stale digest rejected | n/a | Background is not inert (portal required) |

Rule: an empty state has a next step, an error has a safe retry and correlation detail, and a
disconnected transport never renders as a factory failure (`status-glossary.md` §5, §7).

## 4. The eight critical journeys

Each journey is the acceptance path for VG-03. "Safety gate" means the journey fails if the condition
is missing, even when the screen looks correct.

### J1 — Open navigation and find a work item

- **Route:** any → `/i/{instance}/work`.
- **Happy path:** open the navigation (desktop rail or mobile drawer) → choose Work queue → enter a
  term in Search → choose a state → open the matching row.
- **Safety gate:** navigation is read-only and never implies factory state; the active route is
  marked `aria-current="page"`; a mobile drawer closes on Escape and returns focus to the menu
  button; opening a route closes the drawer.
- **Keyboard:** `Tab` to Menu → `Enter` opens → `Tab`/`Shift+Tab` cycle within the drawer →
  `Escape` closes and restores focus → `Tab` to Search → type → `Tab` to the row link → `Enter`.
- **Current:** horizontal nav scroller at ≤860px (`app.css:1547-1581`), no menu button, no drawer,
  no focus restore in `bce73a5`. **PR 2 replaces this** with the drawer described in §10; the
  screen-reader announcement remains open. **Owner:** PR 2.

### J2 — Search, filter and re-use a queue view

- **Route:** `/i/{instance}/work`.
- **Happy path:** type a search term → choose a factory state and a derived indicator → choose a
  sort → switch Table/Kanban/Priority → save the combination under a name → clear filters.
- **Safety gate:** filters only change the projection; every count says what it counts ("Shown");
  the active sort is stated in words; Clear filters is visible whenever any filter is active; a
  saved view never widens the operator's authority.
- **Keyboard:** `Tab` through Search, State, Indicator, Sort, view switch and saved views; native
  selects keep arrow-key behavior; `Enter` activates a view; the Clear-filters button is reachable
  without leaving the toolbar.
- **Current:** PR 2 adds visible labels, the Filters toggle, Clear filters and mobile cards. Saved
  views still have load/save only, and the URL is not yet the addressable state
  (`work.rs:182-230` predates PR 2). **Owner:** PR 2 (toolbar, done) and PR 3 (saved-view CRUD).

### J3 — Intake a requirement and submit the exact document

- **Route:** `/i/{instance}/work` (intake panel).
- **Happy path:** New work item → title, requirement, acceptance expectations, optional contract
  JSON → Validate with factory parser → read the accepted class, scenarios and any errors → tick the
  exact-document confirmation → Submit to factory.
- **Safety gate:** Submit is disabled until the parser accepts and the operator confirms
  (`work.rs:705-713`); the generated requirement shown in the preview is the exact document sent;
  a rejected document cannot be submitted; on failure every entered value is retained.
- **Keyboard:** `Tab` into the form; disclosures with `Enter`/`Space`; Validate; review; `Space`
  confirms; Submit; focus lands on the first error when submission fails.
- **Current:** validation errors are a plain list without an error summary or field links
  (`work.rs:716-725`); draft state is not preserved across navigation. **Owner:** PR 3.

### J4 — Inspect the lifecycle and its evidence

- **Route:** `/i/{instance}/work/{id}`.
- **Happy path:** read state and next action → Lifecycle → Evidence → Source & diff → Lineage →
  Events → copy the digest that a decision would bind.
- **Safety gate:** tabs are read-only; every claim links to its record; identifiers required for a
  decision are never truncated; an empty tab explains what would create its data.
- **Keyboard:** focus the tablist → `ArrowLeft`/`ArrowRight` move selection and focus → `Home`/`End`
  jump to the ends (`work_detail.rs:1210-1252`); `Tab` enters the panel; disclosures open with
  `Enter`/`Space`; no focus loss on tab change.
- **Current:** the tablist is correct, but the tab strip scrolls horizontally at mobile width with
  no labelled affordance (`app.css:1862-1867`); some decision digests are shown short outside the
  dialog (`work_detail.rs:304-306`). **Owner:** PR 3/PR 4.

### J5 — Freeze or reject a contract proposal

- **Route:** `/i/{instance}/work/{id}` → Approve contract / Reject proposal.
- **Happy path:** open either dialog → read the requirement text, supported class, executable
  scenarios, acceptance expectations, unresolved questions, effect and resulting state → verify the
  full 71-character proposal digest → tick the truthful assertion (or record a rejection reason) →
  submit.
- **Safety gate:** a non-executable proposal shows a prominent refusal and cannot be approved
  (`work_detail.rs:1590-1596`); approval submits the digest that was displayed; a changed proposal
  invalidates the confirmation; rejection is durable and records the exact digest
  (`work_detail.rs:1454-1481`).
- **Keyboard:** opening moves focus into the dialog; `Tab`/`Shift+Tab` stay inside; `Escape` closes
  only when no mutation is in flight; assertion, then submit; on error the dialog stays and keeps
  input; closing restores focus to the invoking button.
- **Current:** focus, trap, Escape and restore exist (`web/src/components/modal.rs:59-131`); the
  background is not inert (known gap; portal in PR 2). Two entry buttons open the same dialog
  (`work_detail.rs:288-290`, `:600`). **Owner:** PR 1 (done) / PR 2 (portal).

### J6 — Authorize a merge

- **Route:** `/i/{instance}/quality` → "Review and authorize on the work item" →
  `/i/{instance}/work/{id}?authorize=merge` (deep link; `quality.rs:115-118`) → dialog.
- **Happy path:** read reviewed head and base, source revision, accepted verdict, four mandatory
  checks with digests, frozen contract digest, binding conditions and consequence → copy the full
  review digest → type the exact digest → Authorize merge.
- **Safety gate:** the typed value must equal the review digest (`work_detail.rs:1776-1786`,
  server revalidates); stale evidence disables confirmation with a plain-language cause; one pending
  merge yields exactly one decision (A-05); the dialog states that authorization is not acceptance.
- **Keyboard:** deep link opens the dialog and moves focus inside; `Tab` through the evidence;
  type the digest; submit is disabled until exact; `Escape` when not busy; focus returns to the
  authorizing control.
- **Current:** the decision dialog is implemented (`work_detail.rs:1439-1795`); the background is
  not inert; Quality no longer holds inline merge/template handlers (`quality.rs`) — it links to
  the work item for contract/merge and to Settings for template policy. **Owner:** PR 1 (done) /
  PR 2 (portal, dead path removal).

### J7 — Start and stop a supervised serve

- **Route:** `/i/{instance}/releases/{id}` and `/i/{instance}/work/{id}`.
- **Happy path:** read serving state and last observation → Start supervised serve → choose the
  bounded window (10–300 s) → confirm → wait for the adapter to confirm a reachable process → open
  preview → Stop supervised serve → confirm.
- **Safety gate:** the command is offered only for a released/validated item with no active window;
  no optimistic "serving" before the adapter confirms; an old journal row never renders as serving
  (`status-glossary.md` §5); the same governed confirmation is used on every surface; a serve window
  never implies acceptance.
- **Keyboard:** open the confirmation from the only serve control; `Tab` to the duration field;
  submit; `Escape` only when safe; status changes are announced by `role="status"`.
- **Current:** serve is offered in three places on the work item (`work_detail.rs:291-297`, `:730`,
  `:443-467`) and again on the release (`release_detail.rs:108-131`), with two different dialog
  implementations (`work_detail.rs:1483-1545` vs `release_detail.rs:255-400`). **Owner:** PR 4
  consolidation; PR 2 keeps the existing dialogs but must not add a third.

### J8 — Administer operators, templates and audit

- **Route:** `/i/{instance}/settings`.
- **Happy path:** read instance identity and configuration provenance → create/reset an operator →
  register a template manifest → approve the exact manifest digest → read both audit tables and
  follow an operation id.
- **Safety gate:** admin-only sections are absent or disabled for non-admins with a reason; a
  template is not approved by registration, only by exact digest; secrets never render; immutable
  settings are labelled with their source and "mutable: no"; audit rows are append-only and carry
  the operation id.
- **Keyboard:** reach each section in reading order; form labels are persistent; submit errors
  retain values and focus the error summary; template confirmation requires the digest assertion.
- **Current:** one long page; the 390px baseline measured 482px document width. PR 2 containment
  removes the document overflow (0 at 390px in the measured gate) but does not add section
  navigation; template approval still exists both here (`settings.rs:500-546`) and in Quality
  (`quality.rs:287-295`). **Owner:** PR 5 (and the duplicate decision in §7).

## 5. Cross-surface truth table

The adapter is the authority; `domain/src/labels.rs` maps it once. A surface may reformat but not
re-derive.

| Fact | Authority | Overview | Work queue | Work detail | Quality | Releases / detail | Pipeline | Settings / Analytics | Invariant |
|---|---|---|---|---|---|---|---|---|---|
| Factory state | `WorkItemSummary.state` | stage-strip counts only | table/kanban/priority label | header, single label | — | releases list column | stage membership | analytics lead-time bucket | Same item renders the same label everywhere; unknown values verbatim |
| Derived indicator | `derived_indicator` | attention item | table column | header, only when different from state | — | — | per-stage attention lists | — | Never replaces the state; never a second chip for the same fact |
| Pending merge decision | approvals + `canonical_attention` | one attention row + pending-authorizations row | indicator | merge panel, one dialog | inbox row + count | — | stage "Merge authorization" | quality count | One decision, one row, one authorization surface |
| Pending contract decision | contract + approvals | attention row | indicator | contract panel + dialog | inbox row + count | — | stage counts | quality count | Non-executable proposals are not approvable |
| Serving state | adapter journal + process + `serving_view` | recent-release row | — | serving panel | — | list + release detail | serving stage | — | `active=false` never renders as Serving; same label on every surface |
| Release verification | release record | recent-release row (state only) | indicator | release evidence panel | review matrix (pre-merge) | list + detail | release stage | — | A merge is not verification; verification is not acceptance |
| Attention count | `canonical_attention` | "N item(s)" | awaiting/terminal counts (different definitions) | compact context | inbox counts | — | failed stage count | analytics "recorded stage failures" | Every count names its definition and drills down |
| Freshness | adapter timestamps + SSE state | generated/last event | — | updated timestamp | created timestamp | verified/merged times | generated timestamp + replay mode | analytics window | Transport disconnection is not factory failure; last success is visible |
| Exact identity | adapter digest/SHA | short in text (secondary) | — | short facts, full in dialogs | full digest on the card | short in table, full in detail | — | short where secondary | A digest being bound to a decision is always full, wrap-safe, copyable |
| Role entitlements | session role | dispatch disabled | intake write | action buttons | template admin | serve write | — | admin sections | Server enforces; UI states the reason |

Known truth defects to keep visible until closed:

1. The work header renders up to five chips for the same item (`work_detail.rs:273-279`), which
   invites contradictory readings. Target: state text once + derived indicator once + health line.
2. Overview attention previously described one merge twice; A-05 deduplicates at the server
   (`server/src/overview.rs:183-273`), but Overview still also renders the approvals panel for the
   same decision (`overview.rs:203-224`). The target keeps one row and links to the decision.
3. Work-detail and release-detail serve actions are independent implementations with different copy
   and confirmation bodies; until consolidated, labels and safety text must match verbatim.

## 6. Measured baseline (19 November 2026, live demo instance)

Captured by `.evidence/pr2-capture.cjs` (untracked) against the running app at HEAD `bce73a5`.

- Document horizontal overflow at 390px: Overview 578px, Quality 726px, Releases 722px,
  Settings 482px (the earlier 482px B-04 measurement is reproduced here).
- Visible text below 12px: 68 nodes on Overview at 390px, 147 on Settings at 390px. Token values:
  `--text-2xs: 10px`, `--text-xs: 11px`, `--text-sm: 12px`, `--text-base: 13px`
  (`server/static/app.css:14-21`).
- `--text-faint` contrast: 3.19:1 dark, 3.00:1 light (fails 4.5:1) as recorded in
  `design-system.md` §2.
- Navigation at ≤860px is a horizontal scroller (`app.css:1547-1581`), not a drawer; no menu button
  exists in the shell (`web/src/components/shell.rs:26-104`).

These numbers are the "before" side of the PR 2 gates; the after measurements are recorded in
`design-system.md` and the PR evidence.

## 7. Duplicates to consolidate

Every row is a current duplication with a decision. "Owner" is the PR that removes it.

| # | Duplicate today | Evidence | Consolidation decision | Owner |
|---|---|---|---|---|
| D-01 | One pending merge appears in four places | `overview.rs:172-182` attention, `:203-224` pending authorizations, `quality.rs:68-75` inbox, `work_detail.rs:285-287` header + `:469-481` panel | Overview shows one attention row and one "Pending authorizations" summary that deep-links; Quality is the ledger; the work item is the only authorization surface | PR 3 |
| D-02 | Contract approval has two entry buttons on one page | `work_detail.rs:288-290` header, `:600` contract panel | **Done differently:** one header action row owns approve/reject; the contract panel keeps the evidence and links to the row | PR 3 |
| D-03 | Serve is offered from four controls and two dialogs | `work_detail.rs:291-297`, `:730`, `:443-467`; `release_detail.rs:108-131`, `:255-400` | **Done:** release detail owns start/stop; work detail reports state and deep-links to it. Its `ServeConfirm` still uses local markup, so the shared confirmation is a PR 4 item | PR 4 |
| D-04 | Template policy approval exists twice with different assertions | `quality.rs`; `settings.rs` | **Closed:** Quality keeps a read-only row that links to `Settings#approved-templates`; the inline approve/reject code is removed. Settings owns the template decision | Done |
| D-05 | Status chips repeat the same facts in the work header | `work_detail.rs:273-279` | State text once, derived indicator only when different, one health line; remove "CONTRACT FROZEN"/"DISPATCH ACTIVE" chips (the panels show those facts) | PR 3 |
| D-06 | KPI grids repeat counts already present in context | `overview.rs:154-168`, `work.rs:271-296`, `quality.rs:62-66`, `pipeline.rs:115-120`, `agents.rs:60-72` | Keep a summary row only where it changes a decision; put counts in the heading of the list they count; no six identical tiles | PR 3/PR 4 |
| D-07 | Refresh is a per-page button and there is no global freshness | `overview.rs:60`, `work.rs:171`, `quality.rs:53`, `releases.rs:39`, `pipeline.rs:52`, `agents.rs:41`, `analytics.rs`, `improvement.rs` | Deferred: keep page buttons until `FreshnessIndicator` lands, then one shell refresh with "last successful update" | PR 3 |
| D-08 | Breadcrumbs plus in-page back links plus the page title | `shell.rs:88-99`, `:156-191`; `work_detail.rs:268`; `release_detail.rs` back link | Breadcrumbs are the only navigation trail; remove back links; breadcrumbs carry the exact ID | PR 2/PR 3 |
| D-09 | Three separate state→stage mappings | `overview.rs:405-423`, `work.rs:362-378`, `pipeline.rs` stage data | One mapping in the domain/UI layer, consumed by all three | PR 3 |
| D-10 | Short digest and full digest of the same value on adjacent surfaces | `ui.rs:73-80` (`DigestLine` short) vs `:158-206` (`DigestCopy` full); `work_detail.rs:304-306`, `quality.rs:270-271` | `EntityIdentity` for IDs, `DigestCopy` for any digest a decision binds; short form only for glanceable secondary references with reveal/copy | PR 2/PR 3 |
| D-11 | Attention rendered again inside the work item | `work_detail.rs:311-315` | **Done:** one attention line naming the count and titles, linking to the action row | PR 3 |
| D-12 | Role/profile view duplicated between Agents, Agent detail and Improvement | `agents.rs:74-91`, `agent_detail.rs:117-197`, `improvement.rs:112-198` | Agents owns the inventory, agent detail owns the role, Improvement deep-links to the role instead of re-rendering it | PR 5 |
| D-13 | Search/filter state has two homes (component signals and localStorage) with no URL | `work.rs:182-230` | **Done:** the URL holds search, state, attention, sort and view; localStorage stores only named views | PR 3 |
| D-14 | Status is carried by a chip instead of a label in several lists | `releases.rs:78-84`, `agents.rs:81`, `work_detail.rs:719` | `StatusIndicator` with text + tone; chips only for counts/tags, never as the sole status carrier | PR 2/PR 3 |

## 7.1 Verification gates after the NVDA/state round

| Gate | Status | Evidence |
|---|---|---|
| VG-01 responsive | Measured at 7 widths for 4 routes and 25 dialog cases, both themes; 0 overflow, 0 clipped actions; text spacing 0 overflow; 200% zoom tested as a 720 CSS-px layout equivalent, not a physical zoom | `vg-checks.json`, `dialogs-check.json`, `zoom-check.json` |
| VG-02 readability | Met for automated AA: axe-core 4.11, 17 runs both themes, 0 violations; 15/13/12px tokens, no text below 12px, text spacing measured. Manual contrast review still desirable | `axe-results.json`, `vg-checks.json` |
| VG-03 keyboard + screen reader | Met with notes: real NVDA 2026.3.1 pass over navigation, queue, table, detail tabs, evidence, merge/contract/serve dialogs and intake errors (53/55 scripted expectations; two documented script misses); keyboard-only mutations covered by the E2E; route focus fixed | `vg03-nvda.json`, `nvda-vg6.log`, `e2e-verify.json` |
| VG-04 safety/truth | Met: digest binding, serving consistency, one-decision-per-merge, wrong-digest and repeat refusal, and a moved-target stale decision refused with failure retention (`approval-target-moved`) | `e2e-verify.json`, `stale-merge.json`, `stale-submit-only.cjs` |
| VG-05 visual regression | Harness: 18 fixed-clock views (queue/work/release × 320/768/1440 × both themes), two captures, 0 changed pixels; baseline self-generated, independent approval pending | `visual-diff.json`, `.evidence/pr3/visual/baseline/` |
| VG-06 interaction states | Partial: loading, empty, transient error + reload, offline/resync, 404, viewer denial, stale intake, non-executable contract, empty agent/evidence tabs, the unmerged release state and the no-release empty state measured; queue/work detail/release/overview refresh failures retain data with a retry banner; overview/release initial projection errors show the reload retry | `vg06-states.json`, `vg06-detail-states.json` |
| VG-07 technical | Rust/wasm/browser green; .NET suite not re-run because no adapter source changed | `cargo-test-workspace.txt`, E2E JSON |
| VG-08 large data | Partial: 104 work items, 144 events, 1.33s queue projection, 0 overflow at 1440/390; below the 1000-event leg, no long diff/session fixtures, and the 105-item overview takes 32s server-side / 58s in the browser (adapter approvals >25s) | `vg08-large.json`, `overview-latency.json` |
| VG-09 design coherence | Open: no human visual review | — |
**Auth PR5:** API 401 redirects to `/login?next=…`; re-login returns to the page (queue → `/i/local/work` → 106 rows). Login fields are labelled, CSRF-protected, errors are `role=alert`, focus returns to username, 0 overflow at 320/1440.

**404/error polish:** server fallback has `lang=en`, theme-aware `data-theme`, meaningful title and one back link; axe clean, 0 overflow at 320/1440.

**Integrated docs PR5:** `/docs` follows `cp_theme`, has a back link to `/`, real title, 0 overflow at 320/1440 and axe-clean both themes; session-protected.

**B-04 settings navigation:** "On this page" nav with six anchors (Factory, Control-plane audit, Adapter audit, Operators, Password, Templates), all present; mobile audit scroll hints display; control root, target repository and adapter URL wrap only at path separators (`<wbr>`, 3 elements/19 breaks); 0 overflow at 320/1440, axe clean both themes. The redundant release in-page back link is removed; "Work detail" remains.

**Settings/audit PR5:** instance/audit/template statuses use `StatusIndicator`; audit tables are named focusable regions; role selects have aria-labels; five `Transition` boundaries. 9 panels, 0 chips, 214 rows, 0 overflow at 320/1440, axe clean both themes.

**Analytics PR5:** 6 measured panels with definitions; `Transition` keeps them during refresh; 0 chips, 0 overflow at 320/1440, axe clean both themes.

**Improvement PR5:** Transition + StatusIndicator conversion; 5 panels, 0 chips, empty "No published versions"/"No adaptation events recorded" states, 0 overflow at 320/1440, axe clean both themes. The profile/adaptation/session status paths were not rendered because the selected role has no history.

**Agent activity PR4:** the Agents list and detail replace KPI cards with labelled summaries and definitions; definition IDs are mono identities; active-profile/attempt/session outcomes use `StatusIndicator`; both use `Transition`. 9 cards, 0 chips, 0 overflow at 320/1440, axe clean both themes.

**Pipeline PR4:** four KPI cards replaced by a labelled summary with definitions; stage counts are plain numbers; states use `StatusIndicator`; the board is a named, focusable scroll region; `Transition` keeps content during refresh. 9 stages, 0 chips, 0 overflow at 320/1440, axe clean both themes.

**Quality PR4 partial:** the review matrix uses `StatusIndicator` and `Transition` (0 chips, 4 rows, 0 overflow at 320/1440, axe clean); the approval-card kind label and template `Confirmation` are not browser-verified because the approvals inbox is empty at this fixture.

**C-01 primitives:** all named primitives exist; `Timeline` is now a semantic ordered list and is
wired into work-detail attempts/events, overview activity, agent sessions/adaptations and
improvement events. `InlineValidation`, `Confirmation` and `EvidenceSummary` are live in intake and
the decision dialogs. `ErrorBoundary` and `ActionToolbar` are available but not used on every route.
## 8. Decisions recorded by this inventory

1. **Template policy approval moves to Settings.** Registration and approval are the same object's
   lifecycle; keeping approval in Quality forced a second, weaker assertion. Quality links to
   Settings (D-04). Not implemented yet; PR 5.
2. **Release detail owns the serve mutation.** The release record owns process liveness; the work
   item shows serving state and links to the release. Until PR 4, both surfaces must use identical
   labels and the same confirmation semantics (D-03).
3. **One global refresh + last successful update.** Per-page refresh buttons are a duplication and
   hide freshness (D-07).
4. **No new status vocabulary.** All labels come from `domain/src/labels.rs`; a page needing a new
   word must extend the glossary first.
5. **Not measured is a state, not zero.** Missing telemetry keeps its basis sentence; it is never
   rendered as 0.

## 9. Traceability

| Requirement | Where satisfied |
|---|---|
| IA-01 | §1, §2, each route row |
| IA-02 | §2, J1, D-08 |
| IA-03 | §3, §5 |
| IA-04 | §2, §4, §7 |
| A-01 | Route table, work detail tabs (J4) |
| A-02 | J5 |
| A-03 | J6, D-01 |
| A-04 | §5 serving row, J7 |
| A-05 | §5 attention row, D-01 |
| B-01 | §6, J1 |
| B-02 | J2 |
| B-03 | §2 pipeline row, J4 |
| B-04 | J8, §6 |
| B-05 | D-03, D-05 |
| C-01 | §3, §7 component references |
| C-02 | J5, J6, J7 |
| C-03 | J2, J3, J4 |
| C-04 | §5, §8 |
| VG-01 | §6, J1 |
| VG-02 | §6 |
| VG-03 | §4 keyboard paths |
| VG-04 | §5 |
| VG-06 | §3 |

## 10. PR 2 foundation progress against this inventory

Recorded after the PR 2 foundation commit. This table is evidence of movement, not a claim that the
journeys are finished.

| Item | Status (through the PR 3 completion increment) | What is still missing |
|---|---|---|
| J1 navigation | Done: three task groups, `aria-current="page"`, ≤1024px drawer with Menu button, backdrop, Escape, Tab trap, focus in/return, body scroll lock, closed drawer removed from the tab order. Added: route-change focus moves to the new page h1; NVDA announced "Open navigation"/"Close navigation" and the new heading | A broader AT/drawer matrix is still under VG-06/VG-09 |
| J2 queue toolbar | Done: visible labels on every field, two-row toolbar, Filters toggle at ≤760px, Clear filters with a stated disabled reason, `aria-expanded`/`aria-controls`, labelled view switch, and purpose-built cards at ≤860px. Extended in the PR 3 completion increment: saved-view rename/delete with an active indicator, URL state, 280px board region with arrows and a hint, priority definition | Real screen-reader verification of the saved-view status announcement and the board scroll region (VG-03) |
| D-05 status repetition | Closed: one state line, one derived indicator, one health fact, one attention line, no merge panel; the header is the only decision surface | The attention line was not specifically exercised under NVDA; it remains in the VG-06 matrix |
| D-06 KPI grids | Done for queue, Overview, Quality, Pipeline and Agents (labelled summaries with definitions) | Settings/Analytics metric compositions remain |
| D-07 refresh | Deferred to PR 3 | Per-page Refresh buttons remain until `FreshnessIndicator` exists |
| D-08 breadcrumbs | Partially done: current crumb is not a link, instance crumb targets `/overview`, work-detail back link removed, labels match the new nav | Release detail still has an in-page back link |
| Responsive containment | Done and measured: 224 headless Chrome measurements (16 routes × 7 widths × 2 themes), 0 documents with horizontal overflow, 0 visible text below 12px | Keyboard-focusable table scroll regions and the remaining mobile data layouts (B-02/B-04) |
| Type and contrast | Done: 15px body, 13px metadata, 12px floor, faint tokens 7.50/8.10 dark and 5.43/5.06 light | Real 200% browser zoom; WCAG text spacing measured at 320/1440 with 0 overflow (VG-02) |
| J3 contract decision | Done: one header entry; dialog shows requirement text, declared scenarios, unresolved questions, effects and exact digest; `Confirmation` assertion; non-executable proposals stay disabled. NVDA read the dialog title, description and non-executable notice | A live approval under NVDA was not executed (the keyboard path is covered by the E2E; the dialog was read and cancelled) |
| J4 merge decision | Done: one header entry; `EvidenceSummary` + exact digest + typed `Confirmation`; the server still revalidates the digest. A recorded authorization now hides the button, makes dispatch primary, shows an execution notice, filters the transition list and changes the queue next step. Wrong, repeated and moved-target digests are refused with failure retention | A review re-run with a different digest was not separately constructed; the reviewed target moved and was refused with `approval-target-moved`, retaining evidence, checkbox, typed digest and the error banner |
| J5 serve | Done: release detail owns start/stop; work detail reports state and deep-links; live preview URL is enriched from `factory.release.observed.v1` and probed HTTP 200 in the E2E; NVDA read the serve dialog title, description and contract/URL rule. PR 4 conversion: `StatusIndicator` for state/verified/serving/outcome and the shared `Confirmation` assertion before start/stop | A fresh NVDA pass after the shared `Confirmation` was added was not run |
| J6 intake | Done: field summary with anchors, parser-code mapping, draft storage with a privacy note, exact-document `Confirmation`, stale-document invalidation | Per-route document titles (C-03) still generic; template approval remains in the Quality/Settings decision from D-04 |
| J7 end-to-end | Done on `WORK-b3a440a4`: intake → dispatch → contract → review → merge → post-merge verification → serve start/stop, both themes, 7 widths, keyboard paths, 0 errors; real NVDA pass added | A physical 200% zoom run, the complete VG-06 matrix, VG-08 event/scale legs and VG-09 |