# Wip.Factory Control Plane design system

Status: normative specification for the UI remediation (PR 0). Implementers follow this document;
deviations are design decisions that must be recorded here first.

North star: the authority and safety of an operations console, the clarity of a good issue tracker,
the traceability of a code-review tool, and the density control of a professional IDE. Not a neon
dashboard and not a card collection.

## 1. Principles

1. **Truth before decoration.** Every visual element must correspond to a fact the adapter returned.
   No invented metrics, tokens, agents, progress or health.
2. **One fact, one class, one place.** Authoritative state, derived indicator, verification verdict
   and available action are labelled by position, not by colour alone (see `status-glossary.md`).
3. **Accent is action.** The accent colour marks a primary action, active navigation, a link or a
   focus ring. It is never ambient decoration.
4. **Borders mark groups.** A border encloses a meaningful group. It is not the default separator
   and never nests a card inside a bordered card.
5. **Typography carries hierarchy.** Weight, size and alignment before uppercase and colour.
6. **Decisions show their object.** Any confirmation of a governed mutation shows the exact object,
   evidence and consequence inside the dialog, and binds the exact digest it will submit.
7. **Density is a tool, not a style.** Comfortable defaults; compact only for event/log tables.
8. **Nothing risky is duplicated.** One surface owns each risky action; other surfaces deep-link to it.

## 2. Colour

Dark theme is the default; the light theme is a peer, not an inversion. Both must satisfy the same
contrast bar.

Current tokens live in `server/static/app.css` (`:root` for dark, `[data-theme="light"]` for light).
Adopted semantic aliases: `--bg-canvas`, `--bg-panel`, `--bg-panel-raised`, `--border-subtle`,
`--border-default`, `--border-strong`, `--text-primary`, `--text-secondary`, `--text-tertiary`,
`--accent`, `--accent-strong`, `--accent-contrast`, `--success`, `--warn`, `--danger`, plus muted
variants. Status and interactive colours are consumed only through these aliases.

### Contrast gate (measured with WCAG 2.1 relative luminance)

| Pair | Before | After (PR 2) | Verdict |
|---|---:|---:|---|
| `--text-faint` on `--bg-panel` #11161f (dark) | **3.19:1** | **7.50:1** (#9aa8ba) | passes 4.5:1 |
| `--text-faint` on `--bg-canvas` #090c11 (dark) | **3.45:1** | **8.10:1** (#9aa8ba) | passes |
| `--text-tertiary` #79889b on `--bg-panel` (dark) | **5.01:1** | **5.01:1** | passes, unchanged |
| light `--text-faint` on #ffffff | **3.00:1** | **5.43:1** (#5f6b7a) | passes |
| light `--text-faint` on #f5f7fa | **2.80:1** | **5.06:1** (#5f6b7a) | passes |
| light `--text-tertiary` #68778b on #ffffff | 4.57:1 | **4.57:1** | passes, unchanged |

Measured with the WCAG 2.1 relative-luminance formula (`Get-Luminance` in the PR 2 evidence log). The
"After" values are the token values now in `server/static/app.css` (§12).

Rules:

- Normal text (below 18.66px bold / 24px regular) ≥ 4.5:1 in both themes.
- Large text and UI boundaries/focus indicators ≥ 3:1.
- Status colour is never the only signal: the label must read correctly in greyscale.
- Success, warning and danger are reserved for their semantics. Neutral counts and labels use
  `--text-secondary`, not a status colour.

## 3. Typography

Current tokens are 10/11/12/13px and a 13px body, which is below the operator bar. PR 2 implements
the target scale in `server/static/app.css`:

| Role | Size / line-height | Weight | Token (implemented) |
|---|---|---|---|
| Page title | 24px / 1.25 | 650 | `--text-xl` = 24px |
| Section title | 18px / 1.3 | 620 | `--text-lg` = 18px |
| Record title | 16px / 1.35 | 600 | `--text-md` = 16px |
| Body / form text | 15px / 1.55 | 400 | `--text-base` = 15px |
| Operator metadata | 13px / 1.45 | 450 | `--text-sm` = 13px |
| Secondary annotation | 12px / 1.4 | 400 | `--text-xs` = `--text-2xs` = 12px |
| Prohibited | 10px/11px | — | removed for user-facing data |

Rules:

- Body and standard form text 15px minimum. Dense metadata 13px. 12px only for true annotations;
  never for actionable content, IDs required by a decision, or table cells.
- Monospace only for work IDs, digests, SHAs, code, paths, raw events, logs and timestamps where
  precision matters. Prose, statuses and labels use the UI face.
- Counts use `font-variant-numeric: tabular-nums` and integers. Durations use `format_duration_seconds`.
- Identifiers required for a decision are never truncated: wrap-safe, full reveal, one-click copy
  (`DigestCopy`). `short_digest` is only for glanceable secondary references.
- Dates: human local time with the exact UTC timestamp available on hover/reveal.

## 4. Spacing, borders, elevation

- Space scale: 4, 6, 8, 12, 16, 20, 24, 32, 48 (`--space-1` … `--space-9`).
- Radii: 4px controls, 7px panels, 10px dialogs.
- A panel border uses `--border-subtle`; a section boundary uses `--border-default`; a focused or
  selected element uses `--accent`.
- Elevation is one dialog shadow. Cards do not stack shadows.
- Dialog width: 620px for forms, 980px (`.dialog-wide`) for decisions that show an object.
- Scroll containment: dialogs scroll their body; tables scroll inside `.table-wrap` with a visible,
  labelled affordance.

## 5. Status taxonomy and rendering

| Class | Placement | Form | ARIA |
|---|---|---|---|
| Authoritative state | Page header, once | Text label; tone is secondary | plain text |
| Derived indicator | Header, after state, only if different | Text + optional dot | plain text |
| Verification verdict | Owned by the release record | Text label + evidence digest | plain text |
| Available action | Action area / next-actions panel | Verb button; disabled with reason | button semantics |
| Freshness | Shell (`ConnectionIndicator`) + page | Text: Live / Reconnecting / Stale / Disconnected | `role=status` |

## 6. Component contracts (C-01)

Each primitive replaces page-local markup. Every one supports the listed states.

