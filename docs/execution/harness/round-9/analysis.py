#!/usr/bin/env python3
"""Pre-registered analysis for the live experiment (round 9).

Reads .tmp/live-experiment/results.jsonl, dedupes per (scenario, arm, index), excludes infra errors,
and emits the pre-registered statistics as JSON plus a human-readable table. Pure stdlib.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import statistics
from collections import Counter, defaultdict
from pathlib import Path

RESULTS = Path(__file__).with_name("results.jsonl")
OUT = Path(__file__).with_name("analysis.json")
SEED = 20261108


# ---------- statistics ----------

def wilson(k: int, n: int, z: float = 1.959963984540054) -> tuple[float, float, float]:
    if n == 0:
        return (0.0, 0.0, 0.0)
    p = k / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (p, max(0.0, centre - half), min(1.0, centre + half))


def log_comb(n: int, k: int) -> float:
    return math.lgamma(n + 1) - math.lgamma(k + 1) - math.lgamma(n - k + 1)


def fisher_exact_2x2(a: int, b: int, c: int, d: int) -> float:
    """Two-sided Fisher exact p-value for [[a,b],[c,d]]."""
    n = a + b + c + d
    row1 = a + b
    col1 = a + c
    observed = math.exp(log_comb(row1, a) + log_comb(n - row1, col1 - a) - log_comb(n, col1))
    total = 0.0
    lo = max(0, row1 + col1 - n)
    hi = min(row1, col1)
    for x in range(lo, hi + 1):
        prob = math.exp(log_comb(row1, x) + log_comb(n - row1, col1 - x) - log_comb(n, col1))
        if prob <= observed + 1e-12:
            total += prob
    return min(1.0, total)


def rankdata(values: list[float]) -> list[float]:
    order = sorted(range(len(values)), key=lambda i: values[i])
    ranks = [0.0] * len(values)
    i = 0
    while i < len(order):
        j = i
        while j + 1 < len(order) and values[order[j + 1]] == values[order[i]]:
            j += 1
        avg = (i + j) / 2.0 + 1.0
        for k in range(i, j + 1):
            ranks[order[k]] = avg
        i = j + 1
    return ranks


def mann_whitney(x: list[float], y: list[float]) -> tuple[float, float]:
    """Two-sided Mann-Whitney U with tie-corrected normal approximation."""
    n1, n2 = len(x), len(y)
    if n1 == 0 or n2 == 0:
        return (float("nan"), float("nan"))
    combined = list(x) + list(y)
    ranks = rankdata(combined)
    r1 = sum(ranks[:n1])
    u1 = r1 - n1 * (n1 + 1) / 2.0
    u2 = n1 * n2 - u1
    u = min(u1, u2)
    # tie correction
    counts = Counter(combined)
    tie = sum(t ** 3 - t for t in counts.values())
    n = n1 + n2
    sigma = math.sqrt(n1 * n2 / 12.0 * ((n + 1) - tie / (n * (n - 1)))) if n > 1 else 0.0
    if sigma == 0:
        return (u, 1.0)
    mean = n1 * n2 / 2.0
    z = (u - mean) / sigma
    # two-sided normal p
    p = 2.0 * (1.0 - 0.5 * (1.0 + math.erf(abs(z) / math.sqrt(2.0))))
    return (u, min(1.0, p))


def bootstrap_ci(values: list[float], fn, rng: random.Random, reps: int = 10000):
    if not values:
        return (float("nan"), float("nan"))
    samples = []
    n = len(values)
    for _ in range(reps):
        samples.append(fn([values[rng.randrange(n)] for _ in range(n)]))
    samples.sort()
    return (samples[int(0.025 * reps)], samples[int(0.975 * reps) - 1])


def median_ratio_ci(new: list[float], old: list[float], rng: random.Random, reps: int = 10000):
    if not new or not old:
        return (float("nan"), float("nan"), float("nan"))
    ratios = []
    for _ in range(reps):
        a = [new[rng.randrange(len(new))] for _ in range(len(new))]
        b = [old[rng.randrange(len(old))] for _ in range(len(old))]
        if statistics.median(b) == 0:
            continue
        ratios.append(statistics.median(a) / statistics.median(b))
    ratios.sort()
    return (statistics.median(new) / statistics.median(old), ratios[int(0.025 * len(ratios))], ratios[int(0.975 * len(ratios)) - 1])


# ---------- data ----------

def load_rows(path):
    rows = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            rows.append(json.loads(line))
    # dedupe per cell: prefer the last non-infra record, else the last record
    cells: dict[tuple[str, str, int], list[dict]] = defaultdict(list)
    for row in rows:
        cells[(row["Scenario"], row["Arm"], row["Index"])].append(row)
    chosen = []
    for key, entries in cells.items():
        good = [r for r in entries if not r.get("InfraError")]
        chosen.append(good[-1] if good else entries[-1])
    return chosen


def group(rows, scenario, arm):
    return [r for r in rows if r["Scenario"] == scenario and r["Arm"] == arm]


def summary(values):
    if not values:
        return {"n": 0}
    q = statistics.quantiles(values, n=4) if len(values) >= 4 else [values[0], statistics.median(values), values[-1]]
    return {
        "n": len(values),
        "median": statistics.median(values),
        "q1": q[0],
        "q3": q[2],
        "mean": statistics.fmean(values),
        "min": min(values),
        "max": max(values),
    }


def analyse(rows, rng):
    out = {"seed": SEED, "cells": {}, "tests": {}}
    scenarios = ["normal", "injection", "long-context", "forced-tool-loop", "enhance"]
    arms = ["old", "new-eager", "new-lazy"]
    for scenario in scenarios:
        for arm in arms:
            rs = group(rows, scenario, arm)
            if not rs:
                continue
            n = len(rs)
            k = sum(1 for r in rs if r["Success"])
            p, lo, hi = wilson(k, n)
            tokens = [r["TotalTokens"] for r in rs]
            latency = [r["LatencyMs"] for r in rs]
            calls = [r["Calls"] for r in rs]
            first = sum(1 for r in rs if r.get("FirstTrySuccess"))
            out["cells"][f"{scenario}|{arm}"] = {
                "n": n,
                "success": k,
                "success_rate": p,
                "success_ci": [lo, hi],
                "first_try": first,
                "first_try_rate": first / n if n else 0.0,
                "infra": sum(1 for r in rs if r.get("InfraError")),
                "tokens": summary(tokens),
                "latency_ms": summary(latency),
                "calls": summary(calls),
                "tool_call_runs": sum(1 for r in rs if r.get("ToolCallNames")),
                "unknown_tool_calls": sum(r.get("UnknownToolCallCount", 0) for r in rs),
                "promotions": dict(Counter(r.get("Promotion") for r in rs if r.get("Promotion"))),
                "errors": dict(Counter(r.get("ErrorType") for r in rs if r.get("ErrorType"))),
            }

    def arm_rows(scenario, arm):
        return group(rows, scenario, arm)

    def pair(scenario, arm_a, arm_b):
        a = arm_rows(scenario, arm_a)
        b = arm_rows(scenario, arm_b)
        if not a or not b:
            return None
        a_tokens = [r["TotalTokens"] for r in a]
        b_tokens = [r["TotalTokens"] for r in b]
        a_lat = [r["LatencyMs"] for r in a]
        b_lat = [r["LatencyMs"] for r in b]
        a_succ = sum(1 for r in a if r["Success"])
        b_succ = sum(1 for r in b if r["Success"])
        # Fisher [[a_succ, a_n-a_succ], [b_succ, b_n-b_succ]]
        p_success = fisher_exact_2x2(a_succ, len(a) - a_succ, b_succ, len(b) - b_succ)
        u_tok, p_tok = mann_whitney(a_tokens, b_tokens)
        u_lat, p_lat = mann_whitney(a_lat, b_lat)
        tok_ratio = median_ratio_ci(a_tokens, b_tokens, rng)
        lat_ratio = median_ratio_ci(a_lat, b_lat, rng)
        return {
            "a": arm_a, "b": arm_b,
            "n_a": len(a), "n_b": len(b),
            "success_a": a_succ, "success_b": b_succ,
            "fisher_p_success": p_success,
            "tokens_ratio_a_over_b": tok_ratio,
            "tokens_mannwhitney_p": p_tok,
            "latency_ratio_a_over_b": lat_ratio,
            "latency_mannwhitney_p": p_lat,
        }

    out["tests"]["normal_old_vs_lazy"] = pair("normal", "old", "new-lazy")
    out["tests"]["normal_eager_vs_lazy"] = pair("normal", "new-eager", "new-lazy")
    out["tests"]["normal_old_vs_eager"] = pair("normal", "old", "new-eager")
    out["tests"]["injection_old_vs_lazy"] = pair("injection", "old", "new-lazy")
    out["tests"]["long_old_vs_lazy"] = pair("long-context", "old", "new-lazy")
    out["tests"]["enhance_old_vs_lazy"] = pair("enhance", "old", "new-lazy")

    forced = {arm: group(rows, "forced-tool-loop", arm) for arm in ("old", "new-eager")}
    out["tests"]["forced"] = {
        arm: {
            "n": len(rs),
            "fail_closed": sum(1 for r in rs if (r.get("ErrorMessage") or "").find("MaxStepsReached") >= 0),
            "success": sum(1 for r in rs if r["Success"]),
            "errors": dict(Counter(r.get("ErrorType") for r in rs if r.get("ErrorType"))),
        }
        for arm, rs in forced.items()
    }
    return out


def print_report(out):
    print("=== PRE-REGISTERED ANALYSIS ===")
    for key, cell in out["cells"].items():
        scenario, arm = key.split("|")
        print(
            f"{scenario:15s} {arm:10s} n={cell['n']:2d} success={cell['success']:2d} "
            f"({cell['success_rate']*100:5.1f}% [{cell['success_ci'][0]*100:5.1f},{cell['success_ci'][1]*100:5.1f}]) "
            f"first={cell['first_try']:2d} tokens_med={cell['tokens'].get('median', 0):8.0f} "
            f"ms_med={cell['latency_ms'].get('median', 0):7.0f} calls_med={cell['calls'].get('median', 0):.1f} "
            f"tools_runs={cell['tool_call_runs']} unknown={cell['unknown_tool_calls']} promo={cell['promotions']}"
        )
    print("\n=== TESTS ===")
    for name, test in out["tests"].items():
        print(name, json.dumps(test))


def selftest():
    # Fisher: standard 2x2 example p ~= 0.4857
    p = fisher_exact_2x2(3, 1, 1, 3)
    assert abs(p - 0.4857142857) < 1e-6, p
    # Wilson: 5/10 -> [0.2366, 0.7634]
    _, lo, hi = wilson(5, 10)
    assert abs(lo - 0.2366) < 5e-4 and abs(hi - 0.7634) < 5e-4, (lo, hi)
    # Mann-Whitney: identical samples -> p ~= 1
    _, p2 = mann_whitney([1, 2, 3], [1, 2, 3])
    assert p2 > 0.9, p2
    print("SELFTEST_OK", {"fisher": p, "wilson": wilson(5, 10), "mw_identical": p2})


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--selftest", action="store_true")
    parser.add_argument("--results", type=Path, default=RESULTS)
    parser.add_argument("--out", type=Path, default=OUT)
    args = parser.parse_args()
    if args.selftest:
        selftest()
        return 0
    rows = load_rows(args.results)
    rng = random.Random(SEED)
    out = analyse(rows, rng)
    args.out.write_text(json.dumps(out, indent=2), encoding="utf-8")
    print_report(out)
    print(f"\nWROTE {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
