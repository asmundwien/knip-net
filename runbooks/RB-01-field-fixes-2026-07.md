# RB-01 field fixes from the July 2026 field run

**Status.** Tasks A and B shipped. Task C remains open. The completed sections below are the accepted contracts and the record of why those changes exist.

**Audience:** an orchestrator agent executing this runbook by dispatching implementer and reviewer
subagents. You do not need to invent anything — every task below has a defined contract, a defined
seam, and a defined gate. When this runbook and your own judgment disagree, follow the runbook; when
the runbook and `RUNBOOK.md` (repo root) disagree, follow `RUNBOOK.md` and escalate.

**Origin:** a supervised field run of `dotnet-knip` against a real internal .NET 8 backend
(2026-07-16). The verify loop, `rootCause` gating, cascade re-run behavior, and reliability block
all worked as designed. Three gaps were found. Per `RUNBOOK.md` §7, **no source, paths, or analysis
output from the field solution may appear in this repo** — every reproduction below is a synthetic
shape that reproduces the observed behavior.

**Pre-approvals (Åsmund, 2026-07-16):** the new fixture rows, the new hazard-detection rules, and
the new `unusedProject` finding kind in this runbook are owner-approved. The §6 escalations for
"new confidence/hazard demotion rule" and "output-schema change" are satisfied *for the scopes
defined here* — anything beyond these scopes still escalates.

---

## Ground rules for every task

1. **Tests are the contract — red first.** For each task: commit the fixture + failing test first,
   confirm it is RED for the right reason, then implement until GREEN. An implementer subagent that
   writes the fix before the red test has its work discarded.
2. Every ALIVE assertion ships with a DEAD SIBLING in the same fixture (see `CatDTests.cs` D8/D9 for
   the canonical pair).
3. **Never edit an existing test assertion or fixture.** If a task seems to require it, stop and
   escalate (RUNBOOK.md §5 review rule 3).
4. One branch per task (`rb01/task-a`, `rb01/task-b`, `rb01/task-c`), gate before merge:

   ```bash
   dotnet build Knip.slnx -warnaserror
   dotnet test tests/Knip.Core.Tests/Knip.Core.Tests.csproj --no-build
   dotnet run --project src/Knip.Cli -- tests/fixtures/<category>/Fixture.slnx
   ```

5. After each implementation, dispatch an **independent reviewer subagent** that checks the diff
   against `RUNBOOK.md` §3 invariants by number and §5 review rules. The reviewer must not be the
   implementer.
6. Two consecutive gate failures on a task → stop, summarize, escalate (RUNBOOK.md §6).
7. Tasks are independent; run them in order (A is a correctness bug, B and C are features). Do not
   parallelize A and B — both touch the analyzer/enrichment layer.

---

## Task A — BUG: external-interface implementations don't propagate liveness (high-confidence FP)

**Severity: highest.** This is a `confidence: high`, `rootCause: null`, `remediation: deleteSymbol`
false positive — the exact category invariant #8 calls UNACCEPTABLE-adjacent: an agent following the
protocol deletes the symbol and gets a build break.

### Observed shape (synthetic reproduction)

```csharp
public class FakeLogger : ILogger          // ILogger is EXTERNAL (Microsoft.Extensions.Logging)
{
    public IDisposable BeginScope<TState>(TState state) => new ScopeStub();

    private sealed class ScopeStub : IDisposable   // ← flagged unusedType, HIGH, rootCause null
    {
        public void Dispose() { }
    }
    // Log(...) / IsEnabled(...) omitted
}
```

`FakeLogger` is alive (used by tests). `BeginScope` is an implementation of an **external**
interface member. Nothing in the solution calls it, so it is unreachable in the graph; invariant #7
suppresses it from *reporting*, but its body edges (`new ScopeStub()`) are never traversed, so
`ScopeStub` is flagged dead at high confidence even though it is instantiated three lines away.

### Root cause (verified in source)

