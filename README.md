# Knip.NET

[Knip](https://knip.dev) for .NET — a **free, solution-wide dead-code finder** built on Roslyn.
The paid tools (ReSharper/Rider, NDepend) own this space; the free Roslyn analyzers only catch
*private* unused members inside a single file. Knip.NET closes the gap: it reports code that is
**unreferenced across the entire solution**, including public/internal types and members.

Runs locally and in CI, and is **configured entirely in code** (`knip.json`).

**AI agents are first-class users** — the JSON v2 output is the product API. If you are (or are driving)
an agent that consumes Knip.NET, run `dotnet-knip --agent-instructions` for the canonical protocol, or
read [`AGENTS.md`](./AGENTS.md) for the same run → triage → delete → verify → PR recipe with the
confidence/hazard autonomy rules and a full JSON v2 example.

> Status: **working prototype.** Flagship feature (dead code) is implemented and validated on real
> solutions. Unused `<ProjectReference>` and unused `<PackageReference>` (NuGet) detection are both
> implemented.

## How it works

1. **Load** the `.sln`/`.slnx`/`.csproj` via Roslyn's `MSBuildWorkspace` → every project's
   `Compilation` + `SemanticModel`.
2. **Build a reachability graph.** Walk every syntax tree, recording each declared symbol and the
   "uses" edges from it (signature types, attributes, method-body references, `new`, generic args —
   so e.g. `AddScoped<IFoo, Foo>()` keeps `Foo` alive). Graph nodes are keyed by **documentation
   comment ID** (`M:Ns.Type.Method(...)`), which unifies a symbol across project boundaries — the
   critical trick that makes cross-project analysis correct (Roslyn does *not* give you reference
   equality for a symbol seen from source vs. from a referenced assembly).
3. **Seed roots** from configurable entry points: `Main`, `[Fact]`/`[HttpGet]`/… attributes,
   `*Controller` and `ControllerBase` subtypes, `IHostedService` implementers, DI-registered types,
   and (optionally) the public API of library projects.
4. **Mark and sweep.** BFS from roots over the graph; declared-but-unreachable symbols are dead.

Overrides and interface implementations are kept alive when their abstraction is used; constructors,
overrides, and interface impls are excluded from reporting to avoid noise.

## Usage

```bash
# from the repo (dev):
dotnet run --project src/Knip.Cli -- path/to/Your.sln

# or install as a global tool:
dotnet pack src/Knip.Cli -c Release
dotnet tool install -g Hdir.Knip --add-source src/Knip.Cli/bin/Release
dotnet-knip Your.sln            # also invokable as `dotnet knip`
```

Options: `-s/--solution`, `-c/--config`, `-f/--format console|json|sarif`, `-v/--verbose`,
`--no-fail`, `--production`, `--why <sym-or-id>`, `--print-config`. **Exit codes:** `0` clean · `1` unused
code found (CI gate) · `2` error.

### Agent bootstrap (`init --agent`, `--agent-instructions`)

Knip.NET is self-describing for consuming agents. To let an agent clean up dead code in a repo:

```bash
dotnet-knip init --agent        # writes .knip/AGENTS.md + knip.json into the current directory
dotnet-knip --agent-instructions # prints the same agent-consumer protocol to stdout (no solution needed)
```

`init --agent` bootstraps the current directory only (it never infers a parent repo): it writes the
canonical protocol to `.knip/AGENTS.md` and creates `knip.json` when missing. It never overwrites an
existing `knip.json`, and never overwrites a hand-edited `.knip/AGENTS.md` without `--force`. No
analysis runs. Both surfaces emit the **same** protocol from one embedded source of truth.

The short autonomy contract that protocol encodes: run with `--format json`, assert
`formatVersion == 2`, stop if `reliability.degraded == true`, then triage by `confidence` —
**`high` may be deleted** (only through the delete → build → test → re-run verify loop),
**`medium` is proposed** for human review, **`low` is surfaced** and never touched. Delete by `span`,
outermost-first (`rootCause == null`). See [`AGENTS.md`](./AGENTS.md) for the full recipe.

Findings also carry advisory **`hazards`** — shapes that survive build + tests but can break at runtime.
Alongside `publicApi`, `internalsVisibleTo`, and `buildOnlyPackage`, a dead data member read only by a
JSON serializer (a DTO tagged `[JsonProperty]`/`[DataMember]`/`[Serializable]`, or one passed to
`JsonConvert.DeserializeObject<T>` / `JsonSerializer.Deserialize<T>`) is tagged `serializationShaped`, and
a public property of a config-bound type (`IConfiguration.Get<T>()` / `.Bind(...)` / `Configure<T>`) is
tagged `configBoundType`. Both demote the finding to **low** — deleting them compiles and passes tests, so
verify by hand. Detection is name-based and needs no NuGet reference; it fires regardless of the opt-in
`serialization` plugin (hazards are advisory metadata, not reachability).

### Explain a finding (`--why`) and inspect config (`--print-config`)

- `dotnet-knip --why <symbol-or-id>` traces one symbol and exits `0` (a query, never a gate). Pass a
  finding `id` (`k1_…`) or a display name (`MyApp.Foo.Bar()`, or an unambiguous suffix). A **flagged**
  symbol prints its dead referrers (or "no incoming references") plus its root cause; an **alive** symbol
  prints the shortest root→symbol path with `file:line` hops. Output is prose — never a raw graph key.
- `dotnet-knip --print-config` prints the **effective** merged config (your `knip.json` over the built-in
  defaults) as JSON to stdout and exits `0` without running analysis.
- Any **unknown key** in `knip.json` — top-level or nested — produces a warning naming the key path
  (e.g. `unknown key 'roots.treatAllPubic'`), then analysis proceeds (exit code unchanged).

### Production mode (`--production`) — find tested-but-dead code

By default every `[Fact]`/`[Theory]` is a root, so production code called **only by its own tests**
is reachable and never flagged (a deliberate false negative). Pass `--production` (or set
`"production": true` in `knip.json`) to run **two-color reachability**: code reachable only via test
roots is reported as `onlyUsedByTests` — a distinct kind whose remediation is *delete the code **and**
its tests* (`deleteCodeAndTests`). Findings directly referenced by tests list those symbols in
`details.testReferrers`; transitive findings point through `rootCause` to that direct boundary. The
boundary's confidence governs the complete deletion unit — a dead feature plus its test suite.

Projects are classified test vs production by, first match wins: (1) `testProjects` globs in
`knip.json`; (2) a referenced test-framework assembly (`MSTest.TestFramework`/`xunit.core`/
`nunit.framework`); (3) name globs (`*Tests`/`*.Test`/`*.Tests`). `-v` prints each project's
classification and the signal that decided it; if production mode detects **zero** test projects it
warns loudly (stderr + `reliability.productionModeWarnings`) but never fails.

`onlyUsedByTests` findings land at `medium` confidence — propose in a PR for human review, don't
auto-delete. (Blunt workaround if you can't use `--production`: `ignore.projects: ["*Tests*"]` drops
the test projects entirely — but then test code isn't analyzed and `InternalsVisibleTo` edges are lost.)

### CI

```yaml
- run: dotnet restore Your.sln        # REQUIRED — see below
- run: dotnet-knip Your.sln --format sarif > knip.sarif
```

`--format json`/`sarif` give machine-readable output; SARIF surfaces as annotations in
GitHub/Azure DevOps. A non-empty result exits `1` to fail the build (use `--no-fail` to report-only).

## Configuration (`knip.json`)

Discovered automatically (nearest `knip.json` up the tree from the **analyzed solution's**
directory — or from the current directory when no solution path is given) or passed with `--config`.
See [`knip.json`](./knip.json) for a fully-annotated example (it references
[`schemas/knip.config.schema.json`](./schemas/knip.config.schema.json) via `$schema`, so editors give
completion + validation). The JSON output (`--format json`, `formatVersion: 2`) is described by
[`schemas/knip.output.schema.json`](./schemas/knip.output.schema.json). Key knobs:

- `entryPoints` — attributes / base types / name patterns / explicit custom symbol names that seed
  reachability. The compiler-selected `Main` and public `Configure`, `ConfigureServices`, and
  `ConfigureContainer` methods on `Startup`/`Startup{Environment}` hosts are semantic built-in roots;
  same-name methods on ordinary types remain reportable.
- `roots.treatAllPublicAsUsed` — for **library solutions** consumed by other repos, treats the
  public surface as used so you only see internally-dead code. `roots.publicApiProjects` scopes this
  to specific projects.
- `ignore.files` / `ignore.symbols` / `ignore.namespaces` / `ignore.projects` — globs
  (`**`, `*`, `?`) for generated code, reflection/serialization targets, etc. `ignore.symbols`
  matches a symbol by its **fully-qualified name** — the same shape shown in findings (namespace +
  containing type + member, with parameters for methods, e.g. `MyApp.Foo.Bar()`); a bare member name
  does not match a member, so qualify the glob (`*.Dtos.*`, `MyApp.Foo.Handle*`).
- `plugins` — built-in, config-gated analysis plugins that keep alive usages the core walker cannot
  see. Keyed by camelCase plugin id; `plugins.<id>.enabled` turns one on/off, with optional
  per-plugin settings under the same object. Plugins are **add-only** — a plugin can prevent a false
  positive (keep code alive) but can never mark live code dead. **`reflection` ships ON**
  (`Type.GetType("Ns.Foo")`, `Activator.CreateInstance`, `typeof(T).GetMethod("X")`/
  `x.GetType().GetMethod("X")` and friends → keep the named type/member alive). **`scanningDi` ships
  ON** — roots constructor dependency closures for Microsoft DI `Add`/`TryAdd` Singleton, Scoped, and
  Transient registrations when the selected overload proves container activation. Factory/instance
  overloads instead attach `diPluginShaped` to unresolved constructor dependencies. It also keeps alive
  assembly-scanned MediatR handlers (`IRequestHandler`/`INotificationHandler`), MassTransit consumers
  (`IConsumer<T>`), and AutoMapper `Profile` subclasses. Unrelated types and members remain reportable.
  **`blazorParameter` ships OFF**
  (opt-in via `plugins.blazorParameter.enabled: true`) — keeps alive Blazor component members set from
  `.razor` markup or the DI container: properties carrying `[Parameter]`, `[CascadingParameter]`,
  `[SupplyParameterFromQuery]`, `[EditorRequired]`, or `[Inject]`, matched by attribute NAME (offline).
  It roots only the attribute-bearing member and its accessors — never blanket-roots a component's
  members, so plain sibling properties stay flagged. **`serialization` ships OFF** (opt-in via
  `plugins.serialization.enabled: true`) — keeps alive DTO data members touched only by a JSON
  serializer: the public get/set properties and public fields of a demonstrably-serialized type
  (`JsonSerializer.Serialize<T>`/`Deserialize<T>`, `JsonConvert.SerializeObject`/`DeserializeObject<T>`,
  matched by method NAME offline; also members carrying `[JsonPropertyName]`/`[JsonProperty]`/
  `[DataMember]`). It roots only a serialized type's own data members — never blanket-roots every
  property — so non-serialized types' plain members and unrelated dead types stay flagged. Optional
  `plugins.serialization.namespaces` glob list also roots the data members of types in matching
  namespaces. **`aspnetcore` ships ON by default** (disable via `plugins.aspnetcore.enabled: false`) —
  keeps alive
  ASP.NET Core convention-invoked members the framework dispatches by reflection: `app.UseMiddleware<T>()`
  keeps the type alive but its `Invoke`/`InvokeAsync(HttpContext)` is called reflectively, so the entry
  method + constructor + fields (`_next`/`_logger`) + private helpers would otherwise cascade to false
  positives. The plugin roots the convention entry members — a middleware's `Invoke`/`InvokeAsync` +
  constructors (via `UseMiddleware<T>()`/`UseMiddleware(typeof(T))` and `IMiddleware`), an MVC/Razor
  filter's implementations of `IActionFilter`/`IAsyncActionFilter`/`IResultFilter`/`IAsyncResultFilter`/
  `IExceptionFilter`/`IAsyncExceptionFilter`/`IAuthorizationFilter`/`IAsyncAuthorizationFilter`/
  `IPageFilter`/`IAsyncPageFilter`, and an `IStartupFilter`'s `Configure`; an authorization handler's
  `HandleRequirementAsync`/`HandleAsync` + constructors (derive from `AuthorizationHandler<T>` or implement
  `IAuthorizationHandler`); and a Blazor component's lifecycle methods (derive from `ComponentBase`:
  `OnInitialized`/`OnInitializedAsync`, `OnParametersSet`/`OnParametersSetAsync`, `OnAfterRender`/
  `OnAfterRenderAsync`, `SetParametersAsync`, `BuildRenderTree`, `Dispose`/`DisposeAsync` — `[Parameter]`
  props are the separate `blazorParameter` plugin); an Application Insights telemetry processor's `Process`
  (`ITelemetryProcessor`) / initializer's `Initialize` (`ITelemetryInitializer`) + constructors; a health
  check's `CheckHealthAsync` (`IHealthCheck`) + constructors; and an authorization policy provider's
  `GetPolicyAsync`/`GetDefaultPolicyAsync`/`GetFallbackPolicyAsync` + constructors (implement
  `IAuthorizationPolicyProvider` or derive from `DefaultAuthorizationPolicyProvider`) — matched by framework-type NAME
  (offline, no NuGet needed), so their fields and helpers gain liveness via normal edges. Every recognized
  framework-activated type's explicit instance constructors are rooted as well. The plugin roots only the
  convention entry members — never blanket-roots a middleware/filter's world — so an unrelated dead method
  the entry point never calls stays flagged. Unknown plugin ids
  and unknown per-plugin keys print a **visible warning** rather than silently no-opping, so a typo
  is caught. Run with `-v` to see each plugin's contribution counts and per-project wall-time.

## Important: restore the solution first

Like Knip needs `node_modules`, Knip.NET needs a **restored, resolvable** solution. If packages are
missing (e.g. an unauthenticated private feed), their types become *error types*, overload
resolution degrades, and you may get false positives. Knip.NET detects this and prints a warning
(`N reference(s) to unresolved types …`). Always `dotnet restore` (with feeds authenticated) first.

## Target frameworks & legacy (`packages.config`) support

Both projects multi-target **`net10.0;net472`**:

- **`net10.0`** (Roslyn/`Microsoft.CodeAnalysis` 5.6.0) is the shipped global tool and the default on
  any OS. Use it for SDK-style solutions.
- **`net472`** (Roslyn 4.14.0 — the last major with `netstandard2.0` support) exists so the engine can
  run on **full-framework MSBuild** (Visual Studio Build Tools). That is the only MSBuild that can
  evaluate **legacy, non-SDK `.csproj` + `packages.config`** solutions (e.g. `net48` code). Because it
  needs full-framework MSBuild, the net472 path is **Windows-only** — it compiles everywhere but can
  only *run* on Windows with VS Build Tools installed. A hand-authored legacy fixture lives at
  `tests/fixtures/WS4Legacy/` for a future Windows end-to-end job.

The engine (`Knip.Core` — walker/analyzer/graph) is version- and OS-agnostic and compiles unchanged
against both Roslyn 4.x and 5.x; only build/loading glue differs per framework.

## Known limitations (prototype)

- **Invisible usage** beyond the built-in heuristics — unrecognized reflection, open-ended assembly scanning,
  and data-bound Razor/Blazor/XAML members — needs `ignore`/`entryPoints` config. Framework-aware plugins
  deliberately prefer narrow false negatives over blanket rooting.
- Enum members and constructors are not reported.
- Whole-solution `MSBuildWorkspace` load is the main cost; fine for the solutions tested (~2s for 10
  projects), but very large solutions will want a persisted index.

## Roadmap

1. ✅ **Dead code** (solution-wide unused symbols) — done.
2. ✅ **Unused `<ProjectReference>`s** — a reference whose declaring project uses no symbol from the
   referenced project's assembly (`UnusedProjectReference` finding). Conservative: references with any
   cross-project symbol edge (including `[InternalsVisibleTo]` usage) are kept; runtime-only/transitive
   dependencies with no symbol edge may still be flagged, so triage before removing.
3. ✅ **Unused `<PackageReference>`s** — a package none of whose delivered assemblies is touched by any
   symbol in the referencing project (`UnusedPackageReference` finding, remediation `removePackageReference`).
   The assembly→package map comes from `obj/project.assets.json` (falling back to resolved metadata-reference
   paths), so the project must be restored. Per the recall-over-silence policy, analyzer / source-generator /
   build-only (`PrivateAssets="all"`) packages — whose effect is invisible to symbol edges — are **emitted**
   with a `buildOnlyPackage` hazard at **low** confidence, never dropped; a normal unused package-ref lands at
   **medium** (transitive-only / implicit-`Using` usage can still make a genuinely-needed package look unused,
   so triage through the verify loop before removing).
4. ✅ **Framework plugins** — a config-gated plugin seam with built-in plugins for reflection,
   assembly-scanning DI (MediatR/MassTransit/AutoMapper), ASP.NET Core convention-invoked members,
   serialization, and Blazor parameters (see Configuration above).
5. Remaining: incremental/cached index for very large solutions, `--baseline` for gating only new
   findings, and further framework plugins (e.g. EF Core, source generators).

## Project layout

```
src/Knip.Core/   Roslyn engine: Configuration, Analysis (graph + walker), Model, Reporting
src/Knip.Cli/    dotnet global tool: arg parsing, MSBuild registration, exit codes
```
