# Architecture

Knip.NET has two projects. `Knip.Core` analyzes code. `Knip.Cli` loads MSBuild, handles commands, and writes reports. Keep that boundary sharp. The engine must compile unchanged for `net10.0` and `net472`.

## Analysis flow

`KnipEngine` opens a solution or project through `MSBuildWorkspace`. It then runs the following steps:

1. `ReferenceWalker` records declared symbols and directed use edges for each syntax tree.
2. Built-in plugins add roots or edges for usages Roslyn cannot see directly, such as reflection strings and framework conventions.
3. Configured entry points and semantic host conventions seed the root set.
4. `DeadCodeAnalyzer` traverses the graph from those roots.
5. Unreachable declarations become findings. Project and package reference checks add structural findings.
6. The analyzer assigns IDs, deletion spans, remediation, root causes, and hazards.
7. `KnipEngine` completes reliability data, then `ConfidenceModel` grades each finding.
8. Reporters write console, JSON v2, or SARIF output.

## Graph identity

A graph node is a declared symbol in a solution assembly. `SymbolId.For` keys it as:

```text
DefiningAssembly::documentationCommentId
```

The assembly prefix matters. Two projects may declare the same namespace, type, and member signature. A bare documentation comment ID would merge them. The defining assembly also gives the same key when Roslyn sees a symbol as source in one compilation and metadata in another.

Edges point only to solution symbols. The walker records external assembly use separately for package reference analysis.

## Domain terms

A root is a symbol the runtime or configuration can invoke without an ordinary source reference. Examples include `Main`, test methods, framework entry points, and configured public APIs.

A symbol is alive when a path from a root reaches it. A finding is a declared symbol or reference that the analysis could not reach.

`rootCause` links a finding to the nearest reported parent whose deletion already covers it. A null `rootCause` marks the outermost deletion candidate for the current pass. Deleting one layer may expose another, so cleanup is iterative.

`span` is the complete single-file deletion range. Its positions are 1-based and the range is half-open: `[start, end)`. `location` is only the identifier position for navigation.

`confidence` is the effective autonomy tier of the complete deletion unit. A descendant linked by `rootCause` cannot have greater confidence than any ancestor governing its deletion. Hazards remain local evidence about the finding where they were detected; their confidence demotion propagates through the unit. Hazards do not change reachability and their absence does not make a finding safe.

Reliability describes whether restore and workspace loading produced a graph fit for deletion decisions. `reliability.degraded` blocks autonomous deletion.

## Plugin boundary

Plugins implement `IKnipPlugin` and contribute through a project-scoped `ContributionSink`. They may keep code alive by adding roots or edges. The sink records whether each contribution was discovered from a test or production project; production traversal excludes test-only contributions, while default traversal includes both. A contribution discovered from both origins counts as production. Plugins may not create dead findings. This one-way rule prevents a plugin from making the core analysis less conservative.

Built-in plugins live under `src/Knip.Core/Plugins/BuiltIn`. Configuration selects them by id. Runtime hazard detection is separate from plugin reachability because a disabled plugin may still need to warn that a finding has a risky shape.

## CLI boundary

`Program.cs` registers `MSBuildLocator` before code touches Roslyn MSBuild types. `Runner` owns argument parsing, config discovery, engine invocation, reporting, and exit codes. `Knip.Core` has no CLI or locator dependency.

`src/Knip.Cli/Resources/AgentInstructions.md` is the agent consumer protocol. `AgentInstructionsProvider` embeds that file. Both `--agent-instructions` and `init --agent` use the embedded text.

## Public contracts

The JSON v2 schema in `schemas/knip.output.schema.json` is the machine contract. `schemas/knip.config.schema.json` defines `knip.json`. The annotated root `knip.json` explains settings that need examples.

A behavior change updates the owning schema and its contract tests in the same change. README prose should explain user decisions, not duplicate every schema field or implementation branch.