| Primitive | Purpose | States | Accessibility | Replaces |
|---|---|---|---|---|
| `PageHeader` | Route title, subtitle, actions | default, with/without actions | `<header>`, single `<h1>` | per-page `page-header` markup |
| `Section` | Titled group | default, collapsible | heading level from prop | `Panel`/`PanelActions` variants |
| `StatusIndicator` | One labelled status | tones + `detail` | text label always present | raw `Chip` for status |
| `HealthState` | Execution health | derived indicator + health | text + tone | header chips |
| `EntityIdentity` | Work/release identity | compact, full | copyable, `title` with exact value | `.tiny.mono` id strings |
| `DigestCopy` | Exact digest binding | none / value / copied | `aria-label` on copy, `aria-live` result | `DigestLine` for decisions |
| `MetadataList` | Label/value pairs | default, wrapped | `<dl>/<dt>/<dd>` | ad-hoc `.grid` label rows |
| `DataTable` | Tabular evidence | populated, empty, loading, error, scroll | `<caption>`, header scope, sort `aria-sort` | `.table-wrap table.data` copies |
| `ActionToolbar` | Page-level actions | default, busy, forbidden | disabled + reason | scattered `.page-actions` |
| `FilterBar` | Queue filters | default, active, saved view | labelled fields, clear-filters | queue toolbar |
| `EmptyState` | No data with a next step | static, action | heading + action link | `EmptyBlock` additions |
| `ErrorBoundary` | Route/panel errors | message, retryable, retry | `role=alert`, retry button | `error_view` |
| `FreshnessIndicator` | Data age + transport | live, reconnecting, stale, disconnected | `role=status`, last successful update | `ConnectionIndicator` text |
| `Tabs` | Section switching | roving focus | `role=tablist`, `aria-selected`, `aria-controls`, arrows/Home/End | current `tab_button` |
| `Dialog` | Modal decision | open, busy, error | focus in, Tab trap, Escape when safe, restore | per-page modal markup |
| `Confirmation` | Exact-digest confirmation | disabled until assertion | assertion text names what was shown | checkbox/typed digest blocks |
| `InlineValidation` | Field errors | error, hint, required | `aria-describedby`, error summary links | form error paragraphs |
| `Timeline` | Chronology | populated, empty | ordered list semantics | event/attempt lists |
| `EvidenceSummary` | Verdict → check → identity → raw | collapsed/expanded | disclosure button semantics | review cards |
| `DiffViewer` | Code diff | file list, patch, truncated | legend, copy path, keyboard scroll | `DiffView` |

Implementation notes:

- Shared code belongs in `web/src/components/`; a page-specific style is allowed only when this
  document records the exception.
- No new component may introduce hard-coded colours or spacing values.
- Prefer Leptos-native composition (`Children`, `AnyView`, signals) over a home-grown framework.

## 7. Dialogs and mutation feedback (C-02)

- On open: move focus to the first meaningful control inside the dialog; trap Tab/Shift+Tab;
  background is not reachable while open; `aria-modal="true"`, `aria-labelledby`, `aria-describedby`.
- Escape closes only when no mutation is in flight. Cancel disabled while busy.
- On success: close, toast with the object/version, refresh authoritative data. Never render success
  before the adapter confirms.
- On failure: keep the dialog, keep every entered value, and render the typed error inside it with a
  plain-language cause and correlation code.
- A submit handler is idempotent per click: `busy` guard plus a fresh idempotency key.
- Disabled buttons look disabled: neutral surface, tertiary text, `not-allowed` cursor.

## 8. Tabs, forms, loading, errors (C-03)

- Tabs: `role=tablist` with a label; each tab `aria-selected`, `aria-controls`, roving `tabindex`;
  Arrow/Home/End move selection and focus; one `role=tabpanel` with `aria-labelledby`.
- Forms: persistent visible labels, required markers, field-associated errors, one error summary
  linking to fields; preserve drafts across refresh where local policy permits.
- Parser codes map to human messages with field context; raw codes stay available in technical detail.
- Loading: retain the shape of the destination; respect `prefers-reduced-motion`.
- Errors: what failed, effect, next action, safe retry, correlation detail on demand.
- SSE: distinct live, reconnecting, stale and disconnected states; last successful update visible.
- Every route has a specific document title; breadcrumbs keep exact IDs.

## 9. Copy

Action verbs (use exactly these):

- Authorize merge · Freeze this exact contract · Reject contract proposal
- Start supervised serve · Stop supervised serve · Start dispatch
- View verification evidence · Review and authorize on the work item · Copy digest

Rules: sentence case for controls; no all-caps ordinary metadata; integers formatted without
decimals; no "0" where a value is unmeasured ("Not measured"); errors name the actor and the object.

## 10. Definition of done

- [ ] No user-facing 10/11px text; body ≥15px; metadata ≥13px.
- [ ] Faint text ≥4.5:1 in both themes (table above).
- [ ] Every status is readable without colour and comes from `labels.rs`.
- [ ] Decision dialogs show the object, full digest, consequence and a truthful assertion.
- [ ] Tabs and dialogs pass the keyboard paths in `interaction-inventory.md`.
- [ ] No document-level horizontal overflow at 320, 390, 768, 1024, 1280, 1440, 1920.
- [ ] Both themes reviewed on Overview, Queue, Detail, Approvals, Settings.
- [ ] Every new visual element is justified by a fact or an action.

## 11. Traceability

| Requirement | Sections |
|---|---|
| DS-01 | §1, §2 |
| DS-02 | §3 |
| DS-03 | §1.7, §4, §5 |
| DS-04 | this document |
| IA-03 | §5, `status-glossary.md` |
| IA-04 | §9 |
| C-01 | §6 |
| C-02 | §7 |
| C-03 | §8 |
| C-04 | §5, §9, `status-glossary.md` |
| VG-02 | §2, §3, §10 |

## 12. PR 2 foundation — implementation record

This section records what the code now does, so later PRs do not re-litigate the foundation.

### 12.1 Type and colour (implemented)

- `--text-2xs` and `--text-xs` are both **12px**; `--text-sm` **13px**; `--text-base` **15px**;
  `--text-md` **16px**; `--text-lg` **18px**; `--text-xl` **24px**; `--text-2xl` **30px**.
- `--text-faint` is **#9aa8ba** (dark) and **#5f6b7a** (light); measured 7.50/8.10 and 5.43/5.06
  against the panel and canvas surfaces (§2).
- Table headers no longer use all-caps; navigation group labels, form labels, status text, timestamps
  and table cells are 13px. 12px remains only for chips, table headers and true annotations.
- `--sidebar-width` is 248px; `--topbar-height` is 52px.
- The brand mark is a flat accent square; the glow rings on connection dots are removed.

### 12.2 Shell and navigation (implemented)

- Three task groups in the rail: **Operations** (Overview, Work queue, Quality & approvals, Agents,
  Artifacts & releases), **Analysis** (Production pipeline, Performance, Improvement) and
  **Administration** (Settings & audit, Documentation).
- Every route keeps its URL. Active links carry `aria-current="page"` and detail routes keep their
  parent section active.
- At ≤1024px the rail becomes an off-canvas drawer: a Menu button with `aria-expanded` /
  `aria-controls`, a Close button, a backdrop, Escape to dismiss, a Tab trap, focus entering the
  drawer on open and returning to the Menu button on close. Body scroll is locked while open. The
  closed drawer is `visibility: hidden`, so its links are not in the tab order.
- The topbar shows breadcrumbs with the exact work ID, instance identity/health and the theme
  control. The theme control names the theme it will switch to.
- The sidebar footer shows the event-stream freshness ("Live activity · last event …"), the
  instance identity and adapter health, the account and sign out.
- Per-page Refresh buttons remain in this increment; consolidating them into one shell refresh with
  a last-successful-update timestamp is deferred (D-07) until the shared `FreshnessIndicator`
  component exists.

### 12.3 Queue toolbar (implemented)

- Two rows on desktop: search + state/attention/sort filters + Clear filters; then the view switch
  and saved views.
