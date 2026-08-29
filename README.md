# Knip.NET

Knip.NET is a free, solution-wide dead-code finder built on Roslyn. It reports unreferenced types and members across project boundaries, including public and internal code. It also finds unused project and package references.

The tool is a working prototype. Its main analysis has been used on real solutions. Treat every result as a deletion candidate, not a verdict.

## How it works

Knip.NET loads a `.sln`, `.slnx`, or `.csproj` with `MSBuildWorkspace`. It builds a reachability graph from declared symbols and their uses, seeds roots for runtime entry points, then reports declarations that no root can reach.

Graph keys use `DefiningAssembly::documentationCommentId`. The assembly prefix keeps identical signatures in different projects separate and gives one identity to source and metadata views of the same symbol.

Framework plugins add roots and edges for reflection, dependency injection, ASP.NET Core conventions, serialization, and Blazor parameters. Constructors, overrides, and interface implementations can keep other code alive but are not findings themselves.

See [the architecture guide](docs/architecture.md) for module ownership and the analysis model.

## Run it

From this repository:

```bash
dotnet run --project src/Knip.Cli -- path/to/Your.sln
```

To install the local package as a global tool:

```bash
dotnet pack src/Knip.Cli -c Release
dotnet tool install -g Hdir.Knip --add-source src/Knip.Cli/bin/Release
dotnet-knip path/to/Your.sln
```

The command also works as `dotnet knip`.

Common options:

```text
-s, --solution <path>
-c, --config <path>
-f, --format console|json|sarif
-v, --verbose
    --no-fail
    --production
    --why <symbol-or-id>
    --print-config
```

Exit codes are `0` for a clean run, `1` when findings exist, `2` for usage or load errors, and `130` when the run is cancelled. `--no-fail` changes exit `1` to `0`. It never masks an error.

## Agent use

JSON v2 is the product API. Run this before letting an agent delete code:

```bash
dotnet-knip init --agent
```

The command writes `knip.json` and `.knip/AGENTS.md` in the current directory. It does not run analysis. It keeps an existing `knip.json`, and it will not replace a changed `.knip/AGENTS.md` unless you pass `--force`.

`dotnet-knip --agent-instructions` prints the same protocol without writing files. The source is [`src/Knip.Cli/Resources/AgentInstructions.md`](src/Knip.Cli/Resources/AgentInstructions.md).

The protocol requires agents to check `formatVersion`, stop on degraded reliability, triage by confidence, delete by `span`, and verify every high-confidence deletion with a build, the full test suite, and another Knip run.

## Configuration

Knip.NET looks for the nearest `knip.json` starting from the analyzed solution's directory. Pass `--config` to choose another file.

The annotated [`knip.json`](knip.json) documents the available settings and plugin defaults. [`schemas/knip.config.schema.json`](schemas/knip.config.schema.json) provides editor validation. The main decisions are:

- Set `roots.treatAllPublicAsUsed` or `roots.publicApiProjects` when other repositories consume the public API. Matching public symbols become roots and leave the finding set.
- Add project-specific entry points for code invoked by a framework or by reflection that the built-in plugins do not recognize.
- Use ignores for generated code and known runtime-only references that static analysis cannot prove.
- Enable opt-in plugins only for frameworks the solution uses.

`--print-config` prints the effective config. Unknown keys produce a warning instead of failing the run.

## Results and safe deletion

Use `--format json` for automation. The output schema is [`schemas/knip.output.schema.json`](schemas/knip.output.schema.json).

Each finding has a confidence, hazards, a remediation, and usually a deletion span. `span` uses 1-based, half-open coordinates: `[start, end)`. `location` is for navigation and is not a deletion range.

- `high` findings may be deleted only through the full verification loop.
- `medium` findings need human review.
- `low` findings are report-only.

Hazards describe runtime shapes that a build may miss, such as external public API use, serialization, configuration binding, dependency injection, or build-only packages. Confidence, not the presence or absence of one hazard, controls the action.

If `reliability.degraded` is true, restore or workspace loading was incomplete. Do not delete from that run. Restore the solution with authenticated feeds and run Knip.NET again.

`--why <symbol-or-id>` explains why a symbol is dead or shows the shortest root-to-symbol path for live code.

## Production mode

Tests are roots in the default analysis. Production code used only by its own tests therefore looks alive.

`--production` runs separate production and test reachability. Roots and edges added by analysis plugins inherit the project where the plugin discovered them; a contribution discovered from both production and test projects counts as production. Code reachable only through test roots or test-only plugin contributions is reported as `onlyUsedByTests` with remediation `deleteCodeAndTests`. Direct findings list their test referrers. Transitive findings point to that boundary through `rootCause`.

These findings need human review. The deletion unit contains both production code and tests.

## CI

Restore before analysis. Missing packages create unresolved Roslyn types and make the graph unreliable.

```yaml
- run: dotnet restore Your.sln
- run: dotnet-knip Your.sln --format sarif > knip.sarif
```

A non-empty run exits `1`. Add `--no-fail` for report-only CI.

## Runtime support

Both projects target `net10.0` and `net472`.

The `net10.0` global tool handles SDK-style projects on supported .NET hosts. The `net472` build lets the engine run under full-framework MSBuild for legacy `.csproj` and `packages.config` solutions. That path requires Windows and Visual Studio Build Tools.

## Known limits

- Static analysis cannot prove every reflection, assembly scanning, XAML, WebForms, dynamic dispatch, or configuration-file reference. Configure roots and ignores for those cases.
- Whole-solution `MSBuildWorkspace` load dominates runtime. Portfolio-scale indexing and `--baseline` are not implemented.
- Unused members of a live enum are reported. Constructors are not.
- The legacy fixture compiles cross-platform, but its end-to-end run still needs a Windows runner.

## Development

Maintainers should read [RUNBOOK.md](RUNBOOK.md) before changing behavior. It owns the invariants, verification gate, and current work. Architecture belongs in [docs/architecture.md](docs/architecture.md).
