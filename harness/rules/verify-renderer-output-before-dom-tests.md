---
kind: rule
mode: agent
description: Before writing DOM assertions (AngleSharp/Playwright) against server-rendered HTML, dump the renderer output once and pin CSS class names. Guessing class names leads to mass test failures.
---

# Rule: Verify Renderer Output Before DOM Tests

## The Problem

When you write the HTML page renderer and the DOM tests as independent streams of thought, CSS class name mismatches are guaranteed. The renderer uses names like `.pool-card-stats` while tests assert `.pool-card-elo`, `.pool-card-winrate`. Every DOM `QuerySelector` returns null and the tests fail with `Assert.NotNull() Failure`.

## The Rule

**Before writing a single DOM assertion**, execute the renderer and inspect the actual HTML output at least once. Pin every CSS class name, data attribute, and element hierarchy.

### Minimum procedure

1. Write the server-side renderer and a minimal test that captures its output as a string
2. `Console.WriteLine` the HTML or save to a temp file
3. Scan for actual class names: `class="..."`, `id="..."`, `data-*="..."`
4. Write test selectors using those exact names
5. Re-run to confirm

### Quick check script

```bash
# In a test or script:
var html = StrategyPoolPageRenderer.Render(snapshot);
File.WriteAllText("/tmp/pool-render-output.html", html);
# Then read /tmp/pool-render-output.html to verify selectors
```

## Detection

Multiple DOM test failures with `Assert.NotNull() Failure: Value is null` on `QuerySelector(".some-class")`. The class exists in your head but not in the rendered HTML.

## Failure Pattern (this session)

```csharp
// Test expected these classes (from the plan doc):
Assert.NotNull(firstCard.QuerySelector(".pool-card-elo"));    // ✗ not in HTML
Assert.NotNull(firstCard.QuerySelector(".pool-card-winrate")); // ✗ not in HTML
Assert.NotNull(firstCard.QuerySelector(".pool-card-matches")); // ✗ not in HTML

// Renderer actually produced:
// <div class="pool-stat"><span class="stat-label">ELO</span><span class="stat-value">1600</span></div>
// <div class="pool-stat"><span class="stat-label">Win Rate</span><span class="stat-value">80.0%</span></div>
// → 8 tests failed, all from wrong selectors
```

## Correct Approach

Dump HTML → scan actual classes → write selectors against reality, not the plan doc.