- At ≤760px the search field stays full width and the filters collapse behind a Filters button with
  `aria-expanded` and `aria-controls`; no squeezed select controls.
- Clear filters is visible, disabled with a stated reason when nothing is active, and clears search,
  state and attention (sort is not a filter).
- At ≤860px the table is replaced by purpose-built cards: full ID, title, authoritative state,
  derived attention, priority, event count/updated time, last failure and one advisory next step.
  The card's next step is derived from the same authoritative flags; it is not a second action
  surface.
- The four KPI tiles on the queue were replaced by a labelled summary line; the definitions are
  available in a disclosure. Saved-view rename/delete and URL persistence remain open (B-02).

### 12.4 Responsive containment (implemented)

- Every grid/flex container that holds a scrollable table now has `min-width: 0`, so table wrapping
  and inner scrolling work instead of widening the document.
- Long identifiers and digests wrap at safe boundaries (`overflow-wrap: anywhere`) instead of
  forcing horizontal page scroll.
- `docs/reference/` holds the three PR 0 mockups and `verification.json` (1440×1000 and 390×844,
  both themes, no console errors or document overflow).

### 12.5 Measured PR 2 gate

Headless Chrome against the rebuilt release server (port 3101), 16 routes: `overview`,
`work queue`, `work detail` (merge and contract items), `work 404`, `quality`, `releases`,
`release detail`, `pipeline`, `agents`, `agent detail`, `analytics`, `improvement`, `settings`,
`docs` and `route 404`.

| Gate | Result |
|---|---|
| Widths × themes × routes | 7 × 2 × 16 = **224 measurements** |
| Document horizontal overflow at 320/390/768/1024/1280/1440/1920, both themes | **0** |
| Visible text below 12px | **0** (body 15px; metadata 13px) |
| Unexpected console errors | **0** (11 logs are the deliberate 404 fixture routes) |
| Page errors | **0** |
| Drawer (390px) | opens with focus inside, Tab trap wraps, Escape closes and restores focus, body scroll locked, `aria-expanded` correct |
| Desktop rail | Tab from the last sidebar control leaves the sidebar; the drawer trap does not run when the rail is permanent |
| Queue filters (390px) | fields hidden until Filters, `aria-expanded` correct, Clear filters enables only with an active filter and resets it |
| Work tabs | ArrowRight selects the next tab and moves focus |
| Contract dialog | focus enters, labelled/described, Escape closes and restores focus; **background inert = false** (open gap: portal) |
| After screenshots | 37 files in `.evidence/pr2/shots/`; JSON `.evidence/pr2/pr2-gates.json` |

### 12.6 Still open after this increment

- `DataTable`, `Tabs` as a standalone primitive, `FilterBar`, `EmptyState` actions,
  `FreshnessIndicator`, `EvidenceSummary`, `DiffViewer` are not yet shared components (C-01 partial).
  **Resolved in §13.2** for all of these; `InlineValidation`, `Confirmation` and `EvidenceSummary`
  are built and tested but not yet used by Intake.
- Modal background is not inert; the dialog still renders inside `.app-frame`. A portal is required
  before the background can be inerted safely (C-02). **Resolved in §13.1.**
- Settings is still one long page; the 390px document overflow is addressed by containment, but the
  section subnavigation and the audit-table layout are PR 5 work (B-04).
- Quality still approves template policies inline; the recorded decision is to move that to
  Settings → Approved templates (interaction-inventory D-04).
- The Board and Priority queue views still scroll horizontally at narrow widths without a stage
  selector or labelled scroll affordance (B-02). Saved views still lack rename/delete and URL state.
- Several surfaces still pass explicit uppercase strings into `Chip` (stage status, review verdict,
  approval kind, role, serving status), so the label reads in capitals even though the CSS no longer
  transforms it. This is C-04 copy/status governance, not a token defect. **Closed for Overview,
  the queue and Work detail in §13.3; Quality, Release, Pipeline and Settings remain.**
- Overview, Quality, Pipeline and Agents still use KPI/`MetricCard` grids; only the queue summary was
  replaced in this increment (D-06). **Overview was converted in §13.4; Quality, Pipeline and Agents
  remain open.**
- The mobile work-detail tab strip still scrolls horizontally with no menu/segmented alternative
  (B-05); the reference mockup replaces it with an evidence index. **Resolved in §13.2/§13.6:**
  at ≤860px the tablist is replaced by a labelled picker.
- No real screen-reader run, and no 200% zoom / text-spacing pass yet (VG-02, VG-03).

## 13. PR 3 high-frequency flows — implementation record

This section records the third increment: `C-02` is closed, `C-01` now has shared primitives in
use on Overview, the work queue and Work detail, and `C-04` is closed on those three surfaces.
Quality, Pipeline, Release, Agents and Settings are unchanged and stay open for PR 4/PR 5.

### 13.1 Dialog portal and inert background (C-02 closed)

- The dialog is moved to a document-level portal on mount: the `.dialog-backdrop` becomes a direct
  child of `<body>`. It can no longer be a descendant of `.app-frame`, so `inert` on the frame
  cannot swallow the dialog.
- While open: `.app-frame` carries `inert`, `<body>` carries `dialog-open` and `overflow: hidden`,
  the dialog body scrolls (`overscroll-behavior: contain`), focus moves to the first control inside,
  Tab/Shift+Tab stay inside, Escape closes only when the caller allows it, and the opener receives
  focus back after the lock is lifted.
- Overlapping open/close transitions use a counter: the frame is only made interactive again when
  the last dialog has gone.
- The module comment that claimed the frame was inert before this existed has been replaced by the
  accurate description of what the code does.
- At ≤520px the backdrop padding shrinks, the footer wraps and the dialog may use 94vh, so the
  decision dialog stays usable at 320px.

### 13.2 Shared primitives (C-01, implemented subset)

New modules under `web/src/components/`:

| Module | Primitives |
|---|---|
| `status.rs` | `StatusIndicator`, `HealthState`, `EntityIdentity`, `MetadataList`/`MetadataRow`, `FreshnessIndicator` |
| `table.rs` | `DataTable`, `Column`, `Sort`, `SortDirection`, `TableState`, `aria_sort` |
| `tabs.rs` | `Tabs`, `TabItem`, `TabPanel`, `roving_target` |
| `feedback.rs` | `EmptyState`, `ErrorBoundary`, `InlineValidation`, `ValidationMessage`, `FieldError`, `Confirmation`, `TypedConfirmation` |
| `layout.rs` | `PageHeader`, `Section`, `ActionToolbar`, `FilterBar` |
| `evidence.rs` | `EvidenceSummary`, `EvidenceCheck`, `DiffViewer` |

Properties:

- `DataTable` renders a visible `<caption>`, `scope="col"` headers, `aria-sort` only for the column
  the current ordering actually uses, a labelled `role="region"` scroll container with
  `tabindex="0"` and a visible hint, and explicit populated/empty/loading/error states.
- `Tabs` keeps one panel, `aria-controls`, `aria-selected`, roving tabindex and Arrow/Home/End; at
  ≤860px the tablist is replaced by a labelled `<select>` that exposes the same selection, so the
  last tab is never clipped.
