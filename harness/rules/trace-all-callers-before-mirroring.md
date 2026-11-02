---
kind: rule
mode: agent
description: Before adding a new route registration method that mirrors an existing one, trace all call sites of the existing method across every project in the solution — not just the most obvious entry point.
---

# Rule: Trace All Callers Before Mirroring Routes

## The Problem

When adding a new `MapXxxRoutes` method to match an existing `MapYyyRoutes`, it's easy to wire it only in the Web project's `Program.cs`. But hosts, test fixtures, and integration apps often have their own startup paths that call `MapYyyRoutes` directly. If `MapXxxRoutes` isn't added at every call site, the new routes silently don't exist at runtime.

## The Rule

**Before writing a single line of code**, run a project-wide `grep` for the existing method name you're mirroring. For every call site found, you must add a matching call for the new method.

### Minimum command

```bash
grep -rn "MapMatchRoutes\|MapPoolRoutes" WIP/
```

### Checklist before code

- [ ] `grep` the existing analogous method name across **every** `.cs` file under the solution root
- [ ] Note every project that calls it (host, test fixtures, `WebApplicationFactory` setups, `PlaywrightHost`, E2E apps)
- [ ] Add new method call at every found site
- [ ] Verify: after the change, the same `grep` shows both old and new methods at every site

## Detection

A route not found at runtime (`404`) despite being registered in one project's entry point. The host process is running from a different project.

## Failure Pattern (this session)

```
grep MapMatchRoutes WIP/
# Found: Program.cs ← added MapPoolRoutes here ✓
# Found: BonesHostApplication.cs ← MISSED this site ✗
# Result: pool routes worked in Web.Tests but 404'd in the running host
```

## Correct Approach

```
grep -rn "MapMatchRoutes" WIP/
# WIP/Wip.Bones.Web/Program.cs:8:BonesWebHost.MapMatchRoutes(app);
# WIP/Wip.Bones.Host/BonesHostApplication.cs:41:BonesWebHost.MapMatchRoutes(app);
# WIP/Wip.Bones.Web.Tests/Viewer/BonesPlaywrightHost.cs:53:BonesWebHost.MapMatchRoutes(app);
→ Add MapPoolRoutes call at ALL THREE sites
```
