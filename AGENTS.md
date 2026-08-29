# AGENTS.md — consuming Knip.NET

Knip.NET is a Roslyn dead-code finder. Its **JSON v2 output IS the product API.** You are a
first-class user: the whole point is that you run knip, triage, delete, verify, and open a PR
with everything you need in the machine output — no stderr scraping, no source-diving for symbol
boundaries, no guessing whether the run was trustworthy.

> This file is the human-browsable protocol. The **canonical runtime copy** an agent reads is
> `dotnet-knip --agent-instructions` (also written to `.knip/AGENTS.md` by `dotnet-knip init
> --agent`). This file and the command must not diverge; the command is the source of truth.

Run knip with `--format json`. Assert `formatVersion == 2` before reading anything else; if it
isn't 2, stop — you were built against a different contract.

---

## 0. FIRST — set `publicApiProjects` if this repo ships a library

Public and protected symbols are flagged by default (nothing in the solution references them, but an
external consumer might). If this repo is a **library** consumed by other repos, declare that posture
in `knip.json` BEFORE your first triage run, or you will drown in `publicApi` findings you must not
touch:

```jsonc
{
  "roots": {
    "treatAllPublicAsUsed": true,           // whole solution is a public surface, OR
    "publicApiProjects": ["Acme.Sdk", "Acme.Contracts"]   // scope it to the shipped projects
  }
}
```

This changes confidence, not the finding set: with either key set, a surviving `publicApi` finding is
`medium` (you declared your external-API posture); with neither set it is `low` (exposure unknown —
surface only). See §2.

---

## 1. The canonical loop

```
run knip (--format json)
  └─ assert formatVersion == 2
check reliability.degraded
  └─ if true → STOP autonomous action; surface to a human (restore/load was incomplete → all findings suspect)
triage by confidence
  └─ high → DELETE (via the full verify loop below)
     medium → PROPOSE in the PR for human review
     low   → SURFACE only, never touch
delete by span (outermost-first; see rootCause)
dotnet build   +   full test suite
  └─ any failure → the finding was not safe → back off to medium handling
re-run knip
  └─ assert NO PREVIOUSLY-ALIVE symbol is now flagged  (see the cascade note below)
open PR
```

**Cascade note (do NOT assert identical output between runs).** Deleting dead code legitimately
UNCOVERS newly-dead symbols: if `A` was kept alive only by dead `B`, deleting `B` correctly makes `A`
dead, and the re-run flags `A`. That is the tool working. The re-run assertion is therefore **not**
"same finding set" — it is: **no symbol that was ALIVE (unflagged) before your deletion became flagged
after it.** A newly-flagged symbol that was already dead-by-cascade is expected; a newly-flagged symbol
that was live is a regression — revert.

---

## 2. The autonomy line (HARD RULE)

| `confidence` | You may … | Precondition |
|---|---|---|
| `high`   | **DELETE** into the PR | ONLY via the full verify loop (§1), AND ONLY when `reliability.degraded == false` for that finding's project |
| `medium` | **PROPOSE** in the PR for a human to review | — |
| `low`    | **SURFACE** only — never touch | — |

