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

1. **Graph keys are documentation-comment IDs, never symbol references.**
   Roslyn does NOT give reference-equal symbols for the same method seen from source vs. from
   a referenced assembly; `SymbolEqualityComparer` across projects silently drops every
   cross-project edge and defeats "solution-wide". All graph state is string-keyed via
   `SymbolId.For(...)` (`src/Knip.Core/Analysis/SymbolId.cs`). Any diff reintroducing
   symbol-keyed dictionaries/sets in `GraphState` or the walker is wrong.

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

8. **False positives are the product risk.** The org will *delete code* based on findings
   ahead of migrations. When in doubt, prefer a false negative (miss dead code) over a false
   positive (flag live code). Any heuristic change needs a fixture proving it doesn't flag
   live code.

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
- Real-solution smoke target when auth exists: `Hego.Common.sln` (expects ~7 findings with
  `treatAllPublicAsUsed: true`; treat significant deviation as a regression signal).
- WS4 (legacy projects) ultimately needs **Windows + Visual Studio Build Tools** to run
  end-to-end. Cross-platform agents can still do the multi-targeting/compile work; flag the
  Windows-only verification for the human or a Windows runner.

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
- Tests call `KnipEngine.RunAsync` on a fixture and assert the **exact finding set**
  (symbol display names) — both what IS flagged and what is NOT. One `MSBuildLocator`
  registration per test process — use a collection fixture; registering twice throws.
- Implement every scenario in Appendix A, honoring its status:
  - **Contract** tests must be green before WS1 is done.
  - **Core-gap** tests are written to assert the *correct* behavior and are expected red;
    tag them `Trait("status","core-gap")` + `Skip` with the appendix ID. Each becomes a
    **WS1b** fix task (see below).
  - **Moat** tests likewise, `Skip` referencing WS5.
  - Appendix statuses are hypotheses until first run — the first battery task is a **triage
    run** that corrects statuses against reality and reports surprises to the human.
