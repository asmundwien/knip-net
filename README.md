# Knip.NET

[Knip](https://knip.dev) for .NET — a **free, solution-wide dead-code finder** built on Roslyn.
The paid tools (ReSharper/Rider, NDepend) own this space; the free Roslyn analyzers only catch
*private* unused members inside a single file. Knip.NET closes the gap: it reports code that is
**unreferenced across the entire solution**, including public/internal types and members.

Runs locally and in CI, and is **configured entirely in code** (`knip.json`).

> Status: **working prototype.** Flagship feature (dead code) is implemented and validated on real
> solutions. Unused `<ProjectReference>` detection is implemented; unused NuGet packages are on the
> roadmap below.

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
`--no-fail`. **Exit codes:** `0` clean · `1` unused code found (CI gate) · `2` error.

### CI

```yaml
- run: dotnet restore Your.sln        # REQUIRED — see below
- run: dotnet-knip Your.sln --format sarif > knip.sarif
```

`--format json`/`sarif` give machine-readable output; SARIF surfaces as annotations in
GitHub/Azure DevOps. A non-empty result exits `1` to fail the build (use `--no-fail` to report-only).

## Configuration (`knip.json`)

Discovered automatically (nearest `knip.json` up the tree) or passed with `--config`.
See [`knip.json`](./knip.json) for a fully-annotated example (it references
[`schemas/knip.config.schema.json`](./schemas/knip.config.schema.json) via `$schema`, so editors give
completion + validation). The JSON output (`--format json`, `formatVersion: 2`) is described by
[`schemas/knip.output.schema.json`](./schemas/knip.output.schema.json). Key knobs:

- `entryPoints` — attributes / base types / name patterns / symbol names that seed reachability.
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
  `x.GetType().GetMethod("X")` and friends → keep the named type/member alive). Unknown plugin ids
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

- **Invisible usage** beyond the built-in heuristics — reflection (`Activator.CreateInstance`,
  `Type.GetType`), non-generic DI (`AddScoped(typeof(Foo))` / assembly scanning), and data-bound
  Razor/Blazor/XAML members — needs `ignore`/`entryPoints` config. This is the moat the paid tools
  charge for; framework-aware "plugins" are the path to closing it.
- Enum members and constructors are not reported.
- Whole-solution `MSBuildWorkspace` load is the main cost; fine for the solutions tested (~2s for 10
  projects), but very large solutions will want a persisted index.

## Roadmap

1. ✅ **Dead code** (solution-wide unused symbols) — done.
2. ✅ **Unused `<ProjectReference>`s** — a reference whose declaring project uses no symbol from the
   referenced project's assembly (`UnusedProjectReference` finding). Conservative: references with any
   cross-project symbol edge (including `[InternalsVisibleTo]` usage) are kept; runtime-only/transitive
   dependencies with no symbol edge may still be flagged, so triage before removing.
3. **Unused `<PackageReference>`s** — no symbol from a package's namespaces referenced.
4. Framework plugins (ASP.NET Core minimal APIs, EF Core, MassTransit, source generators),
   incremental/cached index, `--baseline` for gating only new findings.

## Project layout

```
src/Knip.Core/   Roslyn engine: Configuration, Analysis (graph + walker), Model, Reporting
src/Knip.Cli/    dotnet global tool: arg parsing, MSBuild registration, exit codes
```
