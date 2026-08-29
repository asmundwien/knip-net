# RB-02 unused `appsettings.json` key detection

**Status.** Proposed. No contract tests or implementation have landed.

**Audience:** an orchestrator agent executing this runbook with implementer and reviewer subagents.
RB-01 Task B is the prerequisite because this detector reuses its config-bound type data. Task B has
shipped. When this runbook and `RUNBOOK.md` disagree, follow `RUNBOOK.md` and escalate.

**Origin:** a supervised field run (2026-07-16) deleted config-constant classes and a
config-consuming utility from a real backend; the corresponding sections in `appsettings.json` and
`appsettings.Development.json` became dead but nothing flagged them. Dead config is the natural
companion to dead code: after a dead-code sweep, orphaned config keys are the most common residue.
Per `RUNBOOK.md` §7, no source or analysis output from the field solution appears here; all shapes
below are synthetic.

**Pre-approval (Åsmund, 2026-07-16):** the new finding kind, the new `knip.json` config surface,
and the schema changes scoped in this runbook are owner-approved. Anything beyond this scope —
especially widening to non-JSON config formats — escalates per RUNBOOK.md §6.

---

## Ground rules

Identical to RB-01: red test first; every alive assertion has a dead sibling; never edit existing
assertions/fixtures; one branch per task; full gate (`build -warnaserror` + full battery + eyeball
CLI run) before merge; independent reviewer subagent checks invariants by number; two consecutive
gate failures → escalate.

---

## What is being built

A new detector: **config keys declared in `appsettings*.json` that no code can read.**

- New finding kind: `unusedConfigKey`.
- Per-key findings, keyed by the flattened section path (`"Portal:EpjKontekst:ClientId"` style).
- `location` = the file + line of the key in the JSON file; `span` = the deletion unit (the
  property including its value object and trailing comma handling) **only when** the tool can
  produce it safely — otherwise `span: null` (surfaced, never auto-deleted; protocol §4).
- **Confidence: `low`, always, in v1.** Config is read by frameworks, external libraries, other
  services sharing the file, and infrastructure (Azure App Service settings overlays) — the
  verify loop (build + tests) is structurally blind to almost all config usage. `low` = surface
  only is the honest tier (invariant #8: emit with honest confidence, never suppress). Revisiting
  the tier is a future human decision, not something this runbook licenses.
- New hazard is NOT needed — confidence low already communicates the risk; hazards enumerate
  *reasons*, and `configBoundType` (RB-01 Task B) covers the code side.

## Why this is architecturally different from everything shipped

The engine analyzes C# syntax trees. Config files are **not** compilation inputs —
`MSBuildWorkspace` may expose them as `AdditionalDocuments` only if the project opts in, which
real projects generally don't. **Discovery task 0 must settle how config files are found** before
any contract is written.

---

## Task 0 — Discovery spike (timeboxed, throwaway branch)

Answer with a short written summary (no production code):

1. Does `MSBuildWorkspace` surface `appsettings*.json` for a default ASP.NET Core project
   (`Project.AdditionalDocuments`)? Check on a fixture, not a real solution.
2. If not (expected): the detector globs the **project directory** of each project whose items or
   directory contain `appsettings.json` (root config file), collecting `appsettings*.json`
   siblings. Confirm this respects invariant #9 (`Knip.Core` stays CLI-free and OS-agnostic —
   plain `System.IO` + the existing `Configuration/Glob.cs` is fine; no MSBuild evaluation needed).
3. Where does key-usage collection hook in? Expected answer: `ReferenceWalker` already visits every
   invocation; confirm it can cheaply record (a) string literals used in config-read positions and
   (b) `const string` field values (see Task 2 — const resolution is mandatory).

Deliverable: a `runbooks/RB-02-discovery.md` note (committed) stating the chosen mechanism.
If both mechanisms fail (config files unreachable without MSBuild evaluation), STOP and escalate.

---

## Task 1 — Contract: fixture + red tests (CatN)

New category `CatN` in `tests/fixtures/`, an ASP.NET-Core-shaped console/web project with an
`appsettings.json`, an `appsettings.Development.json`, and code exercising every read pattern.
Contract rows (each with its used/unused sibling; write ALL tests red-first):

| Row | appsettings shape | Code shape | Expected |
|---|---|---|---|
| N1 | `"Direct": { "Used": 1, "Dead": 2 }` | `config["Direct:Used"]` | `Direct:Dead` flagged, `Direct:Used` not |
| N2 | `"Sect": { "Used": 1, "Dead": 2 }` | `config.GetSection("Sect")["Used"]` — relative read under a section | `Sect:Dead` flagged, `Sect:Used` not |
| N3 | `"Konst": { "A": 1 }` + dead sibling | `const string KonstA = "Konst:A";` … `config[KonstA]` | const-resolved usage keeps `Konst:A` alive |
| N4 | `"Bound": { "Name": "x", "Extra": "y" }` | `config.GetSection("Bound").Get<BoundOptions>()` where `BoundOptions` has `Name` only | `Bound:Name` alive via binding; `Bound:Extra` flagged |
| N5 | `"Logging"`, `"AllowedHosts"`, `"ConnectionStrings"`, `"Kestrel"` present, never read in code | — | NOT flagged (built-in framework allowlist) |
| N6 | key only in `appsettings.Development.json`, read in code | `config["DevOnly:Flag"]` | not flagged; and a dev-only DEAD key IS flagged **once** (base + overlay files merge into one logical key set — no duplicate findings for a key present in both files) |
| N7 | `"Ign": { "X": 1 }` never read, but `knip.json` has `config.ignoreKeys: ["Ign:*"]` | — | NOT flagged (user allowlist, glob-style) |
| N8 | key read via string concatenation / interpolation (`config[$"Dyn:{name}"]`) | — | the WHOLE `Dyn` section is treated opaque-alive (dynamic access defeats analysis; prefer false negative), pinned by a test |
| N9 | every emitted finding | — | `kind == unusedConfigKey`, `confidence == low`, correct file+line `location` |

