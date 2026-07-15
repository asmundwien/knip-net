# Knip.NET — Maintainer Runbook

This is the operating guide for maintaining Knip.NET: what the tool is, what must never break,
how to verify a change, and when to stop and ask the human (Åsmund). It describes the current
product; it is not a design history.

## 1. What it is

Knip.NET is a free, Roslyn-based, **solution-wide dead-code finder** for .NET
([Knip](https://knip.dev) for the .NET world), shipped as a `dotnet` global tool
(`dotnet-knip`), configured via `knip.json`, with console/JSON/SARIF output and CI exit codes.

**Why it exists:** the owning org (Helsedirektoratet) has a large portfolio spread across many
runtimes, down to .NET Framework 4.8. The strategic use case is **deleting dead code before
migrating**, shrinking the upgrade surface. The oldest analyzed *target framework* the tool must
support is **.NET Framework 4.8** (the final .NET Framework version); older 4.x targets usually
work for free because Roslyn analysis is target-framework-agnostic.

Difficulty is split by *project format*, not framework version:

| Analyzed project | Status |
|---|---|
| SDK-style `.csproj`, any target (net10, net48, netstandard2.0…) | Supported |
| Legacy-format `.csproj` + `packages.config` | Supported via the net472 build (Windows + VS Build Tools to run) |

## 2. Architecture map

```
Knip.slnx
knip.json                          annotated example config (schema-by-example)
schemas/                           knip.config.schema.json, knip.output.schema.json
src/Knip.Core/                     engine (no CLI concerns, no MSBuildLocator)
  KnipEngine.cs                    open .sln/.slnx/.csproj via MSBuildWorkspace → run analyzer → grade confidence
  Analysis/DeadCodeAnalyzer.cs     orchestrates: per-project walk → polymorphism edges → BFS → findings
  Analysis/ReferenceWalker.cs      per-syntax-tree: declarations, "uses" edges, entry-point roots
  Analysis/GraphState.cs           shared graph: Declared / Edges / Roots, string-keyed
  Analysis/SymbolId.cs             symbol → assembly-qualified documentation-comment-ID key
  Analysis/Plugins/                config-gated contribution plugins (reflection, scanningDi, aspnetcore, …)
  Configuration/KnipConfig.cs      knip.json model + discovery + key validation
  Model/, Reporting/               Finding, AnalysisResult, ConfidenceModel; console/json/sarif reporters
src/Knip.Cli/
  Program.cs                       MSBuildLocator.RegisterDefaults() FIRST, then calls Runner
  Runner.cs                        arg parsing → agent surfaces → config → engine → reporter → exit code
  CliOptions.cs                    run options + `init` verb + `--agent-instructions`
  AgentInstructionsProvider.cs     single source of truth for the agent protocol (embedded resource)
  InitCommand.cs                   `init --agent` bootstrap
  Resources/AgentInstructions.md   the canonical agent-consumer protocol (embedded)
```

Pipeline: **load** (MSBuildWorkspace) → **walk** every syntax tree building a uses-graph (signature
types, attributes, body references, `new`, generic args) → **plugins contribute** extra roots/edges
→ **seed roots** from configurable entry points → **mark-and-sweep BFS** → **grade confidence** →
report. Exit codes: `0` clean, `1` findings (CI gate), `2` error.

## 3. Invariants — never let a change break these

Each encodes a real bug already fixed or a human decision. Reject any diff that violates one, even
if its tests pass.

1. **Graph keys are assembly-qualified documentation-comment IDs, never symbol references.** Roslyn
   does not give reference-equal symbols for the same method seen from source vs. from a referenced
   assembly; `SymbolEqualityComparer` across projects silently drops every cross-project edge. All
   graph state is string-keyed via `SymbolId.For(...)`. Key format is `DefiningAssembly::docId`
   (e.g. `MyLib::M:Ns.Type.Method(System.String)`) — the bare doc-comment ID is not enough (two
   projects can declare an identical namespace+type+signature and merge into one node). The
   qualifier must be `symbol.OriginalDefinition.ContainingAssembly` (the *defining* assembly, not
   the referencing compilation). The assembly prefix is load-bearing; do not simplify it away.

2. **`MSBuildLocator.RegisterDefaults()` runs before any Roslyn-MSBuild type is touched.** That is
   why `Program.cs` is tiny and all workspace usage hides behind the `Runner.RunAsync` method
   boundary (JIT loads types per-method). Do not move workspace code into `Program.cs` or add
   Roslyn-MSBuild `using`s there.

3. **Failed overload resolution keeps ALL candidate symbols alive** (`ReferenceWalker.RecordReference`).
   Guessing one candidate produces false positives.

4. **An entry-point member roots its containing-type chain** (`EvaluateRoots`). A `[Fact]` method
   keeps its test class alive. Rooting an instance entry-point member also roots the type's instance
   constructors (the framework constructs the type to invoke it).

5. **Edges only target solution assemblies** (`AddEdge` checks `_solutionAssemblies`); BCL/NuGet
   symbols are not graph nodes. Per-project *used external assembly* names are recorded (a string)
   before the drop, for unused-package-reference detection — the external symbol is still not a node.

6. **Unresolved-type detection stays.** Missing packages (e.g. unauthenticated feed) make types
   error-types and degrade analysis; the tool counts `TypeKind.Error` references and warns. Never
   remove or bypass this warning path.

7. **Reporting noise rules:** only the outermost dead symbol (skip members of dead types); never
   report constructors/static ctors/finalizers, overrides, or interface implementations;
   overrides/interface impls stay alive when their abstraction is used (`AddPolymorphismEdges`).

8. **Recall over silence — but hazards are sacred.** The output is a *suggestion set* consumed
   through a mandatory verify loop (delete → build → full tests → re-run) and downstream CI; a false
   positive that fails that loop is binned at near-zero cost. So: never silently suppress a finding
   class to avoid false positives — emit it with an honest `confidence` tier and `hazards[]`. The
   residual UNACCEPTABLE risk is the finding whose deletion survives build and tests but breaks at
   RUNTIME — reflection, DI-by-name, serialization, config/markup-bound usage (the hazard classes).
   Those must always demote confidence; the plugins that kill them stay default-on; and a heuristic
   change that could create an unflagged runtime-only false positive needs a fixture proving it
   doesn't. This is not a license to loosen existing FN-preferring core rules (invariant #3, the
   collision split in #1) in bulk — relaxing any is per-rule work with its own fixture.

9. **`Knip.Core` stays CLI-free and locator-free.** All MSBuild registration lives in the CLI. Keep
   analysis code (walker, analyzer, graph) free of anything version- or OS-specific — that is what
   keeps the net472/legacy path cheap.

## 4. Environment constraints

- Building requires the **.NET 10 SDK** (`net10.0`, Microsoft.CodeAnalysis 5.6.0). Verify with
  `dotnet --version` before dispatching build tasks.
- **Private feed:** Hdir solutions restore from `pkgs.dev.azure.com/helsedir/...`, which returns
  **401 in unauthenticated sandboxes**. Analysis of real Hdir solutions is only accurate where
  `dotnet restore` succeeds. Validate on the **self-contained fixtures**, not on Hdir solutions,
  unless the environment has feed auth. Fixtures are synthetic only — never commit Hdir source or
  analysis output of Hdir solutions.
- **Restore first.** Like Knip needs `node_modules`, Knip.NET needs a restored, resolvable solution;
  missing packages become error-types and degrade analysis (invariant #6). Always `dotnet restore`
  (with feeds authenticated) before analyzing a real solution.
- The **net472** build exists so the engine can run on full-framework MSBuild (VS Build Tools) to
  evaluate legacy `.csproj` + `packages.config` solutions. It compiles cross-platform but only
  *runs* on Windows; the legacy fixture's end-to-end verification is Windows-only — escalate for a
  Windows runner.
- **Scale is not yet validated at portfolio size.** Whole-solution `MSBuildWorkspace` load is the
  main cost; it is fine for the solutions tested (~2–20 s), but very large solutions may stress load
  time, memory, and partial-load handling. On the first large-solution run, capture wall time, peak
  memory, and workspace diagnostics; if load fails or runtime is unusable, escalate — do not
  silently shrink the analysis scope to make it pass.

## 5. Verification gate — run after every change, before accepting

```bash
dotnet build Knip.slnx -warnaserror                                   # zero-warnings policy, both TFMs
dotnet test tests/Knip.Core.Tests/Knip.Core.Tests.csproj --no-build   # full battery
dotnet run --project src/Knip.Cli -- tests/fixtures/<relevant>/Fixture.slnx   # eyeball console output
```

The battery is the feature contract: `tests/Knip.Core.Tests/` holds one test per scenario over
self-contained fixtures in `tests/fixtures/`, each asserting the exact finding set (what IS flagged
and what is NOT). Every "alive" assertion ships with a dead sibling (or red-flip evidence) so the
fixture cannot pass vacuously. A skipped test marks a deferred feature/plugin gap — skips are
visible, promoted when the feature lands, and never deleted.

Also check the diff itself: no `bin/`/`obj/` files; no deleted invariants; no new `#if` outside the
CLI/loading layer; README/knip.json/schemas updated when behavior or config changed.

**Review rules (mechanical — do not substitute judgment):**
1. A change violating a §3 invariant is rejected by number.
2. Any heuristic change that could create an unflagged runtime-only false positive (reflection,
   DI-by-name, serialization, config/markup-bound) must add a fixture proving it doesn't, in the
   same diff. A new confidence/hazard demotion rule ships with a fixture asserting the tier.
3. Any edit to an existing test assertion or fixture escalates to the human — do not evaluate
   whether the weakening "seems reasonable." Promoting a skipped test is the exception (removing the
   skip only).
4. Changes must land in the right layer (analysis vs. loading vs. CLI — invariant #9).

**Real-solution deletion gate — before recommending any deletion on a real codebase:**
1. Restore-warning check clean (invariant #6 shows zero unresolved-type warnings).
2. Apply the deletions on a branch of the *target* solution; its build and its own tests must stay
   green. This is the strongest automated check — but it cannot catch runtime-only usage
   (reflection/DI/serializer/config-bound), which compiles and tests green then breaks at runtime.
3. A human reviews every finding in runtime-only-shaped code plus a random sample of the rest.

## 6. When to escalate to the human

- A §3 invariant genuinely needs changing (don't change it yourself).
- A public config-schema change (`knip.json` keys are user-facing API) or a new plugin seam shape.
- A change to an existing test assertion/fixture, or a new confidence/hazard demotion *rule*.
- The legacy path needs a Windows/VS Build Tools run, or real-solution validation needs feed auth.
- Two consecutive attempts at a task fail the verification gate — stop, summarize, ask.
- Anything requires publishing (feed, marketplace) — publishing is always human-approved.

## 7. Git hygiene

- `main` must always pass the verification gate; never commit red tests or non-compiling code, never
  force-push or rewrite `main`. One branch per task; merge only after the gate passes.
- One logical change per commit; imperative subject ≤ 72 chars. Behavior changes, their tests, and
  the docs they affect travel in the same commit. Agent-authored commits carry a `Co-Authored-By:`
  trailer.
- Never commit `bin/`/`obj/`/IDE state, credentials or `NuGet.config` with feed secrets, Hdir source
  or Hdir analysis output. Skipped battery tests may be promoted, never deleted.

## 8. Current state

Shipped and covered by the battery (**176 passing / 5 skipped**; the 5 skips are deferred
runtime-only-hazard plugins and detectors):

- **Dead-code detection** (flagship): solution-wide unused symbols via assembly-qualified doc-ID
  graph; cross-project identity, overload-candidate liveness, entry-point containing-type roots,
  polymorphism edges, outermost-only reporting.
- **Unused `<ProjectReference>`** and **unused `<PackageReference>`** detection (the latter maps
  assemblies→packages via `obj/project.assets.json`; build-only/analyzer packages are emitted at low
  confidence, not dropped).
- **Unused enum members** (member-level, outermost-only).
- **Production mode** (`--production`): two-color reachability flags production code reachable only
  via tests as `onlyUsedByTests` (`deleteCodeAndTests`), listing the referring test symbols.
- **JSON v2 output** + `reliability` block + `summary`, described by `schemas/knip.output.schema.json`;
  `knip.json` described by `schemas/knip.config.schema.json`.
- **Confidence/hazard model** (`ConfidenceModel`): start `high`, first-match demotion
  (per-project/global load failure → publicApi/config-sensitive → internalsVisibleTo → project/
  package-ref → deleteCodeAndTests); hazards advisory-only. The autonomy line: `high` delete (via
  verify loop) / `medium` propose / `low` surface.
- **`--why`** (trace one symbol) and **`--print-config`** (effective merged config); unknown-key
  warnings across all of `knip.json`.
- **Plugin seam** + built-in plugins: `reflection`, `scanningDi`, `aspnetcore` default-on;
  `blazorParameter`, `serialization` opt-in. Add-only (can keep code alive, never mark live code
  dead); unknown plugin ids / per-plugin keys warn visibly.
- **Agent bootstrap**: `dotnet-knip --agent-instructions` prints the canonical protocol;
  `dotnet-knip init --agent` writes `.knip/AGENTS.md` + `knip.json`. Both emit one embedded source
  of truth (`Resources/AgentInstructions.md`).
- **net472 multi-target** for legacy solutions (zero `#if` in source; Windows e2e of the legacy
  fixture still pending a runner).
- **CI** (build + test) and **global-tool packaging** (`Hdir.Knip` → `dotnet-knip`) verified
  locally; feed/marketplace publish is human-approved and not yet done.

Known gaps: the deferred runtime-only plugins/detectors (serialization/config/DI hazard *detection*
beyond what the current plugins cover); incremental/cached index and `--baseline` for very large or
brownfield solutions; portfolio-scale validation.
