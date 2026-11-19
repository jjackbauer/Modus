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

| Pair | Current | Verdict | Target |
|---|---:|---|---:|
| `--text-faint` #5b6878 on `--bg-panel` #11161f (dark) | **3.19:1** | fails 4.5:1 | replace with #9aa8ba → **7.50:1** |
| `--text-faint` #5b6878 on `--bg-canvas` #090c11 (dark) | **3.45:1** | fails | #9aa8ba → **8.10:1** |
| `--text-tertiary` #79889b on `--bg-panel` (dark) | **5.01:1** | passes | keep |
| light `--text-faint` #8996a8 on #ffffff | **3.00:1** | fails | replace with #5f6b7a → **5.43:1** |
| light `--text-faint` #8996a8 on #f5f7fa | **2.80:1** | fails | #5f6b7a → **5.06:1** |

Rules:

- Normal text (below 18.66px bold / 24px regular) ≥ 4.5:1 in both themes.
- Large text and UI boundaries/focus indicators ≥ 3:1.
- Status colour is never the only signal: the label must read correctly in greyscale.
- Success, warning and danger are reserved for their semantics. Neutral counts and labels use
  `--text-secondary`, not a status colour.

## 3. Typography

Current tokens are 10/11/12/13px and a 13px body, which is below the operator bar. Target scale:

| Role | Size / line-height | Weight | Current token |
|---|---|---|---|
| Page title | 24px / 1.25 | 650 | `--text-xl` (22px) → raise to 24px |
| Section title | 18px / 1.3 | 620 | `--text-lg` (17px) |
| Record title | 16px / 1.35 | 600 | `--text-md` (14px) → 16px |
| Body / form text | 15px / 1.55 | 400 | `--text-base` (13px) → 15px |
| Operator metadata | 13px / 1.45 | 450 | `--text-sm` (12px) → 13px |
| Secondary annotation | 12px / 1.4 | 400 | `--text-xs` (11px) → 12px floor |
| Prohibited | 10px (`--text-2xs`) | — | remove for user-facing data |

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
