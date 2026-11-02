# Bones Strategy Pool View — Click-to-Focus Fix

> Fix the strategy pool dashboard at `/bones/pool/view` so clicking a strategy card navigates to or focuses a dedicated HTML detail view for that strategy. Address the CSS class name mismatch that currently makes all card clicks silently no-op, add a server-rendered HTML detail page at `/bones/pool/{strategyId}/view`, and fix the existing "View full detail →" link that incorrectly points to a JSON endpoint.

---

## Runtime Diagnostics

| Signal | Detail |
|--------|--------|
| Tests total | 271 |
| Tests passed | 260 |
| Tests failed | 11 |
| Pre-existing failures | 6 × `BehaviorProofComplianceGateTests` (stale requirement docs), 5 × DOM rendering tests (`BonesViewerBoardChainGeometryDomTests`, `BonesViewerBoardChainLayoutDomTests`, `BonesViewerDominoRenderingDomTests`) |
| Pool-specific tests | 11 in `BonesPoolApiEndpointTests` — all pass; no tests cover the `/bones/pool/view` HTML page or `pool.js` interactions |
| Missing test coverage | No integration test verifies that clicking a card in `/bones/pool/view` triggers any UI behavior; no test verifies `pool.js` `toggleCard` successfully expands a card |
| Build warnings | 0 |
| Critical failures found | CSS class name mismatch: `pool.js:16` queries `.pool-card-body` but server renders `.pool-card-detail` — `toggleCard` always returns early |
| Missing route | `/bones/pool/{strategyId}/view` (HTML) does not exist — only JSON endpoints are registered |
| Broken link | "View full detail →" in card markup links to `/bones/pool/{id}` which returns JSON, not HTML |

---

## Build/Test Gate

| Check | Status |
|-------|--------|
| dotnet build Wip.Bones.Web | ✅ passed |
| dotnet test Wip.Bones.Web.Tests | 260 passed, 11 failed |
| Pool API tests | ✅ all 11 pass |
| Pool page/JS tests | ❌ none exist |
| Pre-existing failures addressed in plan | 11 pre-existing failures are unrelated to this scope; no regression expected |

---

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- API tests are valid only when thorough integration gates are asserted.
- DOM/JS behavior must be verified via `WebApplicationFactory<Program>` integration tests that load the rendered HTML page and exercise click interactions.

### Completeness Checklist

- [ ] Fix CSS class name mismatch: rename `pool.js` `.pool-card-body` → `.pool-card-detail` so `toggleCard` finds the detail element [mandatory — runtime failure]
- [ ] Add server-rendered HTML detail page route `GET /bones/pool/{strategyId}/view` mirroring the `BonesViewerPageRenderer` pattern [mandatory — user specified]
- [ ] Add `StrategyDetailPageRenderer` class that renders a full HTML page for a single strategy with source code, lineage tree, opponent summaries, and recent match links [mandatory — user specified]
- [ ] Fix "View full detail →" link in `StrategyPoolPageRenderer` to point to `/bones/pool/{id}/view` instead of `/bones/pool/{id}` (JSON) [mandatory — UX]
- [ ] Make strategy card header clickable as a navigation link to `/bones/pool/{id}/view`, preserving the existing expand/collapse behavior via a separate toggle control [mandatory — user specified]
- [ ] Wire `pool.js` to handle the CSS class fix correctly: expand/collapse must toggle `.pool-card-detail` visibility, load source/lineage/opponents on first expand from `/bones/pool/{id}` JSON API, and not interfere with the new click-to-navigate link [mandatory — behavior-proof]
- [ ] Add a "Back to pool dashboard" navigation link on the strategy detail page [mandatory — UX continuity]
- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory — behavior-proof policy]

---

## Test Plan

### `StrategyPoolPageRenderer.Render` — Card click and detail expansion

1. `Render_GivenPoolSnapshot_ExpectedCardDetailDivUsesPoolCardDetailClass`
   *Assumption*: The server-rendered HTML uses class `pool-card-detail` on the expandable detail container, not `pool-card-body`.

2. `Render_GivenPoolSnapshot_ExpectedViewFullDetailLinkPointsToViewRoute`
   *Assumption*: The "View full detail →" link href matches pattern `/bones/pool/{id}/view`, not `/bones/pool/{id}`.

3. `Render_GivenPoolSnapshot_ExpectedCardHeaderWrapsInLink`
   *Assumption*: Each card header is wrapped in an anchor tag linking to `/bones/pool/{id}/view` for direct navigation.

### `pool.js` — toggleCard and click handling

4. `PoolJs_ToggleCard_GivenDetailDivPresent_ExpectedRemovesHiddenAndTogglesExpanded`
   *Assumption*: Calling `toggleCard` on a card element that contains `.pool-card-detail` removes the `hidden` class, sets `display: block`, and adds `expanded` class to the card.