`DeadCodeAnalyzer.AddPolymorphismEdges` links interface member → implementation
(`Link(state, member, impl)`). For a **solution-internal** interface that works: the interface
member is a graph node, and when it is alive the impl becomes alive. For an **external** interface
(`ILogger`), the interface member is not a solution node (invariant #5), so the edge source is
permanently unreachable and the impl never becomes reachable.

The same problem was already fixed for external **virtual overrides** — see the `(FIX #5)` block in
`AddPolymorphismEdges` and `IsOverrideOfExternalVirtual`: an override of an external virtual gets a
**containing-type → override** edge, so it is reachable exactly when its type is. External-interface
implementations need the symmetric edge.

### Contract (write first, confirm RED)

Add two rows to **CatD** (the polymorphism category), mirroring D8/D9 exactly:

- **D10 (fix):** a REACHABLE type implementing an EXTERNAL interface (use a BCL interface available
  in the fixture's TFM, e.g. `IDisposable` with a `Dispose()` that calls a private helper, or an
  `ILogger`-like shape if the fixture can reference `Microsoft.Extensions.Logging.Abstractions`)
  keeps the implementation member's private callees alive. Assert the private callee
  (`new`-ed nested class or called private method) is NOT in the finding set, and include a dead
  sibling (an identical private nested class that nothing instantiates) that IS flagged.
- **D11 (false-negative guard):** a DEAD type implementing the same external interface stays fully
  dead and is reported outermost-only — proving the new edge introduces no false negative
  (mirror D9's rationale comment).

Extend the existing `tests/fixtures/CatD/` fixture with new namespaces (`CatD.D10`, `CatD.D11`);
do not modify existing D1–D9 fixture files.

### Implementation seam

`src/Knip.Core/Analysis/DeadCodeAnalyzer.cs`, in the interface-implementation loop inside
`AddPolymorphismEdges`: when `iface` is declared in a **non-solution** assembly (same test shape as
`IsOverrideOfExternalVirtual` — check the interface's `ContainingAssembly` against
`solutionAssemblies`), add `Link(state, type, impl)` (containing-type → implementation). Keep the
existing `Link(state, member, impl)` for solution-internal interfaces unchanged. Follow the FIX #5
comment style: document *why* (external interface member is not a graph node; runtime/framework
dispatches the impl), and note the false-negative guard (dead type → edge source unreachable →
still dead).

**Do not** root the impl unconditionally (that would resurrect implementations on dead types and
regress D-category assertions), and **do not** relax invariant #5 by making external symbols nodes.

### Gate additions

Standard gate, plus eyeball `dotnet run --project src/Knip.Cli -- tests/fixtures/CatD/Fixture.slnx`
and confirm D10's callee no longer appears while D11's dead type still does.

---

## Task B: runtime-only hazard detection

**Status.** Shipped. `RuntimeHazardDetector` attaches `serializationShaped` and `configBoundType`; uncertain dependency injection activation attaches `diPluginShaped`. `ConfidenceModel` demotes these findings to low. The active CatL and plugin tests hold the contract.

**Field motivation:** in the field run, a property was deleted from a DTO that is deserialized via
`JsonConvert.DeserializeObject<T>` elsewhere in the solution. The deletion happened to be
behaviorally safe (no reader of the property; extra JSON fields are ignored), but the finding
carried `hazards: []` — the agent had no signal to verify harder. Config-bound POCO properties
(classes bound via `IConfiguration.GetSection(...).Get<T>()` / `Bind` / `Configure<T>`) were
likewise flagged with no hazard. Across the entire 95-finding run only `publicApi` and
`buildOnlyPackage` ever appeared.

### Contract (in order)

1. **Read the skipped CatL test first.** It defines the already-agreed contract shape. Promote it
   by removing the skip ONLY once the detector makes it green — promoting a skip is the one
   permitted test edit (RUNBOOK.md §5 rule 3).
2. Add new CatL contract rows (new fixture namespaces, new tests — additive only) covering the
   field shapes, each asserting **hazard presence and the demoted confidence tier**
   (§5 rule 2: a new demotion rule ships with a fixture asserting the tier):
   - **Attribute-shaped serialization** (if not already in the skipped row): symbol or containing
     type carries `[JsonProperty]`, `[JsonPropertyName]`, `[DataMember]`, `[Serializable]` →
     `serializationShaped`.
   - **Usage-shaped serialization:** a type that appears as a type argument (or argument type) of a
     recognized serializer call — `JsonConvert.DeserializeObject<T>` / `SerializeObject`,
     `System.Text.Json.JsonSerializer.Serialize/Deserialize<T>` — has its unused **data members**
     (public properties/fields) tagged `serializationShaped`. This is the field shape. Sibling
     assertion: an unused *method* on the same DTO is NOT tagged (methods are not serialized).
   - **Config-bound:** a type passed to `IConfiguration.Get<T>()`, `.Bind(instance)`,
     `Configure<T>(section)`, or `GetSection(...).Get<T>()` has its unused public properties tagged
     `configBoundType`. Sibling: an identical un-bound POCO's properties carry no hazard.
3. Each row's DEAD SIBLING proves the detector doesn't over-tag.

### Implementation seam

Hazard *tagging* belongs in the analysis/enrichment layer, not the plugins-that-add-roots layer:
plugins are add-only reachability contributors (RUNBOOK.md §8 — "can keep code alive, never mark
live code dead"), whereas hazards annotate findings. Look at how `Hazard.PublicApi` is attached
today (`FindingEnrichment.cs` / `DeadCodeAnalyzer.BuildFindings`) and attach the new hazards in the
same place. Detection data (which types are serializer/config-touched) can be collected during the
walk (`ReferenceWalker`) or by the existing `serialization` plugin's visitor if reusable — but the
hazard attachment itself must fire even when the plugin is disabled, because hazards are advisory
metadata, not reachability.

Keep detection **name-based and conservative**: match on well-known method names + containing type
names (`JsonConvert`, `JsonSerializer`, `IConfiguration`, `OptionsConfigurationServiceCollectionExtensions`)
without requiring the packages to be referenced by Knip itself. False *hazard* positives are cheap
(hazards never change the emitted set — `ConfidenceModel` doc); false hazard *negatives* are the
expensive direction. When in doubt, tag.

### Notes

- `ConfidenceModel` needs no change: C2 already demotes non-publicApi hazards to low. Add a CatL
  assertion proving a serialization-hazard finding lands `low`.
- Update `schemas/knip.output.schema.json` hazard enum values and README **in the same commit** as
  the detector (RUNBOOK.md §5 diff checks). The enum values already exist in code — verify the
  schema already lists them; if it does, no schema change is needed.

---

## Task C — FEATURE: `unusedProject` finding (the dead-test-project blind spot)

**Field motivation:** the analyzed solution contained a test project with **zero test methods** —
no `[TestClass]`/`[TestMethod]`/`[Fact]` anywhere — referenced by no other project, containing only
leftover test infrastructure. The tool saw all the pieces (its types flagged low with cascade
rootCauses, its stale project refs medium) but the headline — *this entire project is dead* — was
never stated. Test projects are roots by design, so a testless test project is structurally
invisible: nothing in it is a root, but nothing points at it either, and the finding that matters
is project-level, not symbol-level.

### Contract (write first, confirm RED)

New category **CatM** (or extend CatK if the reviewer judges it closer to the K "classification"
theme — K7 already covers the *production-mode zero-test-project warning*, which is adjacent but
different). Fixture: a solution with

1. a production project (alive),
2. a **dead test project**: classified as a test project (by `TestProjectClassifier` signals), zero
   test-attribute entry points, referenced by no other project → expect ONE
   `unusedProject` finding for it (plus its symbol-level findings as today — do not suppress them;
   outermost-only applies to the symbol graph, not across the project boundary),
3. a **live test project** with one `[Fact]` (dead sibling logic inverted: proves no over-flagging),
4. a **testless test project that IS referenced** by the live test project (e.g. shared test infra)
   → NOT flagged (the reference keeps it).

Assertions: exact finding presence/absence per project, `kind == unusedProject`,
`confidence == medium` (C3 tier — same reasoning as project references: structural findings whose
verification is cheap), `remediation` string `"removeProject"`, `span == null`.

`span == null` is deliberate: per the agent protocol (§4, `Resources/AgentInstructions.md`),
findings without a span are **surfaced, never auto-deleted** — removing a project from a solution
is always a human-reviewed act in v1.

### Detection rule (v1, keep it narrow)

Flag project P as `unusedProject` when ALL hold:

- P is classified as a **test project** (`TestProjectClassifier`) — v1 scopes to test projects
  only; dead *production* projects are usually caught as a cascade of publicApi findings and have
  API-posture implications this rule must not judge,
- P contributes **zero roots** (no test entry points found by the walker),
- no other solution project has a `ProjectReference` to P.

### Implementation seam

`DeadCodeAnalyzer` already has everything needed at the point where
`BuildProjectReferenceFindings` runs: per-project root counts (`GraphState`), the classification
list, and the project-reference table. Add `BuildUnusedProjectFindings` alongside it. New enum
member in `FindingKind` (`Finding.cs`) with a doc comment matching the house style.

**Schema + docs (same commit):** `schemas/knip.output.schema.json` gains the new `kind` and
`remediation` values; README's finding-kind table gains a row;
`src/Knip.Cli/Resources/AgentInstructions.md` gains one line in §3/§4 territory stating that
`unusedProject` findings are always surface-only (span is null). Touching AgentInstructions.md is a
protocol-surface change — flag it explicitly in the PR description even though pre-approved here.

---

## Optional validation (owner-run, not committable)

After all three tasks merge: Åsmund re-runs `dotnet-knip --format json --no-fail` against the field
solution (requires authenticated private feed — sandboxes get 401, RUNBOOK.md §4) and confirms:

1. the two `TestDisposable`-shaped high/rootCause-null findings are gone (Task A),
2. serialization/config-bound findings now carry hazards (Task B),
3. the dead test project yields one `unusedProject` finding (Task C).

Results stay out of this repo.

## Explicitly out of scope for this runbook

- Unused-configuration detection (dead `appsettings.json` keys) — **RB-02**, its own iteration.
- The remaining WS5 moat plugins (XAML, WebForms, web.config type refs, dynamic) — their skips
  stay skipped.
- Any relaxation of invariant #3/#5/#7 semantics beyond the two edges specified in Task A.