- `Confirmation` binds the assertion to the exact digest; the typed value must match after trimming
  and the server compares again.
- `FreshnessIndicator` derives live/reconnecting/stale/disconnected from `labels::freshness_view` and
  always shows the last successful update.
- `DiffViewer` adds the legend, a per-file copy-path control and a focusable, keyboard-scrollable
  content region.
- Pure logic is unit-tested: tone whitelist, `aria_sort`, `roving_target`, validation summary
  pluralisation, confirmation satisfaction, typed-digest comparison, queue sort definitions.

Usage in this increment: Overview (all sections), queue table and filter bar, Work detail tabs,
status line and diff. `InlineValidation`, `Confirmation`, `ErrorBoundary` and `EvidenceSummary` are
implemented and tested but not yet wired into Intake/Work detail forms — that is item 6 of the
increment, still open.

### 13.3 One status dictionary (C-04 closed on converted surfaces)

- `labels.rs` gained `outcome_label`, `severity_label`, `health_label`/`health_tone` (moved out of
  Work detail), `stage_state_label`, `serving_status_explanation` and `Freshness`/`freshness_view`,
  with tests that keep unknown codes unchanged.
- Overview, the queue and Work detail no longer call `.to_uppercase()` on any status and no longer
  render a status as a `Chip`: 0 `.chip` elements on all three routes.
- Status text is now a sentence-case word (`Ready for delivery`, `Waiting for merge approval`,
  `Not currently serving`) with the raw code in the `title` attribute.
- Serving status on Work detail now comes from `serving_view`, so the stage, header, panel and
  release list cannot disagree.

### 13.4 Overview conversion

- The six KPI tiles are gone (0 `.metric-grid`, 0 `.metric`). One compact health strip carries
  factory health, work items, durable events, last event, dispatch state and the projection clock.
- Sections: "Decisions waiting" (one row per work item + action, facts before the link), "Where the
  work is" (authoritative state counts with the definition sentence), "Serving now" (a reconciled
  serving table on desktop, a stacked card per release at ≤860px so the explanation is never clipped
  by a horizontally scrolled table), "Recent durable activity".
- Aside: pending authorizations as a count summary that links to the audit inbox instead of
  repeating the same decision, dispatch, execution capacity (deterministic demo runner stated
  explicitly, tokens not shown as zero), recent releases and freshness.
- No metric is fabricated; every count is either the adapter's or computed from the listed rows and
  labelled as such.

### 13.5 Queue table conversion (partial B-02)

- The ad-hoc table is now a `DataTable`: full work ID with copy, title link, authoritative state,
  derived attention (or "None recorded"), declared priority, next actionable step, updated, event
  count and last failure.
- The caption states the current ordering in words; header clicks reuse the adapter's own orderings
  (`title`, `state`, `updated-desc`) and `aria-sort` follows the ordering actually shown. A creation
  ordering deliberately claims no column.
- The toolbar is a `FilterBar`: always-visible search, labelled state/attention/sort fields, a named
  Filters toggle below 760px, Clear filters disabled with a stated reason when nothing is active.
- Kanban widths, priority definitions, saved-view rename/delete and URL state are still open (B-02).

### 13.6 Measured PR 3 gate

Headless Chrome against the rebuilt release server (port 3101, `demo-runtime-pr3`), authenticated
fixture, both themes. Raw JSON: `.evidence/pr3/pr3-verify.json`, `.evidence/pr3/pr3-verify-light.json`.

| Gate | Result |
|---|---|
| Overview: KPI grids / metric cards | 0 / 0 |
| Overview: sections and aside blocks | 4 sections, 5 aside blocks, 1 health strip, 2 deduplicated attention rows |
| Overview: status chips | 0 (8 `StatusIndicator`s) |
| Upper-case status tokens in visible text | 0 |
| Queue: caption, scoped headers, sortable headers | caption with ordering in words; 3 `aria-sort` headers; State click → `ascending`, Updated → `none` |
| Queue: chips / copy controls / filter fields | 0 / 2 / 3 |
| Queue at 390px: mobile cards, document overflow | 2 cards, 0 |
| Work detail: chips, status line | 0 chips; "Ready for delivery", "Waiting for merge approval", "Waiting for authorization" |
| Tabs: roving tabindex, Arrow/End/Home, 390px picker | 6 tabs with one `tabindex="0"`; ArrowRight/End/Home move selection and focus; picker visible at 390 with all 6 options and switching works |
| Dialog: portal, inert, scroll lock, focus | backdrop is a `<body>` child; `frame.inert === true`; `body.dialog-open`, `overflow: hidden`; focus inside; body scrollable |
| Dialog: focus containment | background control cannot take focus; Tab from last wraps to Close; Shift+Tab from first wraps to Cancel |
| Dialog: 390px and 320px | 378×793 within 390×844 with footer visible; 308px wide at 320 with footer visible; 0 document overflow |
| Dialog: Escape | closes, removes `inert`, removes `dialog-open`, restores focus to "Authorize merge" |
| 320px document overflow, 3 converted routes | 0 |
| 320px overview usability | serving state reads as a stacked card; attention facts stack label over value; section headings keep their whole sentence; stage counters wrap 2 per row |
| Visible text below 12px, 3 converted routes | 0 |
| Page errors / unexpected console errors | 0 / 0 |
| Screenshots | 15 files in `.evidence/pr3/shots/` (1440/390/320, both themes, dialog open/closed) |

### 13.7 Still open after this increment

- Item 5 (full queue redesign) is **partial**: Kanban fixed widths and a visible scroll affordance,
  the priority definition panel, saved-view rename/delete and URL-restored filter state are not
  implemented.
- Item 6 (Work detail and Intake conversion) is **not started**: Work detail still shows duplicate
  merge/contract entry points, the contract panel's scenario table still compresses at 320px, Intake
  has no error summary or parser-code mapping, and drafts are not preserved across navigation.
- Intake, the merge/contract dialogs and the release dialog do not yet use `Confirmation`,
  `InlineValidation` or `EvidenceSummary`; the primitives exist and are tested but unwired.
- Settings (B-04), Pipeline (B-03), the Quality review-matrix redesign and the template-policy move
  to Settings (D-04) remain open.
- No real screen-reader run and no 200% zoom/text-spacing pass yet (VG-03), so the plan is not
  complete and no gate below VG-07 is claimed.
## 14. PR 3 completion — queue, work detail, intake and the first end-to-end journey

This section records the increment after §13: the rest of B-02, item 6 on Work detail and
Intake, and the first real end-to-end factory journey (item 7). Nothing here is a stylesheet
pass: every item below changed behaviour and was measured in the running application. `C-01`
is still not complete because `Timeline` is not built; the validation, confirmation and evidence
primitives are now wired into real forms and dialogs.

### 14.1 Queue B-02 closed

- **URL is the addressable state.** `view`, `q`, `state`, `attention` and `sort` are parsed from
  the query before the controls render, serialised back with `navigate(..., replace: true)`, and
  mirrored back into the controls on Back/Forward. Unknown states, attention codes and orderings
  are dropped instead of being forwarded to the adapter.