5. `PoolJs_ToggleCard_GivenSecondClick_ExpectedHidesDetailAndRemovesExpanded`
   *Assumption*: Clicking an already-expanded card collapses it by setting `display: none` and removing `expanded` class.

6. `PoolJs_ToggleCard_GivenFirstExpand_ExpectedLoadsSourceAndLineageFromApi`
   *Assumption*: On first expansion, `toggleCard` fetches `/bones/pool/{strategyId}` and populates `.pool-card-source code` textContent and `.pool-card-lineage` innerHTML from the JSON response.

7. `PoolJs_CardClick_GivenAnchorTagTarget_ExpectedDoesNotToggle`
   *Assumption*: Clicking an `<a>` tag inside a card does not trigger `toggleCard` (navigation proceeds normally).

8. `PoolJs_CardClick_GivenNonAnchorTarget_ExpectedTogglesCard`
   *Assumption*: Clicking anywhere in the card that is not an `<a>` tag calls `toggleCard`.

### `StrategyDetailPageRenderer` — New detail page route

9. `StrategyDetailPageRenderer_Render_GivenValidStrategy_ExpectedReturnsHtmlWithSourceCode`
   *Assumption*: The rendered HTML contains the strategy's source code in a `<pre>` block.

10. `StrategyDetailPageRenderer_Render_GivenValidStrategy_ExpectedReturnsHtmlWithLineageTree`
    *Assumption*: The rendered HTML contains predecessor, outcome, diff summary, and successor information.

11. `StrategyDetailPageRenderer_Render_GivenValidStrategy_ExpectedReturnsHtmlWithOpponentTable`
    *Assumption*: The rendered HTML contains an opponent summaries table with opponent ID, kind, ELO, and games columns.

12. `StrategyDetailPageRenderer_Render_GivenValidStrategy_ExpectedReturnsHtmlWithBackToPoolLink`
    *Assumption*: The rendered HTML contains a navigation link back to `/bones/pool/view`.

13. `StrategyDetailPageRenderer_Render_GivenUnknownStrategyId_ExpectedReturns404Html`
    *Assumption*: Requesting a non-existent strategy ID returns an HTML 404 page, not a JSON 404.

### `BonesWebHost.MapPoolRoutes` — Route registration

14. `MapPoolRoutes_GivenStrategyId_ExpectedPoolViewRouteReturnsHtml`
    *Assumption*: `GET /bones/pool/{strategyId}/view` returns `text/html; charset=utf-8` with status 200 for a valid strategy.

15. `MapPoolRoutes_GivenStrategyId_ExpectedPoolViewRouteReturns404ForUnknown`
    *Assumption*: `GET /bones/pool/{strategyId}/view` returns 404 for an unknown strategy ID.

16. `MapPoolRoutes_RegistersStrategyDetailViewEndpoint`
    *Assumption*: `MapPoolRoutes` registers the `/bones/pool/{strategyId}/view` pattern alongside existing pool routes.

### Integration — Full click-to-navigate flow

17. `PoolDashboard_ClickCardHeader_ExpectedNavigatesToStrategyDetailView`
    *Assumption*: Loading `/bones/pool/view` in a `WebApplicationFactory` client, the rendered HTML contains clickable card headers whose anchor href points to `/bones/pool/{id}/view`, and following that link returns a 200 HTML page containing the strategy's source code.

18. `PoolDetailView_ClickBackLink_ExpectedReturnsToPoolDashboard`
    *Assumption*: The strategy detail page contains a "Back to pool" link whose href is `/bones/pool/view`, and following it returns the dashboard HTML.

---

## Discovered Gaps

1. **CSS class mismatch (critical)** — `pool.js:16` queries `.pool-card-body` but `StrategyPoolPageRenderer.cs:69` renders `.pool-card-detail`. The `toggleCard` function always returns early with no effect. This makes the entire expand/collapse interaction inoperable. Covered by: checklist item 1, test 4.

2. **No HTML strategy detail route** — `BonesWebHost.cs` only maps JSON endpoints for pool routes. There is no `/bones/pool/{strategyId}/view` HTML route. This means users cannot navigate to a dedicated strategy detail page from the dashboard. Covered by: checklist items 2-3, tests 9-16.

3. **"View full detail →" links to JSON** — `StrategyPoolPageRenderer.cs:73` renders `<a href="/bones/pool/{id}">` which returns JSON, not a usable HTML page. Covered by: checklist item 4, test 2.

4. **No test coverage for pool page interactions** — The existing `BonesPoolApiEndpointTests.cs` covers JSON API endpoints only. There are zero tests for the server-rendered HTML page or the `pool.js` client-side behavior. Covered by: all 18 planned tests.

5. **Pre-existing test failures (11)** — 6 `BehaviorProofComplianceGateTests` failures indicate stale requirement documents that need updating, and 5 DOM rendering tests fail for layout assertions. These are unrelated to the pool view issue and are out of scope for this plan.

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
