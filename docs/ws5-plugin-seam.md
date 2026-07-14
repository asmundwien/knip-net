# WS5 — Plugin Seam Design Proposal (for sign-off)

**Status:** DESIGN ONLY — no production code changed. Needs human sign-off before WS5 implementation.
**Scope:** the seam a plugin implements to contribute EXTRA ROOTS and EXTRA EDGES given a `Compilation`,
so "invisible" usages (reflection, scanning DI, MediatR/MassTransit, serialized DTOs, Blazor `[Parameter]`)
stay alive and don't become false positives (§3.8 — the product risk).

This proposal is written against the code as it exists today:
`DeadCodeAnalyzer.AnalyzeAsync` (`src/Knip.Core/Analysis/DeadCodeAnalyzer.cs`),
`ReferenceWalker` (`AddEdge`/`EvaluateRoots`), `GraphState`, `SymbolId.For`, `KnipConfig`.

---

## 0. Design constraints pulled from the code / invariants

- All graph state is string-keyed via `SymbolId.For(...)` — `GraphState.Declared/Edges/Roots`
  are `Dictionary<string,…>`/`HashSet<string>` with `StringComparer.Ordinal` (invariant #1).
  **A plugin must never see or produce a string key** — it hands us `ISymbol`s and we route them
  through `SymbolId.For`. This makes invariant #1 unbreakable by construction.
- `ReferenceWalker.AddEdge` already drops any target whose `OriginalDefinition.ContainingAssembly`
  is not in `_solutionAssemblies` (invariant #5). The seam reuses exactly this filter.
- A root or an edge can only ever *add* reachability. There is no plugin operation that removes
  a node, an edge, or a root. Worst case a buggy plugin keeps something alive that is actually dead
  → a **false negative**, which §3.8 explicitly prefers over a false positive. **Plugins cannot make
  code look more dead.** (See §3.)
- `Knip.Core` stays CLI-free and locator-free (invariant #9). Built-in plugins live in
  `Knip.Core`; the seam interface lives in `Knip.Core`. External-assembly loading, if we ever do it,
  lives in the CLI (see §2, deferred).

---

## 1. Seam interface(s)

### 1.1 The plugin contract

A plugin is handed, for each C# project, its `Compilation` (from which it can call
`compilation.GetSemanticModel(tree)` for any tree it wants), plus a **contribution sink** that
accepts `ISymbol`s only. The sink — not the plugin — owns key derivation and the solution-assembly
filter, so the plugin physically cannot violate invariants #1 or #5.

```csharp
namespace Knip.Core.Plugins;

using Microsoft.CodeAnalysis;

/// <summary>
/// A plugin contributes EXTRA roots and EXTRA edges for usages the core walker cannot see
/// (reflection, scanning DI, serialization, markup binding, …). It may only ADD reachability;
/// it can never remove a node, edge, or root. See docs/ws5-plugin-seam.md.
/// </summary>
public interface IKnipPlugin
{
    /// <summary>Stable id used in config to enable/disable this plugin, e.g. "reflection", "scanning-di".</summary>
    string Id { get; }

    /// <summary>
    /// Inspect one project's compilation and contribute roots/edges via <paramref name="sink"/>.
    /// Runs once per C# project, AFTER the core walk of that project, BEFORE global traversal.
    /// Called even for plugins that end up contributing nothing.
    /// </summary>
    void Contribute(PluginContext context, IContributionSink sink, CancellationToken ct);
}

/// <summary>Everything a plugin is allowed to look at. One project's world.</summary>
public sealed class PluginContext
{
    /// <summary>The current project's compilation. Get semantic models from this as needed.</summary>
    public Compilation Compilation { get; }

    /// <summary>The project being analyzed (name, file path — for diagnostics / project-scoped rules).</summary>
    public Project Project { get; }

    /// <summary>Read-only view of the plugin's own config block from knip.json (see §2). Never null.</summary>
    public PluginSettings Settings { get; }

    // ctor internal — only the analyzer constructs these.
}

/// <summary>
/// The ONLY way a plugin mutates the graph. Symbol-typed on the way in; the sink derives the
/// SymbolId key and applies the solution-assembly filter internally, so invariants #1 and #5
/// hold no matter what the plugin does. Every method is additive.
/// </summary>
public interface IContributionSink
{
    /// <summary>Mark <paramref name="symbol"/> as a root (reachability starts here). No-op if it
    /// has no SymbolId (dynamic/error symbols) or is not solution-defined.</summary>
    void AddRoot(ISymbol symbol);

    /// <summary>Record a "uses" edge <paramref name="from"/> → <paramref name="to"/>. No-op if either
    /// end lacks a SymbolId or <paramref name="to"/> is not solution-defined (mirrors walker's AddEdge).</summary>
    void AddEdge(ISymbol from, ISymbol to);
}
```

The sink implementation is trivial and lives beside `GraphState` in `Knip.Core.Analysis`. It is the
single choke point that guarantees the invariants:

```csharp
// internal, in Knip.Core.Analysis — the analyzer wires this to the live GraphState.
internal sealed class ContributionSink : IContributionSink
{
    private readonly GraphState _state;
    private readonly IReadOnlySet<string> _solutionAssemblies;
    // ...ctor...

    public void AddRoot(ISymbol symbol)
    {
        if (!IsSolutionDefined(symbol)) return;          // invariant #5: only solution symbols are nodes
        if (SymbolId.For(symbol) is { } id) _state.Roots.Add(id); // invariant #1: key derivation owned here
    }

    public void AddEdge(ISymbol from, ISymbol to)
    {
        if (!IsSolutionDefined(to)) return;              // invariant #5 (same rule as ReferenceWalker.AddEdge)
        if (SymbolId.For(from) is { } f && SymbolId.For(to) is { } t &&
            !string.Equals(f, t, StringComparison.Ordinal))
            _state.AddEdge(f, t);
    }

    private bool IsSolutionDefined(ISymbol s) =>
        s.OriginalDefinition.ContainingAssembly?.Name is { } a && _solutionAssemblies.Contains(a);
}
```

Note `AddRoot` also enforces #5: a root must be a solution-defined symbol, otherwise it can never
match a `Declared` key and `Traverse` ignores it anyway — filtering here keeps semantics honest and
mirrors the existing root-seeding, which only ever adds `SymbolId`s of declared solution symbols.

### 1.2 Whole-solution vs one-project-at-a-time

**One project at a time**, matching the existing per-project walk loop. Rationale:

- The core walk is already per-project (`foreach (var project in projects)` →
  `project.GetCompilationAsync`). Handing plugins the same `Compilation` reuses the compilation we
  already materialized — no extra load, no whole-solution `Compilation` (there is no such thing in
  Roslyn anyway).
- Cross-project reachability is preserved regardless: keys are assembly-qualified `SymbolId`s, so an
  edge a plugin adds from project A to a symbol *defined* in project B unifies with B's declared node
  automatically (same mechanism the core walk relies on). A plugin does **not** need the whole
  solution to keep a cross-project symbol alive.
- The genuinely solution-global concern — "which assemblies count as in-solution" — is already
  computed once (`solutionAssemblies`) and is injected into the sink, not exposed to the plugin.

If a future plugin truly needs solution-global state (e.g. "collect all `IConsumer<T>` across every
project, then edge registrations to them"), it accumulates across its per-project `Contribute` calls
in its own field and flushes on the last call — but we defer adding a whole-solution hook until a
plugin demonstrably needs it (open question Q4).

### 1.3 When plugins run relative to the core walk and `AddPolymorphismEdges`

Order inside `AnalyzeAsync`:

1. Seed entry-point roots + run `ReferenceWalker` over every tree of the project (**unchanged**).
2. **NEW: run each enabled plugin's `Contribute` for this project**, against the same `compilation`,
   with a sink bound to the live `GraphState` and `solutionAssemblies`.
3. After the project loop: `AddPolymorphismEdges(state)` (**unchanged**).
4. `Traverse(state)` → findings.

Plugins run **after** the core per-project walk (so declared nodes exist to edge to) and **before**
`AddPolymorphismEdges` (so a plugin that roots an interface-implementing type benefits from
polymorphism edges keeping its overrides/impls alive — e.g. a scanning-DI plugin roots a `Consumer`
type and polymorphism then keeps its interface-method impls alive for free). They run **before**
`Traverse`, which is the whole point — their contributions must be visible to reachability.

Concretely, the only edit to `DeadCodeAnalyzer.AnalyzeAsync` is inserting one call inside the existing
project loop, after the tree-walk block:

```csharp
// after the `foreach (var tree ...)` walk, still inside `foreach (var project in projects)`:
foreach (var plugin in _plugins)          // _plugins resolved once from config in the ctor
{
    ct.ThrowIfCancellationRequested();
    var context = new PluginContext(compilation, project, _config.PluginSettings(plugin.Id));
    plugin.Contribute(context, sink, ct); // sink bound to `state` + `solutionAssemblies`
}
```

`AddPolymorphismEdges`, `Traverse`, `BuildFindings`, `ShouldReport` — all untouched.

---

## 2. Registration / discovery

### 2.1 Built-in plugins, config-gated

For WS5 v1 all plugins are **built-in** to `Knip.Core` and registered in a static list. They are
**opt-in per plugin** via config, because a plugin's job is to relax detection (keep more alive) and
the org should choose which relaxations apply. Defaults are conservative (see §5 for which ship on).

Resolution: `DeadCodeAnalyzer` builds its `_plugins` list from the built-in registry filtered by
config at construction time. No reflection, no assembly scanning of our own process.

### 2.2 NEW `knip.json` keys — REQUIRES SIGN-OFF (user-facing API)

The following are **new user-facing config** and are called out explicitly for human decision. Nothing
below is assumed adopted.

Proposed shape — a single new top-level `plugins` block:

```jsonc
{
  "plugins": {
    "reflection":  { "enabled": true },
    "scanningDi":  { "enabled": true },
    "serialization": {
      "enabled": true,
      // plugin-specific settings live under the plugin's own object; schema is per-plugin.
      "namespaces": ["MyApp.Contracts.*", "MyApp.Dtos.*"]
    },
    "blazorParameter": { "enabled": true }
  }
}
```

Mapping to `KnipConfig` (design; not implemented):

```csharp
// new property on KnipConfig
public Dictionary<string, PluginSettings> Plugins { get; set; } = new();

// PluginSettings: `enabled` + a free-form bag for plugin-specific keys (raw JsonElement),
// exposed to plugins read-only via PluginContext.Settings. Unknown plugin ids are ignored
// (with a load diagnostic) so a newer config doesn't break an older binary.
```

**New keys introduced (the sign-off list):**
- `plugins` (top-level object) — map of plugin id → settings.
- `plugins.<id>.enabled` (bool) — turn a built-in plugin on/off.
- `plugins.<id>.*` — per-plugin settings (e.g. `serialization.namespaces`,
  `blazorParameter.attributes`). Each plugin documents its own sub-keys.

**Explicitly NOT proposed for v1** (deferred to keep the sign-off small):
- External-assembly plugin loading (a `pluginAssemblies: [...]` key). This is a security surface
  (arbitrary code from the analyzed repo's config) and a CLI-layer concern (invariant #9), so it is
  an open question (Q1), not part of v1.
- Per-project plugin overrides.

---

## 3. Invariant safety

| Invariant | How the seam upholds it |
|---|---|
| **#1 string keys** | Plugins never touch strings. `SymbolId.For` is called **only** inside the sink. There is no plugin-facing API that accepts or returns a key. Any diff that adds a string-keyed method to the sink is rejected. |
| **#5 edges only to solution assemblies** | The sink applies the exact `_solutionAssemblies.Contains(assembly)` filter that `ReferenceWalker.AddEdge` uses, to both `AddEdge` targets and `AddRoot` symbols. BCL/NuGet symbols silently no-op. |
| **#3.8 no new false positives** | The sink is **additive-only**: `AddRoot` and `AddEdge` grow reachability; there is no remove/suppress/exclude verb. A misbehaving plugin can only keep dead code alive (false negative), never flag live code. This is the §3.8 bias made structural. |
| **#9 Core stays CLI/locator-free** | Seam + built-in plugins live in `Knip.Core.Plugins`, pure Roslyn, no MSBuild/CLI types. External loading (if ever) is CLI-only and out of v1. |
| **#7 reporting rules** | Untouched — plugins feed the graph *before* `Traverse`; `ShouldReport`/`AddPolymorphismEdges` are unchanged, so outermost-only, no-ctor, override/impl rules still hold. |

A plugin cannot: delete a node, remove an edge, mark something dead, change a key scheme, edge to a
non-solution symbol, or run after traversal. The API surface simply doesn't expose those verbs.

---

## 4. Testing model

Each plugin ships with a **self-contained fixture** under `tests/fixtures/CatH/` proving the exact
false positive it kills, mapped to an Appendix-A category-H row (H1–H12). The assertion pattern is the
**differential** already used by the battery's anti-vacuous-green rule (`Skip`+status today):

> **flagged WITHOUT the plugin, alive WITH the plugin.**

Test shape (design):

```csharp
[Fact]
public async Task Reflection_TypeGetTypeString_KeepsTypeAlive_H2()
{
    // plugin OFF: the reflection-only type is a false positive → present in findings.
    var off = await Run(fixture: "CatH.H2", config: Plugins(reflection: false));
    Assert.Contains(off.Findings, f => f.Symbol == "CatH.H2.ReflectivelyCreated");

    // plugin ON: the plugin roots the type named in the string → alive → NOT reported.
    var on = await Run(fixture: "CatH.H2", config: Plugins(reflection: true));
    Assert.DoesNotContain(on.Findings, f => f.Symbol == "CatH.H2.ReflectivelyCreated");
}
```

The plugin-OFF run **is** the dead sibling: it is the same fixture with the use-site (the plugin)
removed, asserted flagged — satisfying the anti-vacuous-green rule without needing a hand-written
sibling symbol. Promotion path: each H row is `Skip`-tagged today (`status = G-moat`, "WS5"); landing
a plugin means un-skipping its H row and flipping it to Contract-green — features land by promotion,
never by prose (per the WS1 rule). No Contract test may regress: the plugin-OFF assertions double as a
guard that the plugin didn't broaden liveness beyond its fixture.

Each plugin's fixture is isolated in its own namespace (`CatH.H2`, `CatH.H4`, …) with no cross-namespace
references, so one plugin's roots can't accidentally keep another scenario alive (matches WS1 fixture
architecture).

---

## 5. First plugins & order

Ordered by value-per-risk and by how directly each promotes a category-H row:

1. **`reflection` — Reflection / `Type.GetType` string literals → promotes H1, H2.**
   Scans invocations of `Type.GetType("Ns.Foo")`, `Activator.CreateInstance(typeof(Foo))`,
   `assembly.GetType("…")`, and `GetMethod("X")` chains; resolves string literals to type/member
   symbols via `compilation.GetTypeByMetadataName(...)` / semantic model and `AddRoot`s them.
   Highest-frequency invisible usage; today only mitigated by hand-written `ignore.symbols`.

2. **`scanningDi` — Assembly-scanning / non-generic DI → promotes H4, H12 (and hardens H3).**
   Recognizes Scrutor `.FromAssemblyOf<T>()/.AddClasses().AsImplementedInterfaces()`,
   MediatR/MassTransit registration (`AddConsumer`, handler scanning), `AddScoped(typeof(IFoo),
   typeof(Foo))`. Roots the registered/scanned implementation types (and lets `AddPolymorphismEdges`
   carry their interface-method impls). This is the classic "paid-tool moat" case.

3. **`blazorParameter` (attribute-driven) → promotes H6.**
   Roots properties/fields carrying `[Parameter]`/`[CascadingParameter]` (and, configurably, other
   framework attributes) that are set from markup, never from C#. Simplest to implement and a clean
   template for the attribute-based family (H5 serialization DTOs, H7/H9 markup/config can follow the
   same attribute/namespace pattern). Note H6 has a *partial* mitigation today
   (`entryPoints.attributes: ["Parameter"]`); the plugin makes it correct and default-on rather than a
   documented workaround.

**Default-on vs default-off (proposal, needs sign-off):** `reflection` and `scanningDi` default **on**
(their false positives are common and their contributions are tightly targeted). `blazorParameter`
and `serialization` default **off** unless the solution looks like Blazor/uses the relevant packages —
final defaults are Q3 for the human.

### Sample plugin sketch (`reflection`, abridged)

```csharp
namespace Knip.Core.Plugins.BuiltIn;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal sealed class ReflectionPlugin : IKnipPlugin
{
    public string Id => "reflection";

    public void Contribute(PluginContext ctx, IContributionSink sink, CancellationToken ct)
    {
        var compilation = ctx.Compilation;
        foreach (var tree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(ct);

            foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(inv, ct).Symbol is not IMethodSymbol m) continue;
                var name = m.ToDisplayString();
                // Type.GetType("Ns.Foo") / Assembly.GetType("Ns.Foo")
                if (name is "System.Type.GetType(string)" or "System.Reflection.Assembly.GetType(string)"
                    && inv.ArgumentList.Arguments is [{ Expression: LiteralExpressionSyntax lit }, ..]
                    && lit.Token.ValueText is { } typeName
                    && compilation.GetTypeByMetadataName(typeName) is { } type)
                {
                    sink.AddRoot(type);                    // keeps H2 alive; sink handles key + #5 filter
                }
                // (similar arms for Activator.CreateInstance(typeof(T)), GetMethod("X") → member roots for H1)
            }
        }
    }
}
```

The plugin only ever calls `sink.AddRoot` / `sink.AddEdge` with `ISymbol`s — it never sees a
`SymbolId`, never touches `GraphState`, and any non-solution target it resolves is dropped by the sink.

---

## 6. Open questions for the human

- **Q1 — External plugins & security.** Do we ever want to load plugin assemblies referenced by the
  analyzed repo's `knip.json` (a `pluginAssemblies` key)? That is arbitrary code execution driven by a
  file in the target repo — a real supply-chain surface. Proposal: **no** for v1 (built-in only); revisit
  as a CLI-layer, explicitly-trusted, allow-listed feature later. Confirm.
- **Q2 — Config key shape.** Sign off on the `plugins.<id>.enabled` + per-plugin sub-object shape
  (§2.2), including how per-plugin settings are exposed (raw `JsonElement` bag vs typed options per
  plugin). This is the user-facing API.
- **Q3 — Default-on set.** Which built-in plugins ship enabled by default? Proposal: `reflection` +
  `scanningDi` on; `blazorParameter` + `serialization` off unless detected. This directly trades false
  negatives (too much on) against surprise (users seeing code kept alive they expected flagged).
- **Q4 — Whole-solution hook.** Is per-project `Contribute` (plugins accumulate their own cross-project
  state) sufficient, or do we want an explicit end-of-solution `Finish(sink)` pass? Proposal: defer
  until a plugin needs it; add non-breakingly later as an optional interface.
- **Q5 — Perf.** Plugins re-`GetSemanticModel`/re-walk trees the core walker already visited (roughly
  doubling semantic work for enabled plugins). Acceptable at current scale (~2 s / 10 projects) but
  unvalidated at portfolio scale (§4 env note). Do we (a) accept it for v1, or (b) invest in a shared
  visitor pass where the core walk and plugins share one tree traversal? Proposal: (a) for v1, measure
  on the first large solution, optimize only if it hurts.