- **Saved views** support save, rename, delete and an active indicator that compares every
  filter, not only the name; a modified view says “has unsaved changes”. Named views remain in
  `localStorage`; the URL carries the filters. `Clear filters` sits in the always-visible toolbar
  row, so it is still reachable when the filter fields are collapsed at 390px.
- **Board**: seven fixed 280px columns inside a labelled, focusable scroll region with
  Earlier/Later arrows, a visible scrollbar and a hint. Empty columns are 83px tall versus 209px
  for populated columns, so an empty stage is not a blank well. At 320px the region scrolls
  internally with 0 document overflow.
- **Priority**: the definition is displayed next to the list. Order is actionable urgency from
  authoritative flags (decision waiting → stopped → executing → quiet), then declared planning
  priority, then update time. The view states that it is computed in the browser and that the
  Table is where the adapter orderings live. Table and cards now render `Normal`/`Normal priority`
  instead of the raw planning value.
- Measured: `.evidence/pr3/queue-verify.json`; screenshots
  `.evidence/pr3/shots/queue-{board,priority}-{1440,320}-dark.png`, `queue-390-{dark,light}.png`,
  `queue-1440-{dark,light}.png`.

### 14.2 Work detail item 6

- **One entry point per decision.** Approve contract, reject contract and authorize merge live
  once, in the header action row. The contract panel keeps the evidence and links to that row;
  the old “Merge authorization” panel is gone. `What the factory allows next` is explicitly the
  factory's informational transition list, not a second action surface.
- **Serve is owned by the release page.** Work detail no longer opens a serve dialog or calls
  start/stop. It reports the serving state and deep-links to the release page, which owns the
  mutation and its evidence; only a proven, live window shows a preview link.
- **One attention line** replaces the attention cards: count, severity and titles, with a link to
  the action row when a decision is waiting.
- **Evidence hierarchy.** The Evidence tab starts with `EvidenceSummary` (verdict → checks →
  exact identity → raw evidence). The merge dialog renders the same summary, the exact review
  digest with copy, and `Confirmation` with the typed digest. The contract and reject dialogs use
  `EvidenceSummary` for the declared scenarios plus `Confirmation`. Server-side digest
  revalidation and failure retention are unchanged.
- **Contract scenarios** are a definition list in the panel and in the dialog. At 320px the
  lifecycle scenario is 270px wide and the dialog scenario item 290px; paths wrap in two lines
  instead of a character-per-column table, with no horizontal scroll inside the dialog.
- Measured: `.evidence/pr3/work-detail-verify.json`; screenshots
  `after-work-detail-1440-dark.png`, `after-work-evidence-1440-dark.png`,
  `after-merge-dialog-{1440,320}-dark.png`, `after-contract-dialog-320-dark.png`,
  `after-work-detail-320-{dark,light}.png`.

### 14.3 Intake item 6

- Every field has a persistent `label for`, a required marker, an id, and (when it has a problem)
  a `ValidationMessage` referenced by `aria-describedby` and `aria-invalid`.
- One `InlineValidation` summary links to the field for every local or parser problem. Local and
  parser errors about the same field are deduplicated, so the count is problems, not repetitions.
  Parser codes are mapped to human messages with field context; the raw codes stay in
  `Technical details`. The summary receives focus after a rejected parse or a blocked submit.
- **Drafts** are stored in this browser as the same typed `RequirementDraft` the submit path
  builds. Reload and navigation restore them; a privacy note states the storage and scope, and
  `Clear draft` removes both fields and the stored copy. A successful submit removes the draft.
- The **exact generated document** is shown with its factory digest and bound to submission by
  `Confirmation`. Editing after validation marks the parser verdict stale, hides its errors as
  non-current, and disables submission until the new bytes are validated.
- Measured: `.evidence/pr3/intake-verify.json`; screenshots
  `after-intake-1440-dark.png`, `after-intake-320-{dark,light}.png`. The 1440 form uses the full
  width; at 320 the panel header stacks and the banner wraps, with 0 document overflow.

### 14.4 Serve truth and the served URL

- The adapter's live `Serving` projection set `active=true` for the whole serve window but wrote
  the served URL only when the window ended, so no preview link could be shown while the process
  was alive.
- The control-plane server now enriches both work detail and release detail from the durable
  `factory.release.observed.v1` event, but only when that event belongs to the current window
  (`event.timeUtc >= serving.recordedAtUtc`). A `ReleaseObserved` record from an earlier window is
  never reused. Unit-tested in `server/src/api.rs`.
- The end-to-end run proved the live URL responded `HTTP 200` with
  `Wip.Factory deterministic demo target` while the job was `running`, and both the release page
  and work detail offered the same preview link.

### 14.5 Item 7 end-to-end journey (measured)

One real work item, `WORK-b3a440a4`, was created from the intake UI with keyboard activation, and
taken through every gate by the rendered controls. Requirement digest
`sha256:b12276ab…83e4ab`; review digest `sha256:f3686758…5bb070`; merged revision
`c8a5ed98b7b3…`. Evidence: `.evidence/pr3/e2e-verify.json`, timeline in the same file.

| Step | Measured result |
|---|---|
| Intake | parser accepted; generated digest matched the created work item (`digestMatchesPreview: true`); submit was enabled only after the Confirmation assertion and keyboard Enter |
| Dispatch before contract | state `Proposed`; the factory paused with `awaitingContractApproval: true` |
| Contract approval | dialog showed `EvidenceSummary` “Proposal ready for a freezing decision”, 1 scenario, full requirement text; keyboard Space + Enter froze it; `contractFrozen: true` |
| Independent review | one dispatch reached `ReadyForDelivery`; accepted review with 4 passed checks and digest `sha256:f3686758…` |
| Merge authorization | dialog showed 4 checks, 5 identity rows, exact digest; keyboard checkbox + typed digest + Enter; `confirmDisabled` went true → false only when the digest matched |
| Merge execution | one governed dispatch cycle claimed the recorded authorization; state `Released`, `release.verified: true`, scenario `AC-1=pass`, `releaseRecorded: true` |
| Supervised serve | start through the release dialog with a 300s window; state `running`, URL `http://127.0.0.1:64808/`, process `115144`; HTTP probe `200`, body “Wip.Factory deterministic demo target”; release header “Serving” with “Open verified preview”; work detail “Serving” + same preview link and no serve button |
| Stop serve | keyboard through the release dialog; `active: false`, status `stopped` |
| Final truth | state `OperationallyValidated`; requirement binding true; merge digest bound to the review digest; release verified; serving stopped; independent acceptance not claimed |
| Errors | 0 page errors, 0 console errors |

Responsive measurement on the created work detail and release at 320/390/768/1024/1280/1440/1920:
0 document overflow, every action button within the viewport, 6 tabs at every width. Keyboard
checks: ArrowRight changed the selected tab to “Agent activity” with focus; queue search typed by
keyboard updated the URL and rows; Enter on Clear filters removed the query; the 390px drawer
opened with Enter, closed with Escape and returned focus to `nav-toggle`.

