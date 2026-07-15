# Knip.NET — Orchestration Runbook

This runbook is for an **orchestrator agent** coordinating implementation agents on Knip.NET.
It tells you what the project is, what must never break, how to slice work, how to verify it,
and when to stop and ask the human (Åsmund).

Read this whole file before dispatching any work. When delegating, paste the relevant
sections (Mission, Invariants, the work-stream card, Verification Gate) into the
implementation agent's prompt — do not assume it can see this file.

---

## 1. Mission

Knip.NET is a free, Roslyn-based, **solution-wide dead-code finder** for .NET
([Knip](https://knip.dev) for the .NET world), shipped as a `dotnet` global tool
(`dotnet-knip`), configured via `knip.json`, with console/JSON/SARIF output and CI exit codes.

**Why it exists:** the owning org (Helsedirektoratet) has a large portfolio spread across many
runtimes, down to .NET Framework. Upgrades are painful and get worse as code grows. The
strategic use case is **deleting dead code *before* migrating**, shrinking the upgrade surface.

**Support floor (decided):** .NET Framework **4.8** is the oldest *target framework* the tool
must analyze. 4.8/4.8.1 is the final .NET Framework version — nothing older needs explicit
support (though older 4.x targets will usually work for free, since Roslyn analysis is
target-framework-agnostic).

**Consequence for this repo:** difficulty is split by *project format*, not framework version:

| Analyzed project | Status |
|---|---|
| SDK-style `.csproj`, any target (net10, net48, netstandard2.0…) | Works with the current tool |
| Legacy-format `.csproj` + `packages.config` (typical real 4.8 code) | Needs work stream WS4 |

**Status:** working prototype. Dead-code detection is implemented and validated on a real
solution (`Hego.Common.sln`: 10 projects, ~1,100 symbols, ~2s, 7 hand-verified true positives).
There are **no automated tests yet** — that is deliberately the first work stream.

## 2. Architecture map

```
Knip.slnx
knip.json                          annotated example config (also the schema-by-example)
src/Knip.Core/                     engine (no CLI concerns, no MSBuildLocator)
  KnipEngine.cs                    open .sln/.slnx/.csproj via MSBuildWorkspace → run analyzer
  Analysis/DeadCodeAnalyzer.cs     orchestrates: per-project walk → polymorphism edges → BFS → findings
  Analysis/ReferenceWalker.cs      per-syntax-tree: declarations, "uses" edges, entry-point roots
  Analysis/GraphState.cs           shared graph: Declared / Edges / Roots, string-keyed
  Analysis/SymbolId.cs             symbol → documentation-comment-ID key
  Configuration/KnipConfig.cs      knip.json model + discovery (nearest file up the tree)
  Configuration/Glob.cs            ** / * / ? matching
  Model/, Reporting/               Finding, AnalysisResult; console/json/sarif reporters
src/Knip.Cli/
  Program.cs                       MSBuildLocator.RegisterDefaults() FIRST, then calls Runner
  Runner.cs                        arg parsing → config → engine → reporter → exit code
  CliOptions.cs                    -s/--solution -c/--config -f/--format -v --no-fail
```

Pipeline: **load** (MSBuildWorkspace) → **walk** every syntax tree building a uses-graph
(signature types, attributes, body references, `new`, generic args) → **seed roots** from
configurable entry points (`Main`, `[Fact]`/`[HttpGet]`, `*Controller`, `IHostedService`,
optional treat-public-as-used) → **mark-and-sweep BFS** → report unreachable symbols.
Exit codes: `0` clean, `1` findings (CI gate), `2` error.

## 3. Invariants — never let an implementation agent break these

Each of these encodes a bug that was already found and fixed, or a design decision the human
made. Reject any diff that violates one, even if its tests pass.

1. **Graph keys are assembly-qualified documentation-comment IDs, never symbol references.**
   Roslyn does NOT give reference-equal symbols for the same method seen from source vs. from
   a referenced assembly; `SymbolEqualityComparer` across projects silently drops every
   cross-project edge and defeats "solution-wide". All graph state is string-keyed via
   `SymbolId.For(...)` (`src/Knip.Core/Analysis/SymbolId.cs`). Any diff reintroducing
   symbol-keyed dictionaries/sets in `GraphState` or the walker is wrong.
   **Key format is `DefiningAssembly::docId`** (e.g. `MyLib::M:Ns.Type.Method(System.String)`).
   The bare doc-comment ID is NOT enough: two projects can declare an identical
   namespace+type+signature, whose doc-comment IDs are identical, and merge into one node —
   one project's use then keeps the other's dead copy alive (the B6 false negative, fixed
   2026-07-14). The qualifier MUST be `symbol.OriginalDefinition.ContainingAssembly` (the
   assembly that *defines* the symbol, identical from source and metadata viewpoints) — NOT the
   referencing compilation, or cross-project identity breaks (B1/B3 are the regression guard).
   Do NOT "simplify" the assembly prefix away: it is load-bearing, not decoration.

2. **`MSBuildLocator.RegisterDefaults()` runs before any Roslyn-MSBuild type is touched.**
   That is why `Program.cs` is 9 lines and all workspace usage hides behind the
   `Runner.RunAsync` method boundary (JIT loads types per-method). Do not move workspace code
   into `Program.cs` or add Roslyn-MSBuild `using`s there.

3. **Failed overload resolution keeps ALL candidate symbols alive**
   (`ReferenceWalker.RecordReference`). Guessing one candidate produced false positives.

4. **An entry-point member roots its containing-type chain** (`EvaluateRoots`). A `[Fact]`
   method keeps its test class alive.

5. **Edges only target solution assemblies** (`AddEdge` checks `_solutionAssemblies`);
   BCL/NuGet symbols are not graph nodes.

6. **Unresolved-type detection stays.** Missing packages (e.g. unauthenticated feed) make
   types error-types and degrade analysis; the tool counts `TypeKind.Error` references and
   warns. Never remove or bypass this warning path.

7. **Reporting noise rules:** only the outermost dead symbol (skip members of dead types);
   never report constructors/static ctors/finalizers, overrides, or interface implementations;
   overrides/interface impls stay *alive* when their abstraction is used (`AddPolymorphismEdges`).

8. **Recall over silence — but hazards are sacred.** (REVISED 2026-07-15; supersedes the original
   "prefer a false negative" rule.) The tool's output is a *suggestion set* consumed through a
   MANDATORY verify loop (delete → build → full tests → re-run) and downstream CI; a false positive
   that fails that loop is binned at near-zero cost. Therefore: **never silently suppress a finding
   class to avoid false positives — emit it, with an honest `confidence` tier and `hazards[]`.**
   The residual risk that remains UNACCEPTABLE is the finding whose deletion survives build and tests
   but breaks at RUNTIME — reflection, DI-by-name, serialization, config/markup-bound usage
   (**category H**). Those hazard classes must ALWAYS demote confidence; plugins killing them stay
   default-on; and a heuristic change that could create an **unflagged H-class false positive** still
   requires a fixture proving it doesn't. Noise suppression (§3.7 outermost-only, no
   ctors/overrides) is unchanged — that is noise policy, not FP-avoidance.
   (This does NOT license loosening existing FN-preferring CORE rules in bulk — invariant #3 overload
   candidates, B6-style conservatisms stay; relaxing any is per-rule backlog work with its own fixture
   + real-solution measurement.)

9. **`Knip.Core` stays CLI-free and locator-free**; all MSBuild *registration* lives in the CLI.
   Keep analysis code (walker, analyzer, graph) free of anything version- or OS-specific —
   that is what makes WS4 (net472) cheap.

## 4. Environment constraints

- Building this repo requires the **.NET 10 SDK** (`TargetFramework net10.0`,
  Microsoft.CodeAnalysis 5.6.0). Verify with `dotnet --version` before dispatching build tasks.
- **Private feed:** Hdir solutions restore from `pkgs.dev.azure.com/helsedir/...`, which
  returns **401 in unauthenticated sandboxes**. Analysis of real Hdir solutions is only
  accurate where `dotnet restore` succeeds. Implementation agents should validate on
  **self-contained fixtures** (WS1), not on Hdir solutions, unless the environment has feed auth.
- Real-solution smoke target when auth exists: `Hego.Common.sln`. The old "~7 findings"
  expectation PREDATES WS1b + B6 and is now STALE — WS1b closed 12 false-positive classes and
  B6 collision-splitting flags more, so the count may legitimately move. **Re-baseline the
  expected finding set on the first authenticated run; do not treat deviation from ~7 as a
  regression until re-baselined.**
- Scale/real-solution data point (Tjenesteportalen, authenticated run 2026-07-14):
  `Hdir.Selvbetjening.HelseaktorportalBackend.sln` — 10 projects, 2,853 symbols, 634 roots,
  ~3.6 s analysis, ~335 MB, restore clean, 313 findings. Three hand-verified production findings
  were all correct. Surfaced the MSTest/NUnit entry-point defaults gap (see §5 / ledger) and a
  strong WS7 signal (test roots keep production code alive in default mode — 151 production
  findings are a floor).
- WS4 (legacy projects) ultimately needs **Windows + Visual Studio Build Tools** to run
  end-to-end. Cross-platform agents can still do the multi-targeting/compile work; flag the
  Windows-only verification for the human or a Windows runner.
- **Scale is unvalidated.** Evidence so far: 10 projects / ~1,100 symbols / ~2 s. The
  portfolio use case implies solutions an order of magnitude larger; MSBuildWorkspace
  load time, memory, and partial-load failures at that size are unknowns. On the first
  large-solution run, capture wall time, peak memory, and workspace diagnostics; if load
  fails or runtime is unusable, escalate — do not silently shrink the analysis scope to
  make it pass.

## 5. Work streams

Order matters: **WS1 first** — without mechanical verification you cannot safely accept work
from implementation agents. WS2/WS3/WS7 next (same graph, high value, low risk). WS4 and
WS5/WS6 can then proceed in parallel.

### WS1 — Test battery: the feature contract (PREREQUISITE — do this before anything else)

Goal: make correctness checkable by machine, so the orchestrator never has to trust a diff.
The battery **is the product spec**: every scenario in **Appendix A** becomes a test, and the
set of green Contract tests defines what Knip.NET is claimed to do. Features "land" by
promoting a Gap test to Contract — never by prose.

- Create `tests/Knip.Core.Tests/` (xUnit) and `tests/fixtures/` containing small,
  **self-contained** solutions (no external/private packages; nuget.org only, offline-friendly).
- **Fixture architecture (decided):** one fixture solution per appendix category (A–K),
  each scenario isolated in its own namespace (e.g. `CatE.E05`), **no references across
  scenario namespaces**. One solution per category bounds MSBuildWorkspace loads (test
  runtime stays tolerable — a slow gate is a skipped gate) without letting scenarios keep
  each other's code alive. Fixture projects are **never added to `Knip.slnx`** — they
  contain deliberate dead code; the `-warnaserror` gate applies to the tool only.
- Tests call `KnipEngine.RunAsync` on a fixture and assert the **exact finding set**
  (symbol display names) — both what IS flagged and what is NOT. One `MSBuildLocator`
  registration per test process — use a collection fixture; registering twice throws.
- **Fixtures must prove they exercise the scenario (anti-vacuous-green rule).** A green
  "X stays alive" test is worthless if the fixture accidentally roots X some other way —
  the test then pins nothing. Every *alive* assertion therefore ships with a **dead
  sibling**: a symbol identical except for the use-site under test, asserted flagged.
  The sibling is the built-in mutation check. Where a sibling is impractical, the task
  report must show the **red flip** instead: remove the use-site, run the test, paste the
  failure. No alive-assertion is accepted without one of the two.
- Implement every scenario in Appendix A, honoring its status:
  - **Contract** tests must be green before WS1 is done.
  - **Core-gap** tests are written to assert the *correct* behavior and are expected red;
    tag them `Trait("status","core-gap")` + `Skip` with the appendix ID. Each becomes a
    **WS1b** fix task (see below).
  - **Moat** tests likewise, `Skip` referencing WS5.
  - Appendix statuses are hypotheses until first run — the first battery task is a **triage
    run** that corrects statuses against reality and reports surprises to the human.
- Acceptance: `dotnet test` green locally and in CI (skips visible, never deleted);
  the appendix table updated with triaged statuses; every alive-assertion has its dead
  sibling in the fixture or red-flip evidence in the task report.

### WS1b — Close core-walker gaps surfaced by the battery

Appendix A category E (implicitly-invoked members) is expected to expose **false positives in
the core walker** — usages with no `IdentifierName`/`GenericName`/object-creation node, which
`ReferenceWalker` currently never sees (operators, custom indexers, `foreach`/`await`/`using`
pattern members, collection initializers, `Deconstruct`, LINQ query syntax). These are core
correctness, NOT WS5 plugin territory — they violate the "false positives are the product
risk" rule (§3.8) on plain C#.

- One fix task per gap: usually a new `Visit*` override or a switch to
  `SemanticModel.GetOperation`-based edge recording for the affected node kinds.
- Definition of done per task: its battery test un-skipped and green; no Contract test
  regressed. **The only allowed edit to test files is removing the `Skip`/status tag**
  (plus the appendix status update) — any changed assertion or fixture edit in a WS1b
  diff escalates to the human, no exceptions. WS1b blocks WS2/WS3 sign-off on real
  solutions (dead-code deletions must not be recommended while known plain-C# false
  positives exist).

### WS2 — Unused `<ProjectReference>` detection

A project reference is unused when no edge crosses from the referencing project's symbols to
symbols in the referenced assembly. The data already exists in the graph; it currently drops
the *per-project origin* of edges — the implementer will need to track source-project per edge
(or aggregate per-project used-assembly sets during the walk).

- New `FindingKind.UnusedProjectReference`; report project + referenced project (no file/line
  or point at the `.csproj` line).
- A reference can be load-bearing without symbol edges (transitive restore behavior, runtime-only
  deps, `InternalsVisibleTo`). Per the REVISED §3.8 (recall over silence): **always EMIT the
  finding, marked honestly** — never drop it. Attach the matching hazard (`configBoundType`/
  runtime-only, `internalsVisibleTo`) and a low/medium `confidence` (via the WS8 model), so the
  agent triages it through the verify loop rather than the tool hiding it. (WS2 shipped before the
  confidence/hazard model; folding its output into WS8 v2 = `removeProjectReference` + confidence
  is part of WS8b.)
- Acceptance: fixture with one used and one unused ProjectReference; used one NOT flagged; the
  known-hazard case EMITTED with its hazard + low confidence (asserts the tier, not absence).

### WS3 — Unused `<PackageReference>` detection

A package is unused when no referenced symbol resolves to an assembly delivered by that
package. Requires mapping assemblies→packages (via `obj/project.assets.json`) and recording
which *external* assemblies each project touches (today external edges are dropped at
`AddEdge` — the implementer must count them per-project before the drop, without adding them
as graph nodes).

- Hazards (fixture each): analyzers/source-generator packages, build-only packages
  (`PrivateAssets="all"`), packages used only via transitive types, implicit `Using`s.
  Per the REVISED §3.8: these are **EMITTED, marked honestly** — never excluded/dropped. Attach the
  matching hazard + low/medium `confidence` so the agent triages via the verify loop. (Flips the old
  "exclude or mark low-confidence" to "always emit, mark honestly.") Emit into the WS8 vocabulary
  (`removePackageReference`), not a bespoke shape.
- Acceptance: fixture with one used and one unused package; used one NOT flagged; the analyzer-style
  / PrivateAssets / transitive-only cases EMITTED with their hazard + low confidence (assert the
  tier, not absence).

### WS4 — .NET Framework 4.8 / legacy project support (the migration-cleanup use case)

Goal: analyze legacy-format `.csproj` + `packages.config` solutions targeting net48.

- Multi-target the tool: `<TargetFrameworks>net10.0;net472</TargetFrameworks>` in both
  projects. The net472 build must pin **Microsoft.CodeAnalysis 4.x** (last major with
  netstandard2.0 support) via conditional `PackageReference`; keep 5.6.0 for net10.
- Compile `Knip.Core` against BOTH Roslyn versions with zero or minimal `#if NET472`. If an
  agent reports an API that only exists in Roslyn 5, prefer rewriting to the common API over
  forking the code path. Version-specific code is allowed only in `Knip.Cli` and
  `KnipEngine`/loading.
- On net472, `MSBuildLocator` discovers **Visual Studio / Build Tools MSBuild** (full
  framework), which is what can evaluate legacy projects. Note `.slnx` may not parse on old
  MSBuild — legacy solutions will be `.sln`, fine.
- New fixture: a legacy-format csproj + packages.config solution (hand-author the XML;
  new SDKs can't create these).
- End-to-end verification is **Windows-only** — escalate to the human for a Windows run if no
  Windows environment is available; cross-platform CI should still compile the net472 TFM.
- Expect follow-up config needs for legacy codebases (WebForms code-behind, `Global.asax`,
  `web.config`-registered types, WCF services) — capture as WS5 plugin ideas, do not hack
  into the core walker.

### WS5 — Framework plugins / false-positive suppression

The moat. Built-in heuristics miss reflection (`Activator.CreateInstance`, `Type.GetType`),
non-generic DI (`AddScoped(typeof(Foo))`, assembly scanning), MassTransit consumers, source
generators, Razor/Blazor/XAML data binding.

- Design a plugin seam first (a way to contribute extra roots/edges given a `Compilation`),
  get the human's sign-off on the shape, then implement plugins one per task.
- Every plugin ships with a fixture demonstrating the false positive it kills.

### WS6 — Packaging & CI

- `PackAsTool` global tool (`Hdir.Knip`, command `dotnet-knip`) published to the org feed;
  a GitHub Action / Azure DevOps task wrapper (restore → run → publish SARIF).
- Repo CI: build + `dotnet test` on every PR (this part can land together with WS1).
- Consider `--baseline` (gate only NEW findings) from the roadmap when packaging for CI —
  it is what makes adoption on brownfield solutions tolerable.

### WS7 — Production-mode analysis (test-only reachability — the systematic false negative)

The tool is deliberately biased toward false negatives (§3.8: never flag live code). Most
false negatives are scattered one-offs; this one is **structural**: every `[Fact]`/`[Theory]`
method is a root, so production code referenced *only by its own tests* is reachable and never
flagged — tested-but-dead code is invisible. For the pre-migration deletion use case this is
the largest deletable unit there is (dead feature + its whole test suite). Knip (JS) has the
same problem and solves it with `--production` mode; same shape applies here.

- Classify projects test vs production: `testProjects` config globs, defaulting to detection
  via the `IsTestProject` MSBuild property or test-framework package references
  (xunit/nunit/mstest).
- Tag each root with its origin and run **two-color reachability**: reachable from production
  roots → alive; reachable ONLY via test roots → new `FindingKind.OnlyUsedByTests`, a distinct
  kind because the remediation is different ("delete the code AND its tests"). Report the
  referencing test symbols alongside the finding so the deletion unit is obvious.
- Transitivity matters: A used by B, B used only by tests → both are test-only (K6).
- **Off by default** — default semantics stay as pinned by B1/K1; enable via
  `--production` / config. Note the deliberate tension with fixture B1: B1 pins graph
  *identity* in default mode; category K governs production mode.
- Workaround until this lands (document in README): `ignore.projects: ["*Tests*"]` — blunt
  (test code not analyzed at all, orphaned tests not reported, `InternalsVisibleTo` edges
  lost), but usable; pinned by K4.
- Acceptance: battery category K promoted to Contract.
- **Test-project classification (K7 — DECIDED 2026-07-15).** Signal order, first match wins:
  (1) explicit `testProjects` config globs override everything; (2) auto-detect via referenced
  test-framework assemblies in the `Compilation` (`MSTest.TestFramework`, `xunit.core`,
  `nunit.framework`) — prefer this over the MSBuild `IsTestProject` property (Roslyn's Project
  model doesn't surface it; do NOT build MSBuild-evaluation machinery for it — use only if it turns
  out cheaply readable); (3) name globs (`*Tests`, `*.Test`, `*.Tests`) as fallback. Zero test
  projects detected in production mode → warn LOUDLY on stderr AND in the machine-readable
  diagnostics block (WS8); never fail, exit codes unchanged. `-v` lists each project's
  classification and WHICH signal classified it (nobody trusts `OnlyUsedByTests` without seeing
  the classification). K7 promotes with a fixture per signal + the zero-detection warning.

### WS8 — Agent-first interface (the JSON output IS the product API)

Agents are first-class users: the canonical downstream flow is **agent runs knip → triages →
deletes → verifies → opens PR**. Everything an agent needs must be in the machine output — no
stderr scraping, no source-diving for symbol boundaries, no guessing whether the run was
trustworthy. **Priority: WS8a jumps the queue** — WS3/WS7/WS-enum must emit their finding kinds
into the WS8 vocabulary rather than inventing their own; WS2's `UnusedProjectReference` folds into
the v2 design. WS8b–d run in the **reporting/CLI lane, parallel to the analyzer lane**; coordinate
merges on `Finding`/`BuildFindings`, which both lanes touch.

- **WS8a — Design proposal (→ human sign-off; user-facing API, §6 gate).** One doc proposing:
  - **JSON v2:** root `formatVersion`; a `reliability` block (restore/load failures,
    unresolved-type count, projects loaded vs failed, production-mode classification warnings,
    overall `degraded: bool`); a `summary` block (counts by project × kind × confidence);
    per-finding: stable `id` (content hash of kind+symbol+project), full deletion `span`
    (start/end covering attributes + XML docs), `confidence` (high|medium|low), `hazards[]`
    (publicApi, serializationShaped, configBoundType, …), `remediation` (deleteSymbol |
    removeFromInterface | removeProjectReference | removePackageReference | deleteCodeAndTests).
    Propose the confidence/hazard RULES explicitly — they encode §3.8 and need human eyes (L9).
  - **CLI surface:** `--why <symbol-or-id>` (flagged → incoming-edge report; alive → shortest root
    path with file:line hops); `--print-config` (effective merged config as JSON); unknown-key
    warnings extended from `plugins.*` to ALL of knip.json.
  - **Config schema:** a published JSON Schema for knip.json, in-repo, referenced via `$schema` in
    the example config.
  - Console output stays human-first; SARIF unchanged except mapping new fields into existing SARIF
    slots (e.g. `partialFingerprints` for IDs).
- **WS8b — Implement JSON v2 + reliability + schema** (after sign-off). v2 is a BREAKING change to
  the JSON shape, acceptable pre-1.0 — do NOT maintain both shapes.
- **WS8c — `--why` + `--print-config`.** If `--why` needs extra edge provenance that costs memory,
  gate it behind the flag (two-pass acceptable).
- **WS8d — AGENTS.md:** the canonical agent recipe — run → check `reliability.degraded` → triage by
  confidence → delete by span → build + tests → re-run knip asserting no new LIVE-code flags
  (deleting dead code legitimately uncovers newly-dead symbols, so don't assert identical output) →
  baseline/ignore the remainder with reasons. Terse, imperative, exit-code table, one full JSON
  example. README links to it. **CHECKPOINT: WS8d is NOT done until escalated for human review
  against a real dogfood run (the Tjenesteportalen findings) — validated, not signed off as prose.**
- **Hazards:** §3.7 noise policy unchanged. §3.8 is REVISED (recall over silence) — WS8 emits MORE,
  with honest confidence/hazards, and NEVER silently suppresses to avoid FPs. Low confidence is NOT
  license to emit findings that §3.7 noise policy (outermost-only, ctors/overrides) suppresses.
  `--why` output is prose + file:line, never raw graph keys (invariant #1 stays internal).
- **`rootCause` (added per §3.8 revision):** per-finding OPTIONAL `rootCause` = the finding id of the
  nearest dead symbol that keeps this one dead (null when directly unreferenced). Lets agents delete
  outermost-first and shows cascade structure; `--why` reuses it. Battery row **L10**.

**L9 — confidence & hazard model (SIGNED OFF 2026-07-15, with adjustments). This is HOW revised
§3.8 is implemented.** Hazards are advisory-only (never change the emitted set); confidence starts
`high`, demoted by FIRST match:
- **C1 (per-project).** A project-load/restore failure demotes findings IN THE AFFECTED PROJECTS to
  `low`; only solution-GLOBAL degradation demotes everything. `reliability` attributes failures per
  project.
- **C2 (publicApi — config-sensitive).** If `publicApiProjects` OR `treatAllPublicAsUsed` is set,
  remaining `publicApi` findings → `medium` (user declared their external-API posture); if NEITHER is
  set, exposure is unknown → `low`. Other C2 hazards (`serializationShaped`, `configBoundType`,
  `diPluginShaped`) → `low`.
- **C3** project/package-ref → `medium`. **C4** `deleteCodeAndTests` (test-only, WS7) → `medium`.
- **C5 DROPPED from v1** — "entry-point near-miss" ships only with an enumerated definition + one
  fixture each, or not at all; add later ADDITIVELY. No vibe-based demotions.
- **New hazard `internalsVisibleTo`:** `[InternalsVisibleTo]` naming an assembly NOT in the solution
  → internal findings in that project → `low` (invisible external consumer; same logic as
  unconfigured publicApi).
- **Autonomy line (hard precondition):** `high` → agent may DELETE into the PR; `medium` → PROPOSED
  in the PR for human review; `low` → SURFACED only, never touched. `high` autonomy is CONDITIONAL
  on the full verify loop — `reliability.degraded == false` for the finding's project → delete
  strictly by span → `dotnet build` + full tests green → re-run knip asserting no new LIVE-code
  flags. Any step fails → the finding drops to `medium` handling. Auto-deletion without the loop is a
  PROTOCOL VIOLATION, not a judgment call.
- **Change control:** each demotion rule is pinned by an Appendix-L fixture (both branches of the C2
  split included). Rule ADDITIONS are additive with their own fixture; rule CHANGES are contract
  changes → ESCALATE. **L9 blocks WS8b.**

## 6. Orchestration protocol

**Task slicing.** One work-stream bullet ≈ one implementation-agent task. Never hand an agent
a whole work stream. Every task prompt must contain: the goal, the invariants section (§3),
the relevant work-stream card (§5), the verification gate (below), and the instruction to
return a diff summary + test evidence, not prose claims.

**Verification gate — run after EVERY task, before accepting:**

```bash
dotnet build Knip.slnx -warnaserror        # zero warnings policy
dotnet test                                 # once WS1 exists — non-negotiable afterwards
dotnet run --project src/Knip.Cli -- tests/fixtures/<relevant>/Fixture.sln
# expected findings per fixture are asserted by the tests; eyeball the console output anyway
```

Also check the diff itself: no `bin/`/`obj/` files, no deleted invariants, no new
`#if` outside the allowed places (WS4), README/knip.json updated when behavior or config
changed.

**Review checklist (orchestrator, per diff). These are mechanical checks — do not
substitute judgment for them; a diff that fails one is bounced even if it "looks fine":**
1. Does it violate any §3 invariant? → reject with the invariant number.
2. Any heuristic change that could create an **unflagged H-class false positive** (reflection,
   DI-by-name, serialization, config/markup-bound — the runtime-only classes of revised §3.8) must
   ADD a fixture proving it doesn't, in the same diff. Absent → bounce; no promise-to-add-later.
   (Revised 2026-07-15: was "prove live code is not flagged"; under recall-over-silence, EMITTING a
   finding is fine — the guard is specifically against the runtime-only H-class that survives build
   + tests. A new confidence/hazard demotion RULE likewise ships with a fixture asserting the tier.)
3. Any edit to an existing test assertion or fixture, in any task, escalates to the
   human — do not evaluate whether the weakening "seems reasonable"; that judgment is
   exactly what a bad diff exploits. (Gap-promotion tasks: only `Skip` removal is allowed,
   see WS1b.)
4. Is the change in the right layer (analysis vs loading vs CLI)? See invariant #9.

**Real-solution deletion gate — before ANY deletion recommendation on a real codebase.**
Green fixtures prove the tool's mechanics; they say nothing about a specific real
solution. All three steps, in order:
1. Restore-warning check clean (invariant #6 output shows zero unresolved-type warnings).
2. Mechanical proof: apply the proposed deletions on a branch of the *target* solution;
   its build and its own test suite must stay green. This is the strongest automated
   check a dead-code finder has — use it. But note what it cannot catch:
   reflection/DI-scanning/serializer usage (Appendix H) compiles fine with the code
   deleted and breaks at runtime.
3. Until the relevant WS5 plugins exist: a human reviews EVERY finding in
   category-H-shaped code (reflection, `Type.GetType`, scanning DI, serialized DTOs,
   config-registered types) plus a random sample of the rest. Deletion without this
   review is forbidden — this is the §3.8 product risk made operational.

**Escalate to the human when:**
- A §3 invariant genuinely needs changing (don't change it yourself).
- The WS5 plugin seam design is ready for sign-off, or any public config schema changes
  (`knip.json` keys are user-facing API).
- WS4 needs a Windows/VS Build Tools run, or real-solution validation needs feed auth.
- Two consecutive attempts at a task fail verification — stop, summarize what broke, ask.
- Anything requires publishing (feed, marketplace) — publishing is human-approved, always.

**Never:** recommend deletions on a real solution without the full deletion gate above
(restore-clean is necessary, not sufficient); commit fixture changes that reduce coverage;
"fix" a false positive by removing a report category instead of adding a root/edge.

## 7. Git hygiene

The repo history is part of the handoff memory between sessions and agents — keep it legible.

**Branching & merging**
- `main` is protected by convention: it must always pass the verification gate (§6).
  Never commit red tests or non-compiling code to `main`; never force-push or rewrite
  history on `main`.
- One branch per task, named `<ws>/<slug>` (e.g. `ws2/unused-project-refs`,
  `ws1b/indexer-element-access`). Merge to `main` only after the gate passes.

**Commits**
- One logical change per commit; a task is typically 1–3 commits, not 30. Subject line:
  imperative, ≤ 72 chars, prefixed with the work stream (`ws1b: record edges for custom
  indexer element access`). Body explains *why*, and names the battery test(s) that prove it.
- Behavior changes, their tests, and the ledger (§8) / Appendix A status updates travel in
  the **same commit** — never let the runbook drift from the code.
- Agent-authored commits carry a `Co-Authored-By:` trailer identifying the agent/model.

**What never enters the repo**
- `bin/`, `obj/`, `.vs/`, IDE state, `TestResults/` — enforced by `.gitignore`; if
  `git status` shows build output, fix `.gitignore` in the same commit rather than
  force-adding.
- **Credentials of any kind**: no `NuGet.config` containing PATs/feed passwords (feed auth
  lives on the machine/CI, not in the repo), no tokens in scripts or test settings.
- Hdir internal source or analysis output of Hdir solutions — fixtures are synthetic only.
- Skipped/`Gap` battery tests may be promoted, never deleted (§5 WS1); a commit deleting
  contract or gap tests is rejected in review.

**Releases** (relevant from WS6)
- Tag releases `v<semver>` on `main`; the tag message lists promoted battery scenarios since
  the last tag — the changelog falls out of the feature contract.

## 8. Current state ledger

Keep this section updated as tasks complete — it is the handoff memory between sessions.

- [x] Dead-code detection (flagship) — validated on Hego.Common.sln, 7 true positives
- [x] Cross-project identity via doc-comment IDs; overload-candidate liveness; entry-point
      containing-type roots (3 real bugs found & fixed during initial build)
- [x] Console/JSON/SARIF reporters, exit codes, knip.json config + discovery
- [x] Git repository initialized; runbook + battery contract (Appendix A) written
- [x] WS1 test battery per Appendix A — ALL 11 categories (A–K) implemented, triaged, merged.
      Suite: **79 passing / 17 skipped / 96 total**, green on main. The 17 skips are all
      intentional deferrals: 11 WS5 moat (category H), 5 WS7 production-mode (category K),
      1 enum-feature (G6). Every alive-assertion carries a dead sibling or red-flip evidence.
- [x] WS1b core-walker gap fixes — all 12 confirmed false positives closed and promoted to
      Contract: E1–E11 (implicit-invocation members) + **C6** (extension-syntax call did not
      edge the declaring static class → whole class falsely flagged; fixed by edging the
      reduced extension method's ContainingType + ReducedFrom).
- [x] Bugs found by the battery and FIXED (all with a promoted Contract test):
      **SARIF `$schema`** (reporter emitted `schema`, non-compliant with SARIF 2.1.0; J5),
      **malformed knip.json** (CLI crashed exit 134 + stack trace instead of clean exit 2;
      Runner now wraps `KnipConfig.Load`; I6),
      **B6 doc-comment-ID collision** (identical signatures in different assemblies merged into
      one graph node → false negative; SymbolId now assembly-qualifies keys via the DEFINING
      assembly, preserving invariant #1 — B1/B3 cross-project tests stay green).
- **INVARIANT #8 REVISED 2026-07-15** (human decision, §6): "prefer a false negative" → **"recall
  over silence — but hazards are sacred"** (see §3.1 list item 8). Output is a suggestion set behind
  a mandatory verify loop; emit findings with honest confidence + hazards instead of silently
  suppressing. Only the runtime-only H-class (reflection/DI/serialization/config-bound) that survives
  build+tests stays the unacceptable risk → always demote confidence, keep killer plugins default-on.
  Consequences applied same commit: WS2/WS3 cards flip to "always emit, mark honestly"; §6
  review-checklist item 2 reworded; L9 confidence/hazard model pinned (WS8 card) + Appendix L
  rows L10–L17; `rootCause` field added to WS8 design. Existing FN-preferring CORE rules (inv #3,
  B6-style) unchanged — no bulk walker loosening.
- Process rule (learned from B6): **D-rows escalate to the human BEFORE writing any code**, even
  when the fix looks obviously correct. Disposition sign-off ≠ merging the concrete diff. Remaining
  open D-rows: **L9** (confidence rules, blocks WS8b — decided at WS8a sign-off), and the interface-
  member-remediation backlog row below.
- [x] **H11 DECIDED 2026-07-15, IMPLEMENTED 2026-07-15** (walk built-in generated trees for
      edges/roots, never report their decls; I1 wholesale-drop unchanged) — analyzer lane; H11 row
      promoted `G-feat` → `C` (green). Generated detection = path globs + `// <auto-generated/>` header
      + `[GeneratedCode]` + no-path in-memory trees, INDEPENDENT of `ignore.files`. Measured cost on
      CatH: +2 declared symbols, ~+0.8ms avg (within noise) for the one extra walked tree; findings
      27→26 (Invoke() now alive, 2 generated decls suppressed — net safe-direction, invariant #8).
- [x] **K7 DECIDED 2026-07-15** (classification signal order + zero-detection warning) — feeds WS7;
      see the WS7 card and K7 appendix row.
- [x] **MSTest/NUnit entry-point defaults gap FIXED** — added the MSTest lifecycle + NUnit one-time
      attributes to default `entryPoints.attributes`; battery rows F9–F11 (Contract). Removed a
      §3.8 FP class seen on Tjenesteportalen.
- [ ] **WS8 — Agent-first interface (JSON output = product API). WS8a design PROPOSAL JUMPS THE
      QUEUE** (→ human sign-off, §6). WS3/WS7/WS-enum must emit their finding kinds into the WS8
      vocabulary, not invent their own; WS2's `UnusedProjectReference` folds into v2. WS8b (JSON v2
      + reliability + schema), WS8c (`--why`/`--print-config`), WS8d (AGENTS.md) run in the
      reporting/CLI lane, parallel to the analyzer lane — coordinate `Finding`/`BuildFindings`
      merges. Battery = Appendix category L (L9 is a D-row blocking WS8b). See the §5 WS8 card.
- [~] **WS8b-2 L9 confidence/hazard demotion engine — IMPLEMENTED 2026-07-15.** `ConfidenceModel.Apply`
      (Core) grades confidence in a FINAL pass in `KnipEngine.RunAsync`, AFTER the reliability block is
      complete (C1 per-project attribution needs workspace restore/load failures, attributed post-analyzer).
      Hazards attached earlier in `ToFinding` via `FindingEnrichment.ComputeHazards` (publicApi from
      accessibility; internalsVisibleTo from a per-project `[InternalsVisibleTo]`→non-solution scan). Hazards
      ADVISORY (invariant #8) — emitted set byte-identical; only confidence graded. First-match order
      C1 → publicApi/C2 → internalsVisibleTo → C3. Promoted L11–L14, L16 (C3 half), L17 to `C`; L9 + L15 kept
      `G-feat` (serialization/config/DI hazard DETECTION deferred to WS5 — enum + low-tier demotion exist, no
      detector). C4 (`deleteCodeAndTests`) deferred to WS7; C5 dropped. Both TFMs build `-warnaserror`; suite
      108 passing / 18 skipped. No existing finding's confidence shifted (no non-CatL test asserts it). Added
      `[InternalsVisibleTo(Knip.Core.Tests)]` (csproj attribute, both TFMs) so L12 unit-tests the internal
      engine with synthetic `ProjectsFailed`.
- [x] **`ignore.symbols` FQN matching FIXED** — members now match against the finding `DisplayFormat`
      (namespace + containing type + params), so a reported name copies verbatim into `ignore.symbols`;
      bare member-name globs no longer match members. I2 strengthened (pins FQN match AND
      bare-name-no-longer-matches). `FqFormat` untouched (its other consumers unaffected).
- [ ] **Load-diagnostics: distinguish real project-load failures from restore-audit noise.**
      MSBuildWorkspace surfaced harmless NU1900/NU1903 audit advisories as "Msbuild failed when
      processing" while the load actually completed. Real load failures must stay LOUD (invariant
      #6 degraded-analysis warning); audit noise must not masquerade as load failure.
- [ ] Backlog (no action yet): interface members flagged because ALL callers use the concrete type
      (real case `IKontaktregisterFasade.HentPasientInformasjon`) are CORRECT findings, but the
      remediation is "remove from the interface," not "delete the method" — candidate for a distinct
      finding kind / remediation hint. Add as a D row when convenient.
- [x] WS-enum (decided 2026-07-14 from G6; DONE 2026-07-15): member-level enum dead-code support.
      Enum members are declared as graph nodes (`VisitEnumMemberDeclaration` → `Declare`), reference-
      tracked via the existing member-access/identifier path, and reported as new
      `FindingKind.UnusedEnumMember` (remediation `DeleteSymbol`). Outermost-only (§3.7): a whole-dead
      enum reports the TYPE only. G6 promoted `G-feat` → `C`.
- [x] WS2 unused ProjectReferences — new `FindingKind.UnusedProjectReference`; per-project
      used-assembly sets tracked in `AddEdge`. Conservative: runtime-only/transitive refs (zero
      symbol edges) are a documented FP surface (README "triage before removing"); a future
      opt-out `knip.json` key would need sign-off (not added).
- [ ] WS3 unused PackageReferences — UNBLOCKED (WS8a signed off): emit `RemovePackageReference`
      remediation into the v2 vocabulary. Per revised §3.8: analyzer/PrivateAssets/transitive-only/
      implicit-Using hazards are EMITTED with hazard + low/medium confidence, never dropped. NOTE
      (offline): reading `obj/project.assets.json` needs a restored fixture — the test must restore
      its fixture (nuget.org-cached package) or the task solves assembly→package mapping another way.
- [x] WS4 net472 multi-target + legacy csproj — both TFMs build `-warnaserror` (Roslyn 4.14 for
      net472, 5.6 for net10); ZERO `#if` in any source (all divergence at csproj level; BCL gaps
      via PolySharp + a shim file); legacy-format fixture authored. **Windows-only e2e of the
      legacy fixture NOT yet run — needs a Windows/VS Build Tools runner.**
- [~] WS5 plugin seam + first plugins — **SEAM + `reflection` + `scanningDi` plugins LANDED**
      (H1/H2 via reflection; H4/H12 via scanningDi — all Contract; suite 113/16; both TFMs green;
      default-ON set = `{reflection, scanningDi}`). Remaining plugins queued:
      `blazorParameter` (→H6), `serialization` (→H5) — each promotes its H row with a decoy fixture.
      Design SIGNED OFF 2026-07-14 (`docs/ws5-plugin-seam.md`):
      additive-only symbol-typed `IContributionSink` choke point (invariants #1/#5/#8 hold
      structurally), per-project `Contribute`, built-in plugins only (no external assembly loading).
      New `plugins.*` knip.json keys APPROVED. Conditions: unknown plugin ids AND unknown per-plugin
      keys emit a VISIBLE warning (no silent no-op); plugin ids are camelCase (`scanningDi`);
      default-ON = `reflection` + `scanningDi`, `blazorParameter`/`serialization` OFF (no detection
      magic); add an F8-style battery row pinning the default-enabled set; every plugin fixture ships
      a DECOY (unrelated dead symbol asserted STILL flagged with the plugin ON — over-rooting guard);
      `-v` emits per-plugin contribution counts (roots/edges) + wall-time per project.
- [~] WS6 — repo CI (build + test GitHub Action) DONE (`.github/workflows/ci.yml`). Packaging
      (global tool `Hdir.Knip`, marketplace/feed publish) still TODO and human-approved.
- [ ] WS7 production-mode analysis / test-only reachability (Appendix A category K). Real-world
      signal: Tjenesteportalen has ~151 production findings kept alive only by test roots in default
      mode — high-value. K7 classification DECIDED (see WS7 card). UNBLOCKED (WS8a done): emit
      `OnlyUsedByTests` / `DeleteCodeAndTests` remediation into the v2 vocabulary. Promotes category K
      + the C4 confidence rule (deleteCodeAndTests → medium).
- [~] WS8 — Agent-first interface (JSON = product API). **WS8a signed off** + **WS8b DONE** (JSON v2
      shape + reliability + schemas + L9 confidence/hazard engine; L1–L4/L8/L10–L14/L16/L17 promoted).
      Remaining: **WS8c** (`--why`/`--print-config`/all-config-key warnings → L5/L6/L7); **WS8d**
      AGENTS.md — DRAFT merged, **NOT done until dogfood-validated against a real run** (needs feed
      auth). Deferred detectors: L15 (serialization/config/DI hazards → the serialization plugin +
      later). Backlog: reconcile design-doc vs shipped-schema (`reliability.projectsFailed` is an
      ARRAY per schema; drop/rename `productionModeWarnings`/`testProjectClassification`/`run.target`
      or add them to the schema when WS7 lands).

---

## Appendix A — Test battery (the feature contract)

Every row is one test. IDs are stable — reference them in commits, skips, and task prompts.
For each scenario the fixture contains BOTH the used and the unused variant, and the test
asserts the exact finding set: **live code is never flagged; the dead sibling is.** The dead
sibling is not decoration — it is the mutation check proving the fixture actually exercises
the scenario (anti-vacuous-green rule, §5 WS1). Where a sibling is impractical, red-flip
evidence is required instead.

**Statuses** (hypotheses until the WS1 triage run corrects them against reality):

| Status | Meaning |
|---|---|
| `C` Contract | Must pass now. A red `C` test blocks everything. |
| `G-core` | Suspected core-walker false positive. Test asserts correct behavior, is skip-tagged, and spawns a WS1b fix task. |
| `G-moat` | Invisible-usage gap — the reason paid tools are paid. Skip-tagged pending WS5 plugins; the row lists today's mitigation. |
| `G-feat` | Planned-feature gap — asserts the behavior of a not-yet-built feature; skip-tagged pending its work stream. |
| `D` Decision | Behavior is a product decision to confirm with the human before pinning. |

Rules: a Gap test is **never deleted, only promoted**. Promoting `G-*` → `C` is what "shipping
a feature" means. If triage finds a `C` row red, that is a bug report — escalate, don't re-tag.

### A. Reachability fundamentals

| ID | Scenario | Expected |
|---|---|---|
| A1 `C` | Unused private method | flagged |
| A2 `C` | Unused public method (no public-as-used) | flagged |
| A3 `C` | Transitive chain root→A→B; C uncalled | only C flagged |
| A4 `C` | Dead island: A↔B call only each other, nothing reaches them | both flagged |
| A5 `C` | Self-recursive method with no external caller | flagged |
| A6 `C` | Symbol referenced ONLY from dead code | flagged (dead code confers no life) |
| A7 `C` | Dead type: outermost-only reporting | type flagged, its members not |
| A8 `C` | Partial class/method split across files, used | one graph node, alive |

### B. Cross-project & visibility

| ID | Scenario | Expected |
|---|---|---|
| B1 `C` | Method used only from another project (lib+tests) | alive — pins invariant #1 |
| B2 `C` | Unused sibling of B1 in the same lib | flagged |
| B3 `C` | `internal` member used cross-project via `InternalsVisibleTo` | alive |
| B4 `C` | `publicApiProjects` glob: unused public API in matching project | not flagged |
| B5 `C` | `treatAllPublicAsUsed`: only private/internal dead code flagged | as stated |
| B6 `C` | Two projects declare identical namespace+type+signature (doc-comment IDs collide, no assembly in key) | DECIDED 2026-07-14: FIXED — SymbolId now assembly-qualifies keys via the DEFINING assembly (`Assembly::docId`), so the copies are distinct nodes and the unused one is flagged; invariant #1 preserved (B1/B3 green) |

### C. Overloads, generics, delegates

| ID | Scenario | Expected |
|---|---|---|
| C1 `C` | Two overloads, one called | the other flagged — the flagship's precision |
| C2 `C` | Overload resolution fails (unresolved arg type) | ALL candidates alive — invariant #3 |
| C3 `C` | `services.AddScoped<IFoo, Foo>()` | `Foo` alive via generic-arg edge |
| C4 `C` | Method group passed as delegate: `list.Select(Transform)` | `Transform` alive |
| C5 `C` | `nameof(Method)` as only reference | alive (conservative: name-token counts as use) |
| C6 `C` | Extension method called via extension syntax; unused sibling | used alive, sibling flagged (triage found this RED — declaring static class was not edged; fixed in WS1b) |
| C7 `C` | Delegate type used as parameter type only | delegate type alive |
| C8 `C` | Type used only as generic constraint `where T : IFoo` | `IFoo` alive |
| C9 `C` | Type used only in `typeof(Foo)` / `is Foo` / `as Foo` | alive |

### D. Polymorphism & inheritance

| ID | Scenario | Expected |
|---|---|---|
| D1 `C` | Interface member called via interface ref | implementation alive, never reported |
| D2 `C` | Override called via base-class ref | override alive, never reported |
| D3 `C` | Explicit interface implementation | never reported |
| D4 `C` | Interface with zero references | flagged |
| D5 `C` | Derived type used → base type | base alive via BaseType edge |
| D6 `C` | Override-of-override chain, base member used | whole chain alive |
| D7 `C` | Unused attribute class (never applied) | flagged; applied attribute alive |

### E. Implicitly-invoked members — core gaps CLOSED in WS1b

No `IdentifierName`/`GenericName`/object-creation node appears at the use site, so the walker
recorded no edge → **false positive on plain C#**. Triage confirmed E1–E11 as real core-walker
false positives; **all fixed in WS1b and promoted `G-core` → `C`** (edges now recovered via
`GetSymbolInfo`/`GetOperation`/`GetForEachStatementInfo`/`GetAwaitExpressionInfo`/
`GetCollectionInitializerSymbolInfo`/`GetDeconstructionInfo`/`GetQueryClauseInfo`, plus a
deterministic type-based fallback for pattern `Dispose`). E12/E13 were green from the start.

| ID | Scenario | Expected |
|---|---|---|
| E1 `C` | Custom indexer used via `obj[i]` | indexer alive |
| E2 `C` | `operator +` used via `a + b` | operator alive |
| E3 `C` | Implicit conversion operator used via assignment/argument | operator alive |
| E4 `C` | `operator ==`/`!=` used via comparison | operators alive |
| E5 `C` | `foreach` over custom (non-interface, pattern-based) enumerable | `GetEnumerator`/`MoveNext`/`Current` alive |
| E6 `C` | `await` on custom awaitable | `GetAwaiter`/`IsCompleted`/`GetResult` alive |
| E7 `C` | Pattern-based `Dispose` (ref struct in `using`) | `Dispose` alive |
| E8 `C` | Collection initializer `new C { 1, 2 }` | `Add` alive |
| E9 `C` | Tuple deconstruction `var (a, b) = obj` | `Deconstruct` alive |
| E10 `C` | LINQ query syntax over custom provider | `Select`/`Where`/`SelectMany` alive |
| E11 `C` | Index/Range pattern members `obj[^1]`, `obj[1..]` | `Length`/`Slice` alive |
| E12 `C` | Object initializer `new Foo { Bar = 1 }` | `Bar` setter/property alive (IdentifierName exists — expected green) |
| E13 `C` | Event subscribed with `+=` / raised | event alive; unused event flagged |

### F. Entry points & roots

| ID | Scenario | Expected |
|---|---|---|
| F1 `C` | `[Fact]`/`[Theory]` method | method + containing type alive — invariant #4 |
| F2 `C` | `*Controller` name pattern | type + its public members rooted |
| F3 `C` | Subtype of configured `baseTypes` (e.g. ControllerBase) | rooted as F2 |
| F4 `C` | Implements configured interface (IHostedService) | rooted |
| F5 `C` | Attribute matching with and without `Attribute` suffix in config | both match |
| F6 `C` | Top-level statements | synthesized Main + host type rooted |
| F7 `C` | Configured `symbolNames` (classic `Main`, `ConfigureServices`) | rooted |
| F8 `C` | Entry-point config REPLACED with empty lists | framework defaults gone; previously-rooted code flagged (config actually applies) |
| F9 `C` | MSTest `[TestInitialize]` setup method (DEFAULT config) | setup + a helper it calls alive; unattributed sibling flagged |
| F10 `C` | MSTest static `[ClassInitialize]`/`[AssemblyInitialize]` + `[DataTestMethod]` (DEFAULT config) | all rooted; unattributed static sibling flagged |
| F11 `C` | NUnit `[OneTimeSetUp]`/`[OneTimeTearDown]` (DEFAULT config) | both rooted; unattributed sibling flagged |

### G. Language corners

| ID | Scenario | Expected |
|---|---|---|
| G1 `C` | Unused local function | not reported (member-level tool; Roslyn's own analyzers cover it) |
| G2 `C` | Records: unused record flagged; used positional record's synthesized members | never reported |
| G3 `C` | Primary-constructor class (C# 12), used | no spurious findings |
| G4 `C` | `async` methods and iterators (`yield`) | treated as normal methods |
| G5 `C` | Nested private type used only by outer type | alive |
| G6 `C` | Enum members | DONE 2026-07-15 (WS-enum): enum members are first-class graph nodes. An unused member in a LIVE enum is flagged (`UnusedEnumMember`); used members (incl. `[Flags]` members OR'd into a live composite) stay alive; a whole-dead enum reports the TYPE only (outermost-only §3.7) |
| G7 `C` | Constructors / static ctors / finalizers | never reported — §3.7 |
| G8 `C` | Compiler-generated symbols (`<Main>$`, lambdas, anonymous types) | never reported |
| G9 `C` | Unsafe/pointer parameter type `Foo*` | `Foo` alive (pointer unwrap edge) |
| G10 `C` | `const` field referenced (compile-time folded) | alive |
| G11 `C` | Expression-bodied members throughout | identical to block-bodied |

### H. Invisible usage — the moat (WS5 feed)

Each row = one future plugin's acceptance test. Until then: skip-tagged, and the "mitigation"
is what we tell users today.

| ID | Scenario | Expected | Mitigation today |
|---|---|---|---|
| H1 `G-moat` | Members invoked only via reflection (`GetMethod("X").Invoke`) | alive | `ignore.symbols` |
| H2 `G-moat` | Type named only in a string: `Type.GetType("Ns.Foo")` | alive | `ignore.symbols` |
| H3 `C` | Non-generic DI with `typeof`: `AddScoped(typeof(IFoo), typeof(Foo))` | alive via typeof edge (expected green — verify) | — |
| H4 `C` | Assembly-scanning DI (Scrutor `.FromAssemblyOf<>`), MediatR handlers, AutoMapper profiles | alive — PROMOTED 2026-07-15 (`scanningDi` plugin roots types by framework-marker SHAPE: implement `IRequestHandler`/`INotificationHandler`/`IStreamRequestHandler` or derive from `Profile`, matched by NAME offline; also roots the marker interface. Decoy `UnrelatedType` stays flagged — no blanket-root of interface implementers). | `scanningDi` plugin (ON by default) |
| H5 `G-moat` | DTO properties touched only by JSON serializer (STJ/Newtonsoft) | alive | `ignore.symbols` on DTO namespaces |
| H6 `C` | Blazor `[Parameter]` properties set from markup; `@bind` targets | alive — PROMOTED 2026-07-15 (`blazorParameter` plugin, opt-in / default OFF: roots properties carrying `[Parameter]`/`[CascadingParameter]`/`[SupplyParameterFromQuery]`/`[EditorRequired]`/`[Inject]`, matched by attribute NAME offline; roots only the attribute-bearing member + its accessors — never blanket-roots the component. Decoys `MyComponent.Unbound` (plain property) and `UnrelatedType` stay flagged). | `blazorParameter` plugin (opt-in) or `entryPoints.attributes: ["Parameter"]` |
| H7 `G-moat` | XAML binding targets (WPF/MAUI) | alive | `ignore` config |
| H8 `G-moat` | WebForms `.aspx`/`.ascx` code-behind referenced only from markup (WS4-relevant) | alive | `ignore.files` on code-behind |
| H9 `G-moat` | Types referenced only from `web.config`/`app.config` (WCF services, HTTP modules, providers) | alive | `ignore.symbols` |
| H10 `G-moat` | `dynamic` dispatch `((dynamic)x).M()` | alive (undecidable — document as designed FP + mitigation) | `ignore.symbols` |
| H11 `C` | Source-generated code references user symbols, but `**/*.g.cs` is ignored by default → edges FROM generated code are lost | DECIDED 2026-07-15, IMPLEMENTED 2026-07-15: WALK built-in generated trees for their outbound edges/roots, NEVER report declarations inside them (extends the G8 rule from symbols to files). "Generated" = built-in path patterns (`**/*.g.cs`, `*.Designer.cs`, `*.generated.cs`…) + generator-produced in-memory trees (no file path) + `[GeneratedCode]`/`// <auto-generated/>` header heuristics, detected INDEPENDENTLY of `ignore.files` (`GeneratedCode.IsGenerated`) and checked BEFORE the ignore.files drop. `ReferenceWalker` records generated declaration ids into `GraphState.GeneratedDeclarations`; `ShouldReport` suppresses them. **I1 stays pinned as-is** (user `ignore.files` keeps wholesale-drop: not walked, not reported); only BUILT-IN generated handling is walk-don't-report — I1 battery unchanged & green. NO new config key. Battery (green): (a) user method used only from its generated counterpart → alive; (b) dead symbol declared in a generated file → never reported; (c) decoy unrelated dead user symbol still flagged. Measured cost on CatH: 1 extra tree walked, +2 declared symbols, ~+0.8ms avg (within noise), findings 27→26 (safe-direction, invariant #8). | walk-don't-report |
| H12 `C` | MassTransit consumers registered via `AddConsumer`/scanning | alive — PROMOTED 2026-07-15 (`scanningDi` plugin roots types implementing `IConsumer`/`IConsumer<T>` by NAME offline; also roots `IConsumer<>` and — via the Consume signature edge — the message type. Decoy `UnrelatedService` stays flagged). | `scanningDi` plugin (ON by default) |

### I. Config, ignore & diagnostics

| ID | Scenario | Expected |
|---|---|---|
| I1 `C` | `ignore.files` glob | matching file's declarations neither reported nor walked |
| I2 `C` | `ignore.symbols` FQN glob | matching symbol not reported (but still occupies graph) |
| I3 `C` | `ignore.namespaces` | as I2 for the namespace |
| I4 `C` | `ignore.projects` | project skipped entirely; its assembly not in solution set |
| I5 `C` | `knip.json` discovered nearest-up from cwd; `--config` overrides | as stated |
| I6 `C` | Malformed `knip.json` | exit 2 with a clear error, no stack trace |
| I7 `C` | Fixture with a deliberately missing package reference | unresolved-type warning present in output — invariant #6 |
| I8 `C` | Clean, fully-restored fixture | NO unresolved-type warning |

### J. CLI & reporting

| ID | Scenario | Expected |
|---|---|---|
| J1 `C` | Clean solution | exit 0 |
| J2 `C` | Findings exist | exit 1; with `--no-fail` exit 0 |
| J3 `C` | Bad args / missing target | exit 2 + usage on stderr |
| J4 `C` | `--format json` | parses; findings sorted project→file→line (stable across runs) |
| J5 `C` | `--format sarif` | valid SARIF 2.1.0 minimal schema; one result per finding with location |
| J6 `C` | Findings/diagnostics on stdout vs progress on stderr | machine output never polluted by `-v` progress |

### K. Test-only reachability — the systematic false negative (WS7 feed)

Default mode treats test roots like any other root, so tested-but-dead production code is
alive. K1 pins that default; the rest assert production mode (`--production` / `testProjects`).

| ID | Scenario | Expected |
|---|---|---|
| K1 `C` | Default mode: production method referenced only from a `[Fact]` test | alive — pins default semantics AND documents the known false negative |
| K2 `G-feat` | Production mode: production method + type reachable only via test roots | flagged as `OnlyUsedByTests` (distinct finding kind) |
| K3 `G-feat` | Production mode: the K2 finding lists the referencing test symbols | remediation unit ("delete code and tests") visible in output |
| K4 `C` | Workaround: `ignore.projects` excluding the test project | test-only production method flagged (verify — expected green) |
| K5 `G-feat` | Transitive: A used by B; B used only by tests | production mode flags BOTH A and B |
| K6 `G-feat` | Production code genuinely used by production AND by tests | never flagged in production mode (tests don't taint) |
| K7 `G-feat` | Test-project classification default and zero-test-project warning | DECIDED 2026-07-15 (see WS7 card): signal order = explicit `testProjects` globs → referenced test-framework assemblies (`MSTest.TestFramework`/`xunit.core`/`nunit.framework`) → name globs (`*Tests`/`*.Test`/`*.Tests`). Zero detected in production mode → loud warning (stderr + machine diagnostics), never fail. `-v` shows each project's classification + the signal. Promotes with a fixture per signal + the zero-detection warning |

### L. Agent contract — the machine output as product API (WS8 feed)

Agents are first-class users (run → triage → delete → verify → PR). These rows pin what the JSON
must guarantee. Rows promote to `C` as WS8b–d land; **L9 (confidence rules) blocks WS8b** — it is
the "agents may act autonomously" line and is decided with the human at WS8a sign-off.

| ID | Scenario | Expected |
|---|---|---|
| L1 `G-feat` | JSON v2 on a mixed fixture | validates against the shipped output-format JSON Schema |
| L2 `G-feat` | Two consecutive runs, same fixture | identical finding ids and order (extends J4) |
| L3 `G-feat` | Broken-restore fixture (reuse I7) / clean (I8) | `reliability.degraded: true` with failure detail / `false` with zeroes |
| L4 `G-feat` | Delete every high-confidence finding strictly by reported span, then build | compiles green — spans are complete deletion units (the interface-level anti-vacuous test) |
| L5 `G-feat` | `--why` on a flagged / on an alive symbol | "no incoming edges" report / root-to-symbol path; exit 0 |
| L6 `G-feat` | `--print-config` with partial knip.json | effective config = file merged over defaults, valid JSON on stdout |
| L7 `G-feat` | Unknown top-level + unknown nested config key | one warning each, naming the key; analysis proceeds, exit unchanged |
| L8 `G-feat` | Summary block vs findings array | counts agree exactly |
| L9 `G-feat` | Confidence rule table (high/medium/low criteria) | DECIDED 2026-07-15 (see §5 WS8 "L9 model"): start high, first-match demotion; hazards advisory-only; autonomy = delete `high` (verify-loop precondition) / propose `medium` / surface `low`. Rows L11–L17 pin the individual rules. IMPLEMENTED 2026-07-15 (WS8b-2); left `G-feat` because L15 (serialization/config/DI hazard DETECTION) is still deferred (WS5) — the table is not fully pinned until its detectors land, though the low-tier demotion off those hazards already exists |
| L10 `G-feat` | Cascade finding carries the parent's id as `rootCause`; directly-unreferenced finding carries `null` | as stated — enables outermost-first deletion; `--why` reuses it |
| L11 `C` | Solution-global load/restore failure (reliability.degraded) | IMPLEMENTED 2026-07-15 (WS8b-2): ALL findings → `low` (C1 global). Pinned by CatL Degraded fixture (unresolved-type ref drives degraded end-to-end) |
| L12 `C` | Load failure in ONE project only | IMPLEMENTED 2026-07-15 (WS8b-2): findings in that project → `low`; other projects unaffected (C1 per-project attribution). Pinned by a direct ConfidenceModel test with synthetic `ProjectsFailed` (offline fixtures cannot make MSBuild fail one project) |
| L13 `C` | `publicApi`-hazard finding, `publicApiProjects`/`treatAllPublicAsUsed` SET | IMPLEMENTED 2026-07-15 (WS8b-2): → `medium` (C2 configured branch). CatL PublicApi fixture, glob set to a non-matching project so the public symbol survives to a finding |
| L14 `C` | `publicApi`-hazard finding, NEITHER key set | IMPLEMENTED 2026-07-15 (WS8b-2): → `low` (C2 unconfigured branch). CatL PublicApi fixture, default config |
| L15 `G-feat` | `serializationShaped`/`configBoundType`/`diPluginShaped` hazard | → `low` (C2 other hazards). DETECTION deferred (WS5 heuristics/plugins): the enum values + the low-tier demotion off them exist in ConfidenceModel, but nothing attaches these hazards yet, so nothing to pin end-to-end |
| L16 `C` | project-ref / package-ref (C3) and `deleteCodeAndTests` (C4) findings | IMPLEMENTED 2026-07-15 (WS8b-2): project/package-ref → `medium` (C3). Pinned by the WS2 UnusedProjectReference finding. `deleteCodeAndTests` (C4) DEFERRED to WS7 — no `OnlyUsedByTests` kind exists yet |
| L17 `C` | `[InternalsVisibleTo]` names an assembly NOT in the solution | IMPLEMENTED 2026-07-15 (WS8b-2): internal findings in that project → `low` (new `internalsVisibleTo` hazard). CatL InternalsVisibleTo fixture; private sibling stays `high` (anti-vacuous) |