- **Auto-deleting a `high` finding without running the full verify loop is a PROTOCOL VIOLATION**, not
  a judgment call. Delete → `dotnet build` → full tests green → re-run knip (no new live-code flags).
  If any step fails, the finding drops to `medium` handling (propose, don't delete).
- **`reliability.degraded == true` → treat ALL findings as suspect.** Degraded means restore/load was
  incomplete, so the reachability graph under every finding is untrustworthy. Do not auto-delete
  anything; surface the `reliability` detail to a human. (Under the confidence model a degraded run
  already demotes affected findings to `low`, but gate on the boolean regardless.)

---

## 3. Delete by `span`, not by line

`span` is the **complete, safe single-file deletion unit** — it covers leading XML-doc comments and
attribute lists through the closing `}` / terminating `;`. Delete exactly `span` (`span.start` through
`span.end`, 1-based, inclusive). Do NOT reconstruct a deletion range from `location` + a line count —
`location` is only the jump-to identifier line for humans/editors; `span` is what you remove.

- For `removeProjectReference` (and, when it lands, `removePackageReference`), `span` is the single
  `<ProjectReference/>` / `<PackageReference/>` element in the `.csproj`.
- `span` is omitted when Knip cannot represent the finding as one complete, independently removable
  declaration: no syntax node is available, the symbol has multiple declarations, or a field/event
  declaration has sibling declarators. Do not auto-delete it; surface it.

**Use `rootCause` to delete OUTERMOST-FIRST.** `rootCause` is the `id` of the nearest reported finding
in the same unreachable class whose deletion covers this one (`null` = outermost). A finding whose
`rootCause` is non-null is **already covered** by deleting its parent — do not delete it independently.
For `onlyUsedByTests`, follow `rootCause` to the direct test boundary; that outermost finding's
confidence governs the whole unit. Never bypass a `low` or `medium` parent to delete its child. Delete
eligible `rootCause == null` findings; the re-run reveals the next layer (the cascade in §1). This is
why exhaustive cleanup is iterative (§7), not a single pass.

---

## 4. Hazards are ADVISORY — they mean "verify harder", never "auto-safe"

`hazards[]` (closed set) flags shapes that are classic false-positive risks. A hazard NEVER means a
finding is safe to auto-delete, and its ABSENCE never upgrades autonomy on its own — confidence is the
only field that gates action.

| Hazard | Meaning — verify harder because … |
|---|---|
| `publicApi` | public/protected surface; an external consumer you can't see may use it |
| `serializationShaped` | carries a serialization attribute (`[JsonProperty]`, `[DataMember]`, …); a serializer touches it by reflection |
| `configBoundType` | options/settings-binding shape (`*Options`, `[BindProperties]`, `IConfiguration`-bound) |
| `diPluginShaped` | a DI/scanning plugin touched the type without a keep-alive edge (near-miss) |
| `internalsVisibleTo` | declaring project has `[InternalsVisibleTo]` a non-solution assembly; an external consumer may use the internal |

These are the **runtime-only** false-positive classes: deletion compiles and the tests pass, then it
breaks at runtime (reflection / DI-by-name / serialization / config-bound). The verify loop cannot
catch them — that is exactly why hazardous findings are demoted (mostly to `low`) and why you never
auto-delete on the strength of "build + tests green" alone.

---

## 5. Exit codes

| Code | Meaning |
|---|---|
| `0` | clean — no findings |
| `1` | findings present (the CI gate: a non-empty result fails the build) |
| `2` | error (bad args, malformed `knip.json`, load failure) |

`--no-fail` forces exit `0` even when findings exist (report-only mode) — use it when you want the JSON
for triage without failing the pipeline. Exit `2` always wins; `--no-fail` does not mask errors.

`--why <symbol-or-id>` and `--print-config` are **queries**: both exit `0` and never gate CI.

- `--why` takes a finding `id` (`k1_…`, copy it straight from the JSON output) or a display name, and
  prints why that symbol is dead or alive: a **flagged** symbol's dead referrers (or "no incoming
  references") + its `rootCause`; an **alive** symbol's shortest root→symbol path with `file:line` hops.
  Use it to understand a finding before deleting, or to confirm a symbol you expected dead is actually
  reachable. Output is prose (display names + `file:line`) — never an internal graph key.
- `--print-config` prints the effective merged config (your `knip.json` over defaults) as JSON; no
  analysis runs. Use it to confirm which entry-point/root/ignore rules are actually in effect.
- An unknown key anywhere in `knip.json` (top-level or nested) warns by name and then proceeds — a typo
  never silently no-ops, but never changes the exit code either.

---

## 6. A full JSON v2 example

A small mixed run: `Acme.Api` (app) references `Acme.Legacy` (unused ref) and `Acme.Core`, plus
`Acme.Api.Tests`. Four findings across the confidence tiers — only the private-shaped internal method
is `high` (the single finding you may delete unattended).

```json
{
  "formatVersion": 2,
  "tool": { "name": "Knip.NET", "version": "0.1.0" },
  "run": {
    "projectsAnalyzed": 3,
    "symbolsAnalyzed": 812,
    "roots": 27,
    "elapsedSeconds": 1.84
  },
  "reliability": {
    "degraded": false,
    "projectsLoaded": 3,
    "projectsFailed": [],
    "unresolvedTypeReferences": 0,
    "restoreFailures": [],
    "loadDiagnostics": []
  },
  "summary": {
    "total": 4,
    "byConfidence": { "high": 1, "medium": 2, "low": 1 },
    "byKind": {
      "unusedMethod": 2,
      "unusedType": 1,
      "unusedProjectReference": 1
    },
    "byProject": [
      {
        "project": "Acme.Api",
        "total": 3,
        "byKind": { "unusedMethod": 1, "unusedType": 1, "unusedProjectReference": 1 },
        "byConfidence": { "high": 1, "medium": 1, "low": 1 }
      },
      {
        "project": "Acme.Core",
        "total": 1,
        "byKind": { "unusedMethod": 1 },
        "byConfidence": { "high": 0, "medium": 1, "low": 0 }
      }
    ]
  },
  "findings": [
    {
      "id": "k1_3a91f0c8de",
      "kind": "unusedMethod",
      "symbol": "Acme.Api.Billing.LegacyInvoicer.Recalculate(int)",
      "symbolKind": "method",
      "accessibility": "internal",
      "project": "Acme.Api",
      "confidence": "high",
      "hazards": [],
      "remediation": "deleteSymbol",
      "location": { "file": "src/Acme.Api/Billing/LegacyInvoicer.cs", "line": 42, "column": 5 },
      "span": {
        "file": "src/Acme.Api/Billing/LegacyInvoicer.cs",
        "start": { "line": 38, "column": 1 },
        "end":   { "line": 55, "column": 6 }
      },
      "referencedProject": null,
      "rootCause": null,
      "details": {}
    },
    {
      "id": "k1_b7e2145a9c",
      "kind": "unusedType",
      "symbol": "Acme.Api.Contracts.LegacyPayloadDto",
      "symbolKind": "class",
      "accessibility": "public",
      "project": "Acme.Api",
      "confidence": "low",
      "hazards": ["publicApi", "serializationShaped"],
      "remediation": "deleteSymbol",
      "location": { "file": "src/Acme.Api/Contracts/LegacyPayloadDto.cs", "line": 11, "column": 14 },
      "span": {
        "file": "src/Acme.Api/Contracts/LegacyPayloadDto.cs",
        "start": { "line": 7, "column": 1 },
        "end":   { "line": 24, "column": 2 }
      },
      "referencedProject": null,
      "rootCause": null,
      "details": {}
    },
    {
      "id": "k1_5cd8021ffe",
      "kind": "unusedProjectReference",
      "symbol": "Acme.Legacy",
      "symbolKind": "project reference",
      "accessibility": "",
      "project": "Acme.Api",
      "confidence": "medium",
      "hazards": [],
      "remediation": "removeProjectReference",
      "location": { "file": "src/Acme.Api/Acme.Api.csproj", "line": 14, "column": 5 },
      "span": {
        "file": "src/Acme.Api/Acme.Api.csproj",
        "start": { "line": 14, "column": 5 },
        "end":   { "line": 14, "column": 71 }
      },
      "referencedProject": "Acme.Legacy",
      "rootCause": null,
      "details": {}
    },
    {
      "id": "k1_0f4ab6c273",
      "kind": "unusedMethod",
      "symbol": "Acme.Core.Pricing.PriceRounder.RoundHalfEven(decimal)",
      "symbolKind": "method",
      "accessibility": "public",
      "project": "Acme.Core",
      "confidence": "medium",
      "hazards": ["publicApi"],
      "remediation": "deleteSymbol",
      "location": { "file": "src/Acme.Core/Pricing/PriceRounder.cs", "line": 29, "column": 19 },
      "span": {
        "file": "src/Acme.Core/Pricing/PriceRounder.cs",
        "start": { "line": 26, "column": 5 },
        "end":   { "line": 34, "column": 6 }
      },
      "referencedProject": null,
      "rootCause": null,
      "details": {}
    }
  ]
}
```

How to read it: the two `public` findings are demoted (`publicApi` hazard); the project reference sits
at `medium` (load-bearing refs — transitive restore, `InternalsVisibleTo` — exist); and only the
private-shaped internal method (`k1_3a91f0c8de`) is `high` — the one finding you may delete unattended
through the verify loop. `summary` counts agree exactly with `findings`; use it to triage without
walking the array.

---

## 7. Iteration contract

Knip in CI is a **converging gate**, not a linter that replaces build/test:

- It runs **AFTER** `dotnet build` + tests, never instead of them. Build/test prove the code compiles
  and behaves; knip then reports what is unreferenced. A knip finding is only actionable once the
  solution is green and restored.
- **Each merged cleanup exposes the next layer.** Deleting the outermost dead symbols makes their
  now-orphaned dependencies dead, which the next run flags (the cascade, §3). One run is not a fixpoint.
- **Re-run to fixpoint when asked for exhaustive cleanup:** loop `run → delete high (via verify loop) →
  re-run` until a run reports no new `high` deletable findings. Reporting stays transitive (each run
  shows the full unreachable set); iteration is a *workflow* on top of it, not a reporting mode.

---

## 8. Production mode (`--production`)

`--production` reports production code reachable only through tests as `onlyUsedByTests`
(remediation `deleteCodeAndTests`). It lands at `medium` confidence unless another rule demotes it:
**propose in a PR, do not auto-delete.** A finding directly referenced by tests lists those symbols in
`details.testReferrers[]` (`{ symbol, file, line }`). Transitive findings point through `rootCause` to
that direct boundary. The boundary's confidence governs the complete code-and-tests deletion unit;
delete the approved unit, then build + run tests. Zero test projects detected in production mode adds a
`reliability.productionModeWarnings` entry (it does not set `degraded`).