Screenshots: `e2e-intake-validated-1440-dark.png`, `e2e-submitted-1440-dark.png`,
`e2e-before-contract-dialog-1440-dark.png`, `e2e-contract-approved-1440-dark.png`,
`e2e-before-merge-1440-dark.png`, `e2e-released-1440-dark.png`,
`e2e-release-before-serve-1440-dark.png`, `e2e-serving-active-release-1440-dark.png`,
`e2e-work-detail-serving-1440-dark.png`, `e2e-serving-stopped-release-1440-dark.png`,
`e2e-work-detail-{320,390,1440,1920}-dark.png`, `e2e-release-{320,390,1440,1920}-dark.png`,
`e2e-work-detail-{1440,390}-light.png`, `e2e-release-1440-light.png`.

### 14.6 Verification gates — measured

**VG-01 responsive.** Overview, queue, work detail and release detail were measured at 320, 390,
768, 1024, 1280, 1440 and 1920 CSS px: 0 document overflow, 0 clipped actions, 0 visible text below
12px (`.evidence/pr3/vg-checks.json`). Merge, contract and serve dialogs were opened and closed at
the same seven widths, plus both themes at 390 and 1440: 25 cases, 0 failures (`dialogs-check.json`);
every dialog stayed inside the viewport, had no horizontal scroll, kept its footer reachable and
kept focus inside. WCAG text spacing (1.5 line height, 0.12em letter spacing, 0.16em word spacing,
2em paragraph spacing) at 320 and 1440: 0 overflow. A physical browser zoom is not reachable from
page-level automation, so 200% zoom was tested as its layout equivalent, a 720 CSS-px viewport at
1440 device pixels: 0 overflow and no clipped controls. This is recorded as a limitation, not as a
physical zoom run.

**VG-02 readability and contrast.** The documented tokens still measure 15px body, 13px dense
metadata, 12px floor; the width sweep found 0 user-facing text below 12px. Text spacing is measured
above. axe-core 4.11 ran 17 audits (overview, queue, work detail, release, intake, the three
decision dialogs and login, in both themes) under the WCAG 2.0/2.1 A/AA rule tags: 0 violations
(`axe-results.json`). The audit found and this increment fixed real defects: light-theme
`--text-tertiary`, `--accent` and `--success` were below 4.5:1 on the light canvases; neutral chips
used 11px uppercase tertiary text; the table Copy control was rendered at 75% opacity; and the
contract dialog used a `<div class="metadata-list">` around `dt`/`dd` instead of a `dl`. All are
corrected and the rerun is clean. axe is automated, so this is not a substitute for a manual
contrast review; VG-02 is met for the automated AA rule set.

**VG-03 keyboard and screen reader.** The real NVDA pass used NVDA 2026.3.1 (portable, debug log,
`--minimal --disable-addons`) with headed Chrome foregrounded:

- Login page, Factory overview, Work queue, the queue table (caption, 8 columns, rows including
  state and attention), all four filter fields, the view switch and the table scroll region.
- Navigation drawer at 390: "Open navigation" / "Close navigation", opened with Enter, closed with
  Escape, focus returned to the toggle.
- Work detail: the heading is announced on route entry (after the shell fix below), the tablist and
  tab selection are announced, and the Evidence tab announces "Latest review verdict", "Review
  accepted", the four checks and "Exact identity".
- Merge dialog: NVDA reads the dialog title, description, "Verdict Review accepted", the four
  checks and their details. Contract dialog: title, description and "This proposal cannot be
  approved." Serve dialog: title, description and the contract/URL rule. Intake: the summary is
  announced as "3 problems need attention" with the three field links and the field alerts.
- 53 of 55 scripted expectations matched (`vg03-nvda.json`). The two misses are script attribution,
  not unannounced controls: the search edit's typed value is not echoed in this NVDA speech
  configuration (the edit itself is announced), and the work-detail state line is not reached by
  the scripted arrow sequence because route focus leaves NVDA in focus mode; the same
  "Operationally validated" state is announced in the queue table row. This is recorded as a
  detail-page reading gap, not hidden.
- Keyboard-only mutations (contract freeze, merge authorization, serve start/stop) were executed in
  the item 7 E2E; the dialogs that carry them were additionally read under NVDA in this pass.

**VG-04 safety/truth.** Met. The E2E proved requirement-digest binding, review-digest binding to
the merge dialog, post-merge verification on the merged revision, one merge decision per fixture,
and a serving state that agreed between work detail and release detail. The stale path was then
exercised on `WORK-6075dcca`, whose reviewed target had moved: submitting the open dialog's digest
was refused with `approval-target-moved`, the dialog stayed open with its evidence, checked
confirmation and exact typed digest, the refusal banner showed the code once, and no event or state
changed. Wrong-digest and repeat-authorization refusals were also measured
(`stale-submit-only.cjs`, `stale-merge.json`).

**VG-05 visual evidence.** A reproducible harness now captures queue, work detail and release at
320, 768 and 1440 in both themes with a fixed clock and reduced motion: **18 views** were captured
twice and pixel-diffed, with **0 changed files and 0 changed pixels** (`visual-diff.json`,
`.evidence/pr3/visual/baseline`). The overview is excluded because its 105-item projection takes
58s and is itself an open VG-08 limit. The baseline was generated by this harness, so independent
design approval is still required; VG-05 is not claimed complete.

**VG-06 interaction states.** Measured in `.evidence/pr3/vg06-states.json`: delayed projection shows
the loading state (`role=status`, `aria-live=polite`); an empty result shows "No work matches these
filters" with a Clear filters action; an aborted work-items request shows the network error and a
"Reload this page" action, and the reload returns the rows; offline shows "Offline · showing the
last durable facts" within 0.8s, keeps the durable rows, and returns to live activity when the
browser reconnects; a nonexistent work item shows 404 `work-item-not-found` with no retry action and
the exact ID in the breadcrumb; a viewer role sees disabled merge and dispatch controls and receives
403 `forbidden` from the merge endpoint. Non-executable contract and stale intake drafts are covered
above. Detail-tab states were also measured: a proposed item shows "No implementation attempt yet" and "No review verdict yet"; the release page for a merge-authorized, unmerged item resolves to Not verified / Not serving with "No post-merge verification"; an invalid ID shows the non-retryable 404 without a retry button (`vg06-detail-states.json`). A failed background refresh now keeps the last good projection on the queue, work detail, release detail and overview and shows "The last refresh failed" with a Retry refresh action; the banner clears after a successful retry (`refresh-banner.json`, `release-refresh.json`, `overview-refresh.json`). The overview had to finish its 58s initial projection before the failure could be exercised; it retained its 11.7k-character content and never entered the error state. Initial projection failures were also measured on overview and release: both show the typed network error and the safe Reload this page action (`initial-errors.json`). A work item with no accepted review shows the release empty state "No release yet" with its explanation, not a loading shell or an error (`release-empty.json`). The empty-factory overview is not available in this single-instance fixture, so VG-06 is not claimed complete.

**VG-07 technical.** `cargo test --workspace` suites were green; the web crate has 24 tests and the
server 13. The hydrate wasm check and release server/wasm builds succeeded, and the browser checks
ran on the authenticated demo fixture. No .NET source changed in this increment, so the adapter
suite/build was not re-run.