- Acceptance: `dotnet test` green locally and in CI (skips visible, never deleted);
  the appendix table updated with triaged statuses.

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
  regressed. WS1b blocks WS2/WS3 sign-off on real solutions (dead-code deletions must not
  be recommended while known plain-C# false positives exist).

### WS2 — Unused `<ProjectReference>` detection

A project reference is unused when no edge crosses from the referencing project's symbols to
symbols in the referenced assembly. The data already exists in the graph; it currently drops
the *per-project origin* of edges — the implementer will need to track source-project per edge
(or aggregate per-project used-assembly sets during the walk).

- New `FindingKind.UnusedProjectReference`; report project + referenced project (no file/line
  or point at the `.csproj` line).
- Careful: a reference can be load-bearing without symbol edges (transitive restore behavior,
  runtime-only deps, `InternalsVisibleTo`). Start conservative; put known-hazard cases in
  fixtures; consider a distinct confidence label in output.
- Acceptance: fixture with one used and one unused ProjectReference; used one NOT flagged.

### WS3 — Unused `<PackageReference>` detection

A package is unused when no referenced symbol resolves to an assembly delivered by that
package. Requires mapping assemblies→packages (via `obj/project.assets.json`) and recording
which *external* assemblies each project touches (today external edges are dropped at
`AddEdge` — the implementer must count them per-project before the drop, without adding them
as graph nodes).

- Hazards (fixture each): analyzers/source-generator packages, build-only packages
  (`PrivateAssets="all"`), packages used only via transitive types, implicit `Using`s.
  These are NOT safely detectable as unused — exclude or mark low-confidence.
- Acceptance: fixture with one used and one unused package; analyzer-style package NOT flagged.

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

**Review checklist (orchestrator, per diff):**
1. Does it violate any §3 invariant? → reject with the invariant number.
2. Does any new heuristic have a fixture proving live code is not flagged? → if not, bounce back.
3. Did tests change to accommodate the diff? If an existing assertion was weakened, escalate.
4. Is the change in the right layer (analysis vs loading vs CLI)? See invariant #9.

**Escalate to the human when:**
- A §3 invariant genuinely needs changing (don't change it yourself).
- The WS5 plugin seam design is ready for sign-off, or any public config schema changes
  (`knip.json` keys are user-facing API).
- WS4 needs a Windows/VS Build Tools run, or real-solution validation needs feed auth.
- Two consecutive attempts at a task fail verification — stop, summarize what broke, ask.
- Anything requires publishing (feed, marketplace) — publishing is human-approved, always.

**Never:** run against Hdir production solutions and recommend deletions without the
restore-warning check being clean; commit fixture changes that reduce coverage; "fix" a false
positive by removing a report category instead of adding a root/edge.

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
- [ ] WS1 test battery per Appendix A (incl. triage run) ← **NEXT, blocks everything**
- [ ] WS1b core-walker gap fixes (Appendix A category E)
- [ ] WS2 unused ProjectReferences
- [ ] WS3 unused PackageReferences
- [ ] WS4 net472 multi-target + legacy csproj (floor: .NET Framework 4.8)
- [ ] WS5 plugin seam + first plugins
- [ ] WS6 packaging (global tool, CI action, repo CI)
- [ ] WS7 production-mode analysis / test-only reachability (Appendix A category K)

---

## Appendix A — Test battery (the feature contract)

Every row is one test. IDs are stable — reference them in commits, skips, and task prompts.
For each scenario the fixture contains BOTH the used and the unused variant where applicable,
and the test asserts the exact finding set: **live code is never flagged; the dead sibling is.**

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
| B6 `D` | Two projects declare identical namespace+type+signature (doc-comment IDs collide, no assembly in key) | document collision behavior; decide fix vs. known limitation |

### C. Overloads, generics, delegates

| ID | Scenario | Expected |
|---|---|---|
| C1 `C` | Two overloads, one called | the other flagged — the flagship's precision |
| C2 `C` | Overload resolution fails (unresolved arg type) | ALL candidates alive — invariant #3 |
| C3 `C` | `services.AddScoped<IFoo, Foo>()` | `Foo` alive via generic-arg edge |
| C4 `C` | Method group passed as delegate: `list.Select(Transform)` | `Transform` alive |
| C5 `C` | `nameof(Method)` as only reference | alive (conservative: name-token counts as use) |
| C6 `C` | Extension method called via extension syntax; unused sibling | used alive, sibling flagged |
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

### E. Implicitly-invoked members — suspected core gaps (WS1b feed)

No `IdentifierName`/`GenericName`/object-creation node appears at the use site, so the current
walker likely records no edge → **false positive on plain C#**. Highest-priority fixes.

| ID | Scenario | Expected |
|---|---|---|
| E1 `G-core` | Custom indexer used via `obj[i]` | indexer alive |
| E2 `G-core` | `operator +` used via `a + b` | operator alive |
| E3 `G-core` | Implicit conversion operator used via assignment/argument | operator alive |
| E4 `G-core` | `operator ==`/`!=` used via comparison | operators alive |
| E5 `G-core` | `foreach` over custom (non-interface, pattern-based) enumerable | `GetEnumerator`/`MoveNext`/`Current` alive |
| E6 `G-core` | `await` on custom awaitable | `GetAwaiter`/`IsCompleted`/`GetResult` alive |
| E7 `G-core` | Pattern-based `Dispose` (ref struct in `using`) | `Dispose` alive |
| E8 `G-core` | Collection initializer `new C { 1, 2 }` | `Add` alive |
| E9 `G-core` | Tuple deconstruction `var (a, b) = obj` | `Deconstruct` alive |
| E10 `G-core` | LINQ query syntax over custom provider | `Select`/`Where`/`SelectMany` alive |
| E11 `G-core` | Index/Range pattern members `obj[^1]`, `obj[1..]` | `Length`/`Slice` alive |
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

### G. Language corners

| ID | Scenario | Expected |
|---|---|---|
| G1 `C` | Unused local function | not reported (member-level tool; Roslyn's own analyzers cover it) |
| G2 `C` | Records: unused record flagged; used positional record's synthesized members | never reported |
| G3 `C` | Primary-constructor class (C# 12), used | no spurious findings |
| G4 `C` | `async` methods and iterators (`yield`) | treated as normal methods |
| G5 `C` | Nested private type used only by outer type | alive |
| G6 `C` | Enum members | never reported (documented limitation — pin it) |
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
| H4 `G-moat` | Assembly-scanning DI (Scrutor `.FromAssemblyOf<>`), MediatR handlers, AutoMapper profiles | alive | `entryPoints.implementedInterfaces`/`baseTypes` |
| H5 `G-moat` | DTO properties touched only by JSON serializer (STJ/Newtonsoft) | alive | `ignore.symbols` on DTO namespaces |
| H6 `G-moat` | Blazor `[Parameter]` properties set from markup; `@bind` targets | alive | `entryPoints.attributes: ["Parameter"]` |
| H7 `G-moat` | XAML binding targets (WPF/MAUI) | alive | `ignore` config |
| H8 `G-moat` | WebForms `.aspx`/`.ascx` code-behind referenced only from markup (WS4-relevant) | alive | `ignore.files` on code-behind |
| H9 `G-moat` | Types referenced only from `web.config`/`app.config` (WCF services, HTTP modules, providers) | alive | `ignore.symbols` |
| H10 `G-moat` | `dynamic` dispatch `((dynamic)x).M()` | alive (undecidable — document as designed FP + mitigation) | `ignore.symbols` |
| H11 `D` | Source-generated code references user symbols, but `**/*.g.cs` is ignored by default → edges FROM generated code are lost | decide: always walk generated trees for edges while never reporting their declarations | config today |
| H12 `G-moat` | MassTransit consumers registered via `AddConsumer`/scanning | alive | `entryPoints` |

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
| K7 `D` | Test-project classification default (`IsTestProject` prop vs package refs vs name globs) and whether production mode warns when zero test projects are detected | decide with the human |
