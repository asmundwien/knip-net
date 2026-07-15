# WS8 — Agent-First Interface Design Proposal (for sign-off)

**Status:** DESIGN ONLY — no production code changed. Needs human sign-off before WS8b–d implementation.
**Blocker to resolve at sign-off:** **§4 L9 — the confidence/hazard rule table** (the "agents may act
autonomously" line). WS8b cannot start until this is signed off.

**Scope:** the machine output (JSON v2), the reliability signal, the stable finding `id`, the deletion
`span`, `confidence`/`hazards`/`remediation`, plus two agent-facing CLI additions (`--why`,
`--print-config`), a published config JSON Schema, generalized unknown-key warnings, and the SARIF field
mapping. Console output stays human-first and is unchanged in shape.

Written against the code as it exists today:
`Finding` / `AnalysisResult` (`src/Knip.Core/Model/`), the three reporters
(`src/Knip.Core/Reporting/Reporters.cs`), `CliOptions` / `Runner` (`src/Knip.Cli/`),
`DeadCodeAnalyzer` + `GraphState` + `SymbolId` (`src/Knip.Core/Analysis/`), and `KnipConfig`
(`src/Knip.Core/Configuration/`).

---

## 0. Product thesis and hard constraints

Agents are first-class users. Canonical flow: **run knip → triage → delete → verify → open PR.**
Everything the agent needs is in the machine output — no stderr scraping, no source-diving for symbol
boundaries, no guessing whether the run was trustworthy. **The JSON output IS the product API.**

Constraints this design honours (all pre-existing invariants):

- **Invariant #1 — internal keys never leak.** `SymbolId.For` keys (`Assembly::docId`) are internal.
  No output field — not `id`, not `--why`, not SARIF `partialFingerprints` — ever contains a raw graph
  key. `--why` is prose + `file:line`. The stable `id` is a content hash (opaque, but reproducible from
  *published* fields, never from the internal key).
- **§3.7 / §3.8 unchanged — WS8 ENRICHES, never changes, the finding set.** The set of emitted findings
  is byte-for-byte the same set the analyzer produces today (plus the WS3/WS7/WS-enum kinds as they land).
  WS8 only *annotates* each finding. **Low confidence is NOT a licence to emit a finding that §3.7/§3.8
  currently suppress** (constructors, overrides/interface impls, ignored symbols, generated declarations,
  the conservative project-reference rule, etc.). If the analyzer suppresses it today, it stays suppressed;
  confidence only ever grades what already survives to `result.Findings`.
- **Determinism (L2).** Two runs over the same fixture produce identical `id`s and identical order.
  Ordering is already deterministic (`BuildProjectReferenceFindings` sorts by project → file → line →
  symbol); `id` is a pure function of published fields (§3.2).
- **`Knip.Core` stays CLI-free (invariant #9).** The enrichment (id/span/confidence/hazards/remediation)
  is computed in `Knip.Core` and carried on the model. `--why` and `--print-config` are CLI concerns in
  `Knip.Cli`; `--why` may call a `Knip.Core` provenance API (§5.2).

---

## 1. JSON v2 — the shape

**This is a BREAKING change to the JSON output. We do NOT maintain both shapes** (acceptable pre-1.0).
`formatVersion` lets a consumer assert the contract it was built against and fail fast otherwise.

Top level:

```
{
  "formatVersion": 2,
  "tool":       { "name": "Knip.NET", "version": "0.1.0" },
  "run":        { … timing / target … },
  "reliability":{ … trust signal, incl. degraded:bool … },   // §1.1
  "summary":    { … counts by project × kind × confidence … },// §1.2
  "findings":   [ … enriched findings … ]                     // §1.3
}
```

### 1.1 `reliability` — is this run trustworthy?

The agent reads exactly one boolean to gate autonomous action: `reliability.degraded`. When `true`, the
agent must NOT auto-delete; it surfaces the detail to a human. The detail fields explain *why*.

```jsonc
"reliability": {
  "degraded": false,                  // OR of the conditions below (see rule)
  "projectsLoaded": 4,
  "projectsFailed": 0,
  "restoreFailures": [],              // string[]: per-project restore/load failure detail
  "loadDiagnostics": [                // the existing LoadDiagnostics channel, structured
    // { "severity": "warning", "code": "unresolvedTypes", "message": "…" }
  ],
  "unresolvedTypeReferences": 0,      // state.UnresolvedTypeReferences (invariant #6 signal)
  "productionModeWarnings": [],       // WS7: e.g. "production mode requested, zero test projects detected"
  "testProjectClassification": []     // WS7: [{ "project": "...", "kind": "test|production", "signal": "..." }]
}
```

**`degraded` rule (proposed):** `degraded == true` iff **any** of:

1. `projectsFailed > 0` (a project in the target failed to load), OR
2. `unresolvedTypeReferences > 0` (the invariant-#6 "solution not fully restored" signal — today this
   already prepends a load diagnostic), OR
3. `restoreFailures` is non-empty, OR
4. any `loadDiagnostics` entry has `severity == "error"`.

Production-mode warnings (`productionModeWarnings`, e.g. WS7's zero-test-project case) are **surfaced but
do NOT by themselves set `degraded`** — they change the *meaning* of `OnlyUsedByTests` findings, not the
trustworthiness of the graph. They are called out as an open question (§7 Q4) because reasonable people
differ here. This maps battery **L3** (broken-restore fixture → `degraded:true` with detail; clean →
`false` with zeroes).

### 1.2 `summary` — counts by project × kind × confidence

Pure aggregation of `findings`; **counts must agree exactly** with the array (battery **L8**). Consumers
use it for a triage dashboard without walking every finding.

```jsonc
"summary": {
  "total": 5,
  "byConfidence": { "high": 3, "medium": 1, "low": 1 },
  "byKind":       { "unusedType": 2, "unusedMethod": 1, "unusedProjectReference": 1, "onlyUsedByTests": 1 },
  "byProject": [
    {
      "project": "Acme.Api",
      "total": 3,
      "byKind":       { "unusedMethod": 1, "unusedProjectReference": 1, "onlyUsedByTests": 1 },
      "byConfidence": { "high": 2, "medium": 0, "low": 1 }
    }
    // …
  ]
}
```

### 1.3 The enriched finding

```jsonc
{
  "id": "k1_9f2c1a7b4e",              // stable content hash (§3) — opaque, reproducible, NOT a graph key
  "kind": "unusedMethod",             // FindingKind, camelCase (see §2 vocabulary)
  "symbol": "Acme.Api.Billing.LegacyInvoicer.Recalculate(int)",  // display name (unchanged)
  "symbolKind": "method",
  "accessibility": "internal",
  "project": "Acme.Api",
  "confidence": "high",               // high | medium | low  (§4)
  "hazards": ["publicApi"],           // string[] from a closed set (§4.2); [] when none
  "remediation": "deleteSymbol",      // closed set (§2)
  "location": {                       // 1-based, where you'd jump to (unchanged semantics)
    "file": "src/Acme.Api/Billing/LegacyInvoicer.cs",
    "line": 42, "column": 5
  },
  "span": {                           // THE DELETION UNIT — see §3.3 / battery L4
    "file": "src/Acme.Api/Billing/LegacyInvoicer.cs",
    "start": { "line": 38, "column": 1 },   // first line of leading XML-doc / attributes
    "end":   { "line": 55, "column": 6 }    // through the closing brace / semicolon (inclusive)
  },
  "referencedProject": null,          // set only for removeProjectReference kinds (WS2)
  "details": { }                      // kind-specific extras (e.g. WS7 testReferrers[]) — see §2
}
```

Notes:

- `location` (the jump-to point) and `span` (the deletion unit) are **both** present and are different
  things: `location` is the declaration identifier line; `span` covers attributes + XML-doc + the whole
  member/type body. Agents delete `span`; humans/editors jump to `location`.
- For `removeProjectReference` (WS2) the `span` is the `<ProjectReference …/>` element in the `.csproj`
  (see §3.3), `location.line` may be 0 today, and `referencedProject` names the referenced project.

---

## 2. Remediation & kind vocabulary (folds WS2, reserves WS3/WS7/WS-enum)

`remediation` is a **closed set** — the machine action an agent takes. `kind` stays `FindingKind` (a finer
label). The mapping:

| `remediation`           | `FindingKind`(s)                                            | Owner  | Deletion unit (`span`) |
|-------------------------|------------------------------------------------------------|--------|------------------------|
| `deleteSymbol`          | `unusedType` `unusedMethod` `unusedProperty` `unusedField` `unusedEvent` | core   | member/type decl incl. attributes + XML-doc |
| `removeFromInterface`   | (reserved) `unusedInterfaceMember`                         | WS-enum| the interface member decl; agent must also touch impls — flagged, see §7 Q5 |
| `removeProjectReference`| `unusedProjectReference`  **(WS2 folds in here)**          | WS2    | the `<ProjectReference/>` element in the `.csproj` |
| `removePackageReference`| (reserved) `unusedPackageReference`                       | WS3    | the `<PackageReference/>` element |
| `deleteCodeAndTests`    | (reserved) `onlyUsedByTests`                               | WS7    | the production symbol **plus** its test referrers (see `details.testReferrers`) |

- **WS2 today** emits `FindingKind.UnusedProjectReference`. In v2 that finding carries
  `remediation: "removeProjectReference"`, `referencedProject`, and a `.csproj`-element `span`. No new
  analyzer behaviour — pure annotation of the existing finding.
- **WS7** (`onlyUsedByTests`, K2/K3/K5) sets `remediation: "deleteCodeAndTests"` and populates
  `details.testReferrers` with the referencing test symbols (prose display names + `file:line`, never
  graph keys) so the deletion unit — code AND its tests — is explicit in the output (K3).
- **WS3** (`unusedPackageReference`) and **WS-enum** (`unusedInterfaceMember`) reserve their slots now so
  they emit into this vocabulary rather than inventing their own (runbook §WS8 priority note).

`details` is an open object keyed by `remediation`; each owner defines its own shape. Reserving it now
keeps v2 stable when WS3/WS7/WS-enum land (they add keys under `details`, not new top-level fields).

---

## 3. The stable `id` and the `span` — precise specs

### 3.1 Why a content hash and not the graph key

The graph key (`Assembly::docId`) is internal (invariant #1) and would leak the assembly + doc-comment
structure. Instead `id` is a hash of **published, human-meaningful fields** — reproducible by anyone who
has the JSON, stable across runs, and revealing nothing internal.

### 3.2 `id` hashing spec (reproducible — battery L2)

```
material = kind "" symbol "" project        // for symbol findings
material = kind "" symbol "" project "" referencedProject   // for removeProjectReference
id       = "k1_" + lowerhex( SHA-256( UTF-8(material) ) )[0..10]              // first 10 hex chars (40 bits)
```

Precise rules (so two implementations agree byte-for-byte):

- **Inputs:** exactly the *serialized* values of `kind` (camelCase string, e.g. `unusedMethod`), `symbol`
  (the display string already produced by `ToFinding`), `project`, and — only for
  `removeProjectReference` — `referencedProject`. **Nothing else** (no file path, no line — those move when
  code is edited; L2 requires stability across the delete-and-rerun loop).
- **Separator:** U+001F (unit separator), which cannot appear in any of the fields.
- **Encoding:** UTF-8, no BOM. No normalization/trimming beyond what the fields already carry.
- **Hash:** SHA-256; take the first 10 lowercase-hex characters. `k1_` prefix reserves room to bump the
  scheme (`k2_`) without collision if we ever change the material.
- **Collision stance:** 40 bits is ample for a single solution's finding set; on the astronomically
  unlikely tie within one run, findings still differ by array position and `--why` accepts a `symbol`
  too, so a collision is a display nuisance, never a correctness bug. (Open question §7 Q3 if the human
  wants 64 bits.)

`kind + symbol + project` uniquely identifies a finding within a run because the analyzer reports the
outermost dead symbol only and `symbol` is fully qualified with signature.

### 3.3 The `span` — the deletion unit (battery L4)

`span` is what an agent deletes to remove the finding, verified by battery **L4**: *delete every
high-confidence finding strictly by its reported span, then build → compiles green.* Rules:

- **Start** = the earliest of: the first leading-trivia XML-doc comment (`///` block), the first attribute
  list on the declaration, or the declaration's own first token. Computed from the Roslyn declaration
  syntax node's **full span including leading trivia** (`node.GetLeadingTrivia()` — take doc-comment and
  attribute trivia; do NOT swallow an unrelated preceding blank line or a comment that belongs to the
  previous member).
- **End** = the declaration's closing `}` (types/methods with a body) or terminating `;` (fields,
  auto-props, abstract members), inclusive.
- Positions are **1-based line/column**, matching `location`.
- For `removeProjectReference` / `removePackageReference`, `span` is the single `<ProjectReference/>` /
  `<PackageReference/>` XML element in the project file (start of `<` to end of `/>` or `</…>`).
- **Attributes hazard:** if a member carries an attribute that is itself the reason it's *shaped*
  (serialization/config), that raises a hazard (§4.2) and typically lowers confidence — the span still
  covers the attribute, but the agent is warned.

This is a **computed enrichment** in `Knip.Core` (new helper off the declaring `SyntaxNode`), not a change
to which findings exist.

---

## 4. **L9 — Confidence & hazard rules (the decision to sign off)**

This is the central decision. It encodes §3.8 (false positives are THE product risk) into the field that
tells an agent whether it may act without a human. **Recommendation below; this table is what needs human
eyes.**

Governing principle: **confidence never widens the finding set.** Every finding here already survived
§3.7/§3.8 suppression. Confidence answers only: *given that we flagged it, how safe is autonomous
deletion?*

### 4.1 Confidence rules (proposed)

A finding starts at **high** and is demoted by the first matching rule (most-severe wins):

| Rule | Condition | Result | Rationale |
|------|-----------|--------|-----------|
| C0 | none of the below | **high** | private/internal symbol, no hazards, resolved graph — the safe-to-delete core case |
| C1 | finding is in a project that loaded with `projectsFailed`/restore failure affecting it, OR `unresolvedTypeReferences > 0` touched this project | **low** | the graph under it is untrustworthy; §3.8 says don't act |
| C2 | any hazard in `{ publicApi, serializationShaped, configBoundType, diPluginShaped }` present | **low** | these are the classic false-positive shapes; never auto-delete |
| C3 | `remediation == "removeProjectReference"` (or `removePackageReference`, WS3) | **medium** | conservative by construction (invariant #8) but load-bearing refs exist (transitive restore, `InternalsVisibleTo`); worth a glance |
| C4 | `remediation == "deleteCodeAndTests"` (WS7 `onlyUsedByTests`) | **medium** | correct only if production-mode classification is right; multi-file deletion; human should confirm the referrer set |
| C5 | `hazard == entryPointShaped` (name/attribute *near-misses* a configured entry-point convention but didn't match) | **medium** | plausible reflection/framework use the walker couldn't see |

**Recommended autonomy line:** **agents may auto-delete `high` only.** `medium` = propose in the PR for
human review; `low` = surface, never touch. WS8d (AGENTS.md) encodes exactly this.

### 4.2 Hazard set (closed enumeration, proposed)

`hazards[]` is additive and independent of confidence (though several hazards demote confidence per §4.1).

| Hazard | Set when | Source signal |
|--------|----------|---------------|
| `publicApi` | symbol is `public`/`protected` **and** the run did not already treat it as used (i.e. it's still flagged) | `accessibility` + not covered by `roots.treatAllPublicAsUsed`/`publicApiProjects` |
| `serializationShaped` | symbol carries a serialization attribute (`[JsonProperty]`, `[DataMember]`, `[XmlElement]`, `[ProtoMember]`, …) or its declaring type does | attribute scan on the declaration/containing type |
| `configBoundType` | symbol/type name or attribute matches options-binding shapes (`*Options`, `*Settings`, `[BindProperties]`, records bound via `IConfiguration`) | name + attribute heuristic |
| `diPluginShaped` | a DI/scanning plugin *touched* this symbol's type but did not produce a keep-alive edge (near-miss) | plugin contribution telemetry (§5.2) |
| `entryPointShaped` | name/attribute is a near-miss of a configured `entryPoints.*` convention | compare against effective `entryPoints` config |
| `partialDeletionUnit` | `span` alone won't compile-clean the reference (e.g. `removeFromInterface`, `deleteCodeAndTests`) | remediation kind |

**Important:** hazards are *advisory annotations on findings that already exist*. A hazard NEVER causes a
new finding to be emitted, and the absence of a hazard NEVER un-suppresses one. If we cannot cheaply and
reliably detect a hazard, we omit it (a missing hazard is a false negative on the *annotation*, which is
the safe direction — it lands the finding at higher confidence, so we must be **conservative**: only set
`high` when we're confident *no* hazard applies, i.e. the demotion rules must be sound, not exhaustive).
This tension is Open Question §7 Q1 — **the item most needing sign-off.**

---

## 5. CLI surface, config schema, SARIF, console

### 5.1 New flags

- `--why <symbol-or-id>` — provenance report to stdout, **prose + `file:line`, never raw graph keys**.
  - Argument accepts a finding `id`, or a symbol display string (or unambiguous suffix).
  - **Flagged (dead) symbol:** report "no incoming edges from any root" plus, if the symbol has *incoming
    edges that are themselves all dead*, list those dead referrers as `file:line — <display name>` (this
    is why it's dead: only dead code points at it). Battery **L5** (flagged → "no incoming edges" report).
  - **Alive symbol:** the **shortest root→symbol path**, each hop rendered as
    `RootName (file:line) → … → Symbol (file:line)` in display names. Battery **L5** (alive → root path).
  - Exit **0** for both (it's a query, not a gate).
- `--print-config` — the **effective merged config** (file over defaults) as JSON to stdout, exit 0.
  Battery **L6**. Reuses `KnipConfig.JsonOptions`. Emits the same unknown-key warnings (§5.3) to stderr.

### 5.2 `--why` provenance & the two-pass memory gate (WS8c)

`GraphState.Edges` today stores forward edges (source→targets); shortest-path and incoming-edge reports
need reverse reachability and per-edge *origin* (file:line of the reference site), which we do **not**
retain today. Per the runbook: **gate the extra provenance behind the flag.** When `--why` is present,
`DeadCodeAnalyzer` runs a **second pass** (or retains reverse edges + one representative source location
per edge) so the default run keeps its current memory profile. `--why` is CLI-invoked but calls a
`Knip.Core` provenance API (invariant #9 — core stays CLI-free; the *rendering* is CLI). The
`diPluginShaped` hazard (§4.2) reuses the same plugin-contribution telemetry.

### 5.3 Unknown-key warnings — generalize `ValidatePlugins` to ALL of knip.json

Today `KnipConfig.ValidatePlugins()` warns on unknown `plugins.*` ids/keys. Generalize the same pattern to
**every** knip.json object: unknown top-level keys and unknown nested keys each emit **one** warning naming
the key path (e.g. `unknown key 'roots.treatAllPubic' in knip.json — did you mean 'treatAllPublicAsUsed'?`).
Analysis proceeds; exit code unchanged. Battery **L7** (unknown top-level + unknown nested → one warning
each). Implementation: bind knip.json to a `JsonDocument` alongside the typed model and diff the key tree
against the known schema (the same schema published in §5.4), rather than hand-maintaining a key list.

### 5.4 Published JSON Schema for knip.json (in-repo)

Ship `schema/knip.schema.json` (JSON Schema draft 2020-12) describing the full `KnipConfig` surface. The
annotated example `knip.json` gains a `"$schema": "./schema/knip.schema.json"` (or a raw-GitHub URL) line
so editors give completion + validation. The §5.3 unknown-key check validates against this same schema
(single source of truth). Battery **L1** references an *output-format* schema for JSON v2; we ship **two**
schemas — `schema/knip.schema.json` (config input) and `schema/knip-output.schema.json` (JSON v2 output) —
and L1 validates the emitted document against the latter.

### 5.5 SARIF — unchanged shape, new fields mapped into existing slots

SARIF stays SARIF 2.1.0. We only *populate existing slots* with the new data:

- `result.partialFingerprints` = `{ "knipId/v1": "<the stable id §3.2>" }` — stable across runs so
  code-scanning platforms dedupe/track a finding across commits.
- `result.rank` or a `result.properties.confidence` = the confidence (SARIF has no first-class confidence;
  `properties` is the idiomatic bag). Recommendation: `properties.confidence` + `properties.hazards`.
- `result.properties.remediation` = the remediation verb.
- The `span` maps to the existing `region` (`startLine`/`startColumn` … `endLine`/`endColumn`).

No new SARIF top-level structures; consumers that ignore `properties` are unaffected.

### 5.6 Console — human-first, unchanged in shape

The console reporter keeps its grouped-by-project layout. Only additive touches (subject to taste, not a
breaking change): show a one-glyph confidence marker and a short hazard tag per line, and print
`degraded` prominently at the top when true. No structural change; console is not the API.

---

## 6. Sub-tasks & merge coordination

| Task | Deliverable | Lane |
|------|-------------|------|
| **WS8b** | JSON v2 (`formatVersion`, `reliability`, `summary`, enriched `findings`) + the `id`/`span`/`confidence`/`hazards` computation + both JSON Schemas + generalized unknown-key warnings. **Blocked on §4 (L9) sign-off.** | reporting |
| **WS8c** | `--why` + `--print-config`; extra edge/provenance memory gated behind `--why` (two-pass OK, §5.2). | CLI |
| **WS8d** | `AGENTS.md`: the canonical recipe — run → check `reliability.degraded` → triage by confidence (auto-delete `high` only) → delete by `span` → build + tests → re-run asserting **no new LIVE-code flags** (deleting dead code legitimately uncovers newly-dead symbols; do NOT assert identical output) → baseline/ignore the remainder with reasons. Exit-code table; one full JSON v2 example; README links it. | docs |

**Merge-coordination hazard:** WS8b changes `Finding` (adds `Confidence`, `Hazards`, `Remediation`, `Span`)
and touches `BuildFindings`/`ToFinding`/`BuildProjectReferenceFindings`. **The analyzer lane (WS3/WS7/
WS-enum) also touches `Finding` and `BuildFindings`.** Coordinate: land the `Finding` record extension
(new fields, defaulted) **first and small** so the analyzer lane rebases onto a stable shape; enrichment
logic (confidence/hazard/span computation) lands as a separate follow-up commit. Reporters read the new
fields; they don't compute them.

---

## 7. Open questions for the human (sign-off)

- **Q1 — the §4 L9 table itself (BLOCKS WS8b).** Are the confidence rules C0–C5, the hazard set, and the
  **"auto-delete `high` only"** autonomy line correct? Specifically: is `publicApi` always `low` (C2), or
  should a `public` symbol in a non-`publicApiProjects` app project be `medium`? This is the exact line
  where an agent is trusted to act unattended — it needs your eyes.
- **Q2 — hazard detection cost vs. soundness.** §4.2 hazards are heuristic. Do you want us to *ship a
  hazard only when detection is cheap and sound* (missing hazards leave findings at `high`), or to be
  **maximally conservative** and default anything with attributes/public surface to `medium` until proven
  clean? (Recommend the conservative default; costs some `high`→`medium` noise, buys safety.)
- **Q3 — `id` width.** 40 bits (10 hex) enough, or go 64 bits (16 hex)? (Recommend 40; §3.2.)
- **Q4 — does `productionModeWarnings` set `degraded`?** Proposed: **no** (it changes finding *meaning*,
  not graph trust). Confirm, since it gates autonomy on WS7 findings.
- **Q5 — `removeFromInterface` (WS-enum).** Deleting an unused interface member is a multi-file edit
  (member + all impls). Ship it as `medium` + `partialDeletionUnit` hazard with impl `file:line`s in
  `details`, and let the agent do the multi-file edit — or hold WS-enum out of the autonomous set entirely?
- **Q6 — SARIF confidence slot.** `properties.confidence` (recommended) vs. overloading `result.rank`?

---

## Appendix A — full JSON v2 example (small mixed fixture)

Fixture: `Acme.Api` (app) references `Acme.Legacy` (unused ref) and `Acme.Core`; `Acme.Api.Tests`.
Findings: one dead internal method (high), one dead `public` DTO type shaped by serialization (low),
one unused project reference (medium), one production method used only by a test (WS7, medium).

```json
{
  "formatVersion": 2,
  "tool": { "name": "Knip.NET", "version": "0.1.0" },
  "run": {
    "target": "Acme.sln",
    "projectsAnalyzed": 3,
    "symbolsAnalyzed": 812,
    "roots": 27,
    "elapsedSeconds": 1.84
  },
  "reliability": {
    "degraded": false,
    "projectsLoaded": 3,
    "projectsFailed": 0,
    "restoreFailures": [],
    "loadDiagnostics": [],
    "unresolvedTypeReferences": 0,
    "productionModeWarnings": [],
    "testProjectClassification": [
      { "project": "Acme.Api",       "kind": "production", "signal": "default" },
      { "project": "Acme.Core",      "kind": "production", "signal": "default" },
      { "project": "Acme.Api.Tests", "kind": "test",       "signal": "referencedAssembly:xunit.core" }
    ]
  },
  "summary": {
    "total": 4,
    "byConfidence": { "high": 1, "medium": 2, "low": 1 },
    "byKind": {
      "unusedMethod": 1,
      "unusedType": 1,
      "unusedProjectReference": 1,
      "onlyUsedByTests": 1
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
        "byKind": { "onlyUsedByTests": 1 },
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
      "details": {}
    },
    {
      "id": "k1_0f4ab6c273",
      "kind": "onlyUsedByTests",
      "symbol": "Acme.Core.Pricing.PriceRounder.RoundHalfEven(decimal)",
      "symbolKind": "method",
      "accessibility": "public",
      "project": "Acme.Core",
      "confidence": "medium",
      "hazards": ["publicApi", "partialDeletionUnit"],
      "remediation": "deleteCodeAndTests",
      "location": { "file": "src/Acme.Core/Pricing/PriceRounder.cs", "line": 29, "column": 19 },
      "span": {
        "file": "src/Acme.Core/Pricing/PriceRounder.cs",
        "start": { "line": 26, "column": 5 },
        "end":   { "line": 34, "column": 6 }
      },
      "referencedProject": null,
      "details": {
        "testReferrers": [
          {
            "symbol": "Acme.Core.Tests.PriceRounderTests.RoundsHalfEven()",
            "file": "tests/Acme.Core.Tests/PriceRounderTests.cs",
            "line": 17
          }
        ]
      }
    }
  ]
}
```

Note in the example how the two `public` findings are demoted (C2), the project reference and the
test-only method sit at `medium` (C3/C4), and only the private-shaped internal method is `high` — the
single finding an agent may delete unattended. That is the L9 line made concrete.
```

---

## Appendix B — battery rows this design targets

L1 (output schema validates), L2 (stable ids + order), L3 (`degraded` true/false), L4 (span is a complete
deletion unit), L5 (`--why` flagged/alive), L6 (`--print-config`), L7 (unknown top-level + nested key
warnings), L8 (summary counts == findings), **L9 (this table — sign-off blocker for WS8b).**

---

## Amendment — 2026-07-15 (post sign-off; supersedes the confidence table above)

L9 SIGNED OFF **with adjustments** (Åsmund). The confidence/hazard model is the implementation of the
REVISED invariant #8 ("recall over silence — but hazards are sacred", §3.1). Apply together.

- **C1 per-project:** a project-load/restore failure demotes only that project's findings → `low`;
  solution-global degradation demotes all. `reliability` attributes failures per project.
- **C2 publicApi is config-sensitive:** `publicApiProjects` OR `treatAllPublicAsUsed` set → `medium`;
  neither set → `low`. Other C2 hazards (`serializationShaped`, `configBoundType`, `diPluginShaped`) → `low`.
- **C3** project/package-ref → `medium`. **C4** `deleteCodeAndTests` → `medium`.
- **C5 DROPPED from v1** (entry-point near-miss): ships only with an enumerated definition + fixtures,
  or added later additively. No vibe-based demotions.
- **New hazard `internalsVisibleTo`:** `[InternalsVisibleTo]` naming a non-solution assembly → that
  project's internal findings → `low`.
- **Autonomy (option 1) with HARD precondition:** `high` deletes into the PR ONLY via the full verify
  loop (`reliability.degraded==false` for the project → delete by span → build + full tests green →
  re-run knip, no new live-code flags); any step fails → `medium` handling. `medium` = propose in PR;
  `low` = surface only. Auto-delete without the loop is a protocol violation.
- **`rootCause` field (additive):** per-finding optional `rootCause` = finding id of the nearest dead
  symbol keeping this one dead (`null` when directly unreferenced). Outermost-first deletion + cascade
  structure; `--why` reuses it. Battery row L10. Reporting stays TRANSITIVE (full unreachable set;
  A4/A6) — iteration is a workflow, not a reporting mode.
- **Change control:** demotion rules pinned by Appendix-L fixtures (L10–L17); rule additions additive
  w/ fixture, rule changes escalate. **WS8d (AGENTS.md) requires a real dogfood-run review before done.**