**VG-08 large data.** 100 real work items were submitted through the control-plane API (API p50
1.39s, p95 2.59s, max 2.71s; 100/100 accepted). The queue then projected 104 items and 144 events
in 1.33s, rendered 104 rows at 1440 with 0 overflow (104 mobile cards at 390, 0 overflow). This
meets the >=100 work-item leg but not the >=1000-event leg, and does not cover long diffs, many
sessions or dozens of approvals, so VG-08 is partial. The large fixture also exposed a real
limitation: the overview projection took **32.3s** (200, 73 KB) after bounding the adapter
approvals read at 8s; the adapter `/v1/approvals` endpoint alone exceeded 25s and the demo adapter
serialises the remaining detail reads. The server now parallelises the independent reads and bounds
approvals, but the overview is not usable at this scale: the browser load was **58s** (SSR projection plus the client refetch). This remains open.

**VG-09 design coherence.** No human visual review was conducted in this increment; VG-09 remains
open.

### 14.6.1 Two gaps found by this round and fixed

- **Route-change focus (J1).** NVDA stayed on the old control after navigation. The shell now
  focuses the new route's `h1` after the data-driven heading renders, keeps retrying briefly while
  a reactive refresh can replace the node, and stops the moment the operator focuses a control or a
  dialog is open. Verified on direct loads of queue/work/release and on SPA queue→detail.
- **Timeline semantics (C-01).** Timelines were `div` grids. They are now a shared ordered-list
  primitive: `<ol aria-label>` with `<li class="event">` entries, a time column whose `title`
  carries the exact UTC instant, and a `compact` variant for the overview. Work detail's
  implementation attempts and event log were measured at 1440 and 320: 1 and 15 entries, computed
  `list-style: none`, zero margin/padding, 0 horizontal overflow; axe remains clean across all 17
  runs (`timeline-check.json`). Chrome's accessibility tree exposes them as named `list` nodes
  with 1 and 15 `listitem` children (`timeline-ax.json`).

- **Refresh failures.** A background refetch that fails used to replace a full queue or work
  item with the error screen. Both now keep the last successful projection and show the typed
  failure with a Retry refresh action; the banner clears when the retry succeeds. Verified with
  aborted work-items, work-item and release requests (`refresh-banner.json`,
  `release-refresh.json`). The overview refresh path is still unverified.

- **Background refresh retention.** The converted surfaces used `Suspense`, so an SSE-driven
  refresh replaced a fully rendered work item with its loading block and lost the operator's
  context. Overview, queue, work detail and release now use `Transition`: the first load still
  shows the loading state, but a background refetch keeps the previous content on screen. Verified
  on the merge-authorized fixture (notice, buttons and state stayed present after the stream
  backlog) and the loading/empty/error/offline state check still passes.- **Contrast (VG-02).** axe found light-theme tertiary/accent/success tokens below 4.5:1 on light
  surfaces, 11px uppercase neutral chips, a 75% opacity Copy control in the table, and an invalid
  `div`/`dt`/`dd` structure in the contract dialog. Tokens were darkened, chips are 12px sentence
  case with primary text, the Copy control is fully opaque, and the dialog uses a real `dl`. The
  full axe rerun is clean in both themes.- **Offline truth (IA-03/VG-06).** The activity stream could remain "live" while the browser was
  offline. The shell now listens to the browser's offline/online events, sets an explicit offline
  state, closes the stream so buffered heartbeats cannot claim live, and reopens it on reconnect.
  Transient read failures gained a safe "Reload this page" retry.
### 14.7 Status and remaining gaps

- **C-01 primitives — closed.** All named primitives now exist. `Timeline` is built and wired as
  an ordered `ol` with a required accessible name (fallback "Timeline"), `li.event` rows with a
  time column and exact UTC title, and a `compact` variant; unit tests cover the label fallback.
  It formats work-detail attempts and events, overview durable activity, agent sessions and
  adaptations, and improvement events. `InlineValidation`, `Confirmation`, `EvidenceSummary` and
  `ValidationMessage` are live in intake and the decision dialogs. `ErrorBoundary` and
  `ActionToolbar` remain available but are not used on every route.
- **C-03 document titles — closed.** Measured in `titles-check.json`: "Factory overview · Wip.Factory
  Control Plane", "Work queue · Wip.Factory Control Plane", "{work title} · WORK-… · Wip.Factory
  Control Plane", and "Release · {work title} · WORK-… · Wip.Factory Control Plane". The title is
  set from the rendered `h1` plus the record identity in the URL; a bounded retry covers
  Suspense-backed routes.
- **Merge authorization execution — closed.** A real fixture (`WORK-37a2601c`) was left at
  `ReadyForDelivery` with a recorded `factory.merge.approved.v1`: the header no longer offers
  `Authorize merge`, `Start dispatch` is primary, a notice says the factory executes the merge on
  the next dispatch cycle, the transition list no longer repeats the authorization, and the queue
  next step is "Run dispatch to merge". A wrong digest is refused with `review-digest-mismatch`;
  repeating the recorded authorization is refused with `approval-already-applied`; neither changes
  the release record. Evidence: `merge-state.json`, `merge-queue-check.cjs`, `stale-merge.json`.
- **Release detail (PR 4) — converted.** The header state, verification and serving labels
  now use `StatusIndicator`; the verification outcome uses `outcome_label`; and start/stop serve
  uses the shared `Confirmation` assertion, with the footer disabled until it is checked. Measured
  on `WORK-b3a440a4`: 0 chips on the page, the three status labels render in sentence case, the
  serve dialog exposes the assertion, is disabled before the check and enabled after it, and
  Escape closes it (`release-serve-check.json`). The releases list now uses the same
  `StatusIndicator` contract: 0 chips, “Operationally validated / Release verified / Stopped”
  and “Ready for delivery / Not verified / Not serving” in sentence case, 4 rows, 0 overflow at
  320 and 1440, axe clean in both themes (`releases-check.json`). Quality (review matrix), Pipeline, Agent activity
  and Settings/audit are converted. B-04 is closed: section navigation, audit scroll cues and
  path-aware wrapping are measured; the redundant release in-page back link is removed.
- **Agent activity — converted.** The Agents list and agent detail replace their KPI cards
  with labelled summaries and definitions; definition IDs are mono identities rather than
  uppercase chips; active-profile, attempt and session outcomes use `StatusIndicator`; both
  pages use `Transition`. Measured: 9 role cards, 0 chips, “Registered roles 9 / Recorded
  sessions 0 / Implementation attempts 4 / Tokens Not measured”, 0 overflow at 320 and 1440,
  axe clean in both themes; the detail shows “No active profile” and its six-count summary
  (`agents-check.json`). That role had 0 sessions/attempts, so its attempt/session
  `StatusIndicator` paths were not rendered; the list totals still show 4 attempts across roles.
- **Pipeline — converted.** The four KPI cards are replaced by a labelled summary with stated
  definitions; stage counts are plain numbers, work-item states use `StatusIndicator`, the page uses
  `Transition`, and the horizontally scrollable board is a named, keyboard-focusable region.
  Measured: 9 stages, 0 chips, “Work items shown 106 / Queue depth 102 / In flight 0 / Needs
  attention 0”, 0 overflow at 320 and 1440, axe clean in both themes (`pipeline-check.json`).
  B-03 composition: desktop columns are 280px with a “9 stages · scroll horizontally…” and
  keyboard cue; at ≤760px the compact overview is hidden and the board becomes a single-column
  stage list (304px), with 0 document overflow and a clean axe rerun (`pipeline-b03.json`).
