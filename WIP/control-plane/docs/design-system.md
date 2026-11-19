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
- Modal background is not inert; the dialog still renders inside `.app-frame`. A portal is required
  before the background can be inerted safely (C-02).
- Settings is still one long page; the 390px document overflow is addressed by containment, but the
  section subnavigation and the audit-table layout are PR 5 work (B-04).
- Quality still approves template policies inline; the recorded decision is to move that to
  Settings → Approved templates (interaction-inventory D-04).
- The Board and Priority queue views still scroll horizontally at narrow widths without a stage
  selector or labelled scroll affordance (B-02). Saved views still lack rename/delete and URL state.
- Several surfaces still pass explicit uppercase strings into `Chip` (stage status, review verdict,
  approval kind, role, serving status), so the label reads in capitals even though the CSS no longer
  transforms it. This is C-04 copy/status governance, not a token defect.
- Overview, Quality, Pipeline and Agents still use KPI/`MetricCard` grids; only the queue summary was
  replaced in this increment (D-06).
- The mobile work-detail tab strip still scrolls horizontally with no menu/segmented alternative
  (B-05); the reference mockup replaces it with an evidence index.
- No real screen-reader run, and no 200% zoom / text-spacing pass yet (VG-02, VG-03).