N4 depends on RB-01 Task B's config-binding detection (`Get<T>`/`Bind`/`Configure<T>` call
recognition) — reuse that collection, do not re-implement.

Options-pattern nuance for N4: `Configure<T>(section)` binds `T`'s public settable properties;
the alive key set under the bound section is the property names (recursively for nested POCOs).
Respect `[ConfigurationKeyName("...")]` if trivially available; otherwise property-name matching is
the v1 contract and `[ConfigurationKeyName]` becomes a documented gap (add a SKIPPED test for it,
CatH-style, so the gap is visible — skips are never deleted, only promoted).

## Task 2 — Implementation

Layering (invariant #9 — analysis stays OS/CLI-agnostic):

1. **Key collection** (`src/Knip.Core/Analysis/` — new `ConfigKeyReader.cs`): per project, locate
   `appsettings*.json` (per Task 0's mechanism), flatten to `Section:Sub:Key` paths with file+line
   per key (a minimal JSON walker with line tracking; `System.Text.Json` `JsonDocument` does not
   give line numbers — either walk with `Utf8JsonReader` tracking positions or a small hand-rolled
   line-aware pass. No new package dependencies without escalation).
2. **Usage collection** (`ReferenceWalker`): record config-read string arguments — indexer on
   `IConfiguration`/`IConfigurationSection`, `GetSection(...)`, `GetValue<T>(...)`,
   `GetConnectionString(...)` (maps to `ConnectionStrings:<name>`) — resolving arguments that are
   (a) literals, (b) `const string` fields/locals (resolve the constant value via the semantic
   model — this is mandatory: real codebases centralize keys in constant classes), (c) `nameof`.
   Non-constant arguments mark the receiving section opaque-alive (N8). Track section context so
   relative reads (N2) compose with their parent path.
3. **Binding-derived keys**: from RB-01 Task B's collected bound types, project property names to
   key paths under the bound section (N4).
4. **Matching + findings** (`DeadCodeAnalyzer`, alongside the other Build*Findings): declared keys
   minus (directly-read keys ∪ binding-derived keys ∪ opaque-alive sections ∪ built-in allowlist ∪
   `config.ignoreKeys`). A parent section key is alive if any child is alive. Emit findings for
   leaf keys only (outermost-only inverted: flag the *deepest* dead node whose entire subtree is
   dead → report the subtree root — mirror the "only the outermost dead symbol" philosophy,
   invariant #7: one finding for a fully-dead section, not one per leaf).
5. **Built-in allowlist** (in code, documented in README): `Logging`, `AllowedHosts`,
   `ConnectionStrings` (only when `GetConnectionString`/EF patterns absent — v1: always allowlist),
   `Kestrel`, `ApplicationInsights`, `Serilog`, `NLog`, `AzureAd`, `DetailedErrors`,
   `https_port`, `urls`. Extensible only via `config.ignoreKeys`, not auto-widened.

Config surface (`knip.json` — user-facing API, pre-approved scope):

```jsonc
"config": {
  "enabled": true,            // v1 default ON (findings are low/surface-only, cost is low)
  "files": ["appsettings*.json"],  // glob, relative to each project dir
  "ignoreKeys": []            // glob-style key paths, e.g. "FeatureFlags:*"
}
```

Update in the SAME commits as behavior: `schemas/knip.config.schema.json`,
`schemas/knip.output.schema.json` (new kind), README (new section: what is detected, the
allowlist, the honest limits), `knip.json` example at repo root,
`src/Knip.Cli/Resources/AgentInstructions.md` (one line: `unusedConfigKey` findings are always
`low`/surface-only; never delete config autonomously).

## Task 3 — Reporting polish

- Console reporter: group `unusedConfigKey` findings per file, print the flattened key path.
- `--why Portal:EpjKontekst:ClientId` support is OUT OF SCOPE v1 (WhyService is symbol-keyed);
  note it in README as a known gap.

---

## Hard boundaries (escalate rather than cross)

- **No non-JSON formats** in v1: no `web.config`/`app.config` (that is the H9 moat row), no YAML,
  no `launchSettings.json`, no environment variables, no KeyVault references.
- **No confidence above `low`** for any config finding, regardless of how certain the analysis
  looks.
- Never let config analysis affect the *code* graph (no roots, no edges from config into the
  symbol graph in v1 — the two analyses stay independent; coupling them is a future design
  decision).
- If restore/load is degraded (invariant #6 signals), config findings demote with everything else
  (C1 in `ConfidenceModel` — they're already `low`, so this is a no-op today; add a comment, not a
  mechanism).

## Optional validation (owner-run, not committable)

Åsmund re-runs against the field solution and confirms the orphaned cookie-configuration sections
in its base + Development appsettings are flagged, and that framework sections (`Logging`,
`ApplicationInsights`, `AllowedHosts`) are not. Results stay out of this repo.