- **Integrated docs — polished.** `/docs` follows `cp_theme` (dark default, light when stored), has a
  real title (“Documentation · Wip.Factory Control Plane”) and a “← Back to the control plane”
  link to `/`. Measured at 1440 and 320: 0 overflow, axe clean in both themes
  (`docs-check.json`). The page remains session-protected like the rest of the app.
- **Auth — polished.** An API 401 now redirects to `/login?next=…`; signing in returns to the
  original page (measured: queue → login with `/i/local/work` → login → the same 106-row queue).
  The login page has labelled username/password fields, CSRF, `role=alert` errors, focus returns
  to the username field after a bad password, and 0 overflow at 320 and 1440
  (`session-expiry.json`, `login-check.json`).
- **Settings/audit — converted.** Instance health, audit results and template states use
  `StatusIndicator`; audit tables are named, keyboard-focusable scroll regions with a mobile
  “Scroll sideways…” hint; role selects have accessible names; all five `Suspense` boundaries are
  `Transition`. B-04: an “On this page” nav links six anchors (Factory, Control-plane audit, Adapter
  audit, Operators, Password, Templates); control root, target repository and adapter URL wrap only
  at path separators (`<wbr>`). Measured: 9 panels, 0 chips, 214 rows, 6 links/anchors, 0 overflow
  at 320/1440, axe clean both themes (`settings-check.json`, `settings-b04.json`,
  `paths-release.json`).
- **Quality — partially converted.** The review matrix now uses `StatusIndicator` (verdict
  "Accepted"; checks "Passed" / "Failed" / "Not measured"), has 0 chips, 4 rows, 0 overflow at 320
  and 1440, axe clean in both themes, and `Transition` keeps the table on screen during a refresh
  (`quality-axe.json`). The approval-card kind label and template `Confirmation` path are not
  browser-verified because the approvals inbox is empty in this fixture (the adapter
  `/v1/approvals` exceeds 25s). Improvement is converted (Transition, 0 chips, empty
  profile/adaptation states verified; its status paths were unrendered). Analytics uses its
  measured panels and definitions with `Transition`; Settings/audit is converted; auth now
  redirects expired sessions to sign-in with a return path. D-04 is closed: Quality links to
  `Settings#approved-templates`; B-03/B-04 and the release in-page back link are closed.
- **Remaining VG work:** VG-02 is met by the automated AA audit but has no manual contrast
  review; VG-05 has a 0-diff baseline of 18 fixed-clock views but no independent approval; VG-06 covers the four major surfaces' refresh
  failures but not every initial error/empty combination; VG-08 reached 104 work items but only
  144 events, no long diff/session/approval fixtures, and the overview takes 32s server-side /
  58s in the browser at that scale; VG-09 has no human visual review. A physical 200% browser
  zoom is still not run (the 720 CSS-px layout equivalent is the recorded substitute). The plan
  is not complete and must not be marked so.
### 14.8 Handoff — gate status, evidence and human decisions

This section is the single handoff for the branch. It records what is delivered, what is measured,
and what still requires a human or an adapter decision. It does not declare the plan complete.

**Delivered**

- Queue B-02, work detail item 6, intake item 6 and the item 7 E2E (real authorized fixture,
  keyboard paths, both themes, 320–1920, before/after screenshots and JSON).
- C-01 primitives, including the `Timeline` ordered list; C-03 per-route document titles with exact
  record identity.
- Truth fixes: offline/online transport states, retryable read failures, background-refresh
  retention for queue/work detail/release/overview, and decision-dialog failure retention.
- Merge authorization execution state: recorded authorization hides the action, dispatch becomes
  primary, the queue next step is "Run dispatch to merge".
- PR 4/5 conversions: release detail/list, Pipeline, Agent activity, and the Quality review matrix;
  `StatusIndicator` and the shared `Confirmation` replace the remaining chips/local serve dialog;
  Improvement, Analytics, Settings/audit, auth and integrated docs are converted as well;
  D-04, B-03 and B-04 are closed.

**Gate status**

| Gate | Status | Evidence |
|---|---|---|
| VG-01 responsive | Met: 7 widths × 4 routes, 25 dialog cases, both themes, text spacing, 0 overflow; 200% is the 720 CSS-px layout equivalent | `vg-checks.json`, `dialogs-check.json` |
| VG-02 readability | Met for automated AA: axe 17 runs, 0 violations; no manual contrast review | `axe-results.json` |
| VG-03 keyboard/AT | Met with notes: real NVDA 53/55; two scripted misses explained; physical Ctrl+= blocked by Windows input access | `vg03-nvda.json`, `timeline-ax.json` |
| VG-04 safety/truth | Met: digest binding, serving consistency, one decision per merge, wrong/repeat/stale-target refusals with failure retention | `stale-merge.json`, `stale-submit-only.cjs` |
| VG-05 visual | Baseline ready: 18 fixed-clock views, two captures, 0 changed pixels; independent approval pending | `visual-diff.json` |
| VG-06 states | Partial: four surfaces' initial/refresh/empty/offline/denied states measured; empty-factory overview unavailable | `vg06-states.json`, `vg06-detail-states.json`, `refresh-banner.json` |
| VG-07 technical | Met for this increment: 26 web + 13 server tests, wasm build/check, browser suites; .NET not re-run because no adapter source changed | `cargo-test-workspace.txt` |
| VG-08 large data | Partial: 104 work items/144 events, 0 overflow; no 1000-event/long-diff fixtures; overview 32s server-side / 58s browser | `vg08-large.json`, `overview-latency.json` |
| VG-09 design coherence | Open: no independent human review | — |

**Human decisions still required**

1. **VG-05:** approve the 18-view baseline or replace it; then the diff gate can fail a regression.
2. **VG-09:** conduct the independent review of Overview, Queue, Detail, Approvals, Agents,
   Analytics and Settings in both themes.
3. **VG-08:** decide whether to invest in the demo adapter's approvals/detail fan-out and a
   1,000-event/long-diff fixture. The adapter at 5100 was kept untouched; its `/v1/approvals`
   exceeds 25s and serialises detail reads.
4. **Quality approvals path:** provide an approvals fixture (template or pending approval) so the
   converted approval-card `StatusIndicator`/`Confirmation` path can be browser-verified.
5. **PR 5:** final empty/error polish remains; integrated docs and the named PR5 surfaces are
   converted.

**Deliberate deviations**

- Physical 200% browser zoom was not achievable: page-level keys do not reach browser chrome and
  the OS-level `SendKeys` attempt was denied. The 720 CSS-px layout at 1440 device pixels is the
  recorded substitute.
- The large-data fixture has 104 work items but 144 events; the overview was bounded at 8s for
  approvals and still takes 32.3s server-side.
- No .NET adapter source was changed and the 3100/5100 processes were not restarted.
