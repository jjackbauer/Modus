# Round 14 report — the equity-aware policy is now a CLI-evaluable artifact

**Status:** verified artifact/CLI slice; D4/D1 remain Partial. Goal active.

## Delivered

- `EquityPolicyJson`: canonical, content-addressed schema `tholdem.equity-policy.v1` with strict
  parse and named refusals.
- `EquityPolicyStore`: `<data>/equity-policies/<id>.json`, fail-closed on malformed files.
- `evaluate4 --challenger-kind/--incumbent-kind strategy|equity-policy`: resolves either family;
  policy hashes are content hashes; the verifier rebuilds fresh agents through the same factory.

## Verified

| Check | Observed |
|---|---|
| Default policy hash | `760ac54c77d737a71a4f488e1752574fb6dde1f4367fa6372959506e180091ac` |
| Equity-policy report | `challengerPolicyHash 9c8924…`, report hash `05ca3b3a…`, `report-05ca3b3a.json` |
| Strategy path unchanged | `report-920f9dcb.json` and the decision/verification pair still pass |
| Pointer | no `active-champion.json` after either kind |
| Tests / suite / build | 3/3 JSON, 6/6 CLI, **570/570**, 0 errors |

## Limits

- The v1 artifact omits `PreflopRaiseMargin`; `Write` refuses a non-default margin rather than
  dropping the field (OBS-010, OQ-003). The experiment uses the default margin.
- No strength or promotion claim; the test plan is tiny.
- Anchor 3 remains the round-7 null.

## Next

The pre-registered equity-policy vs legacy promotion experiment through `evaluate4` with an anchor
plan, recorded with its honest verdict (including inconclusive/rejected).
