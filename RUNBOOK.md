# Knip.NET maintainer runbook

This guide owns the maintenance invariants, verification gate, and escalation rules. It describes the current product, not its design history.

Read [README.md](README.md) for user behavior, [docs/architecture.md](docs/architecture.md) for module ownership and domain terms, and `src/Knip.Cli/Resources/AgentInstructions.md` for the agent consumer protocol.

## 1. Scope

Knip.NET finds code that can be removed before a migration. It must analyze SDK-style projects on current .NET and legacy `.csproj` projects with `packages.config` through the Windows `net472` build.

Keep a fact here only when it controls a change or review. User options belong in the README, schemas, and annotated `knip.json`.

## 2. Architecture

[docs/architecture.md](docs/architecture.md) defines the analysis flow, module boundaries, and domain terms. Update it when responsibility moves between modules or a core term changes meaning.

Do not copy its file map here. This runbook records the constraints on that design.

## 3. Invariants

Each invariant records a bug already fixed or a signed-off decision. Reject a change that breaks one, even when its tests pass.

1. **Graph identity.** Use assembly-qualified documentation comment IDs, never symbol references. `SymbolId.For` returns `DefiningAssembly::docId`. The assembly must come from `symbol.OriginalDefinition.ContainingAssembly`. A bare ID merges equal signatures from different projects. Roslyn symbol identity also differs when one compilation sees source and another sees metadata.

2. **MSBuild registration.** `MSBuildLocator.RegisterDefaults()` must run before code touches a Roslyn MSBuild type. Keep `Program.cs` small and workspace code behind `Runner.RunAsync`. Do not add Roslyn MSBuild imports to `Program.cs`.

3. **Failed overload resolution.** `ReferenceWalker.RecordReference` keeps every candidate symbol alive. Picking one candidate creates false positives.

4. **Entry points.** An entry-point member roots its containing type chain. An instance entry point also roots the constructors and initializers needed to create the type.

5. **Solution graph boundary.** Graph edges target solution assemblies only. Record external assembly names separately before dropping those edges; package reference analysis needs that data.

6. **Unresolved types.** Missing packages create error types and weaken overload resolution. Keep the unresolved-type count and warning path. A degraded run is not safe for deletion.

7. **Reporting noise.** Report the outermost dead symbol. Do not report constructors, static constructors, finalizers, overrides, or interface implementations. Keep an override or implementation alive when its abstraction is used.

8. **Recall over silence, hazards are sacred.** Do not hide a finding class to avoid false positives. Emit the finding with an honest confidence and hazards. Reflection, dependency injection by name, serialization, configuration binding, and markup can survive build and tests but fail at runtime. Those shapes must lower confidence. A change that could miss one needs a fixture that proves the safe case and its dead sibling. Do not relax invariants 3, 5, or 7 as a group.

9. **Engine boundary.** `Knip.Core` has no CLI or `MSBuildLocator` dependency. Keep the analysis code independent of operating system and Roslyn version so the same source compiles for `net10.0` and `net472`.

## 4. Environment

- Building requires the .NET 10 SDK.
- Private Hdir feeds return 401 without authentication. Use self-contained fixtures unless feed restore succeeds.
- Never commit Hdir source or analysis output from an Hdir solution.
- Restore a real solution before analysis. Missing packages make the result unreliable.
- Running the `net472` legacy path requires Windows and Visual Studio Build Tools. It can compile elsewhere, but its end-to-end check cannot run there.
- Portfolio scale is untested. On the first large-solution run, record wall time, peak memory, and workspace diagnostics. Do not shrink the solution to make the run pass.

## 5. Verification gate

Run after every change:

```bash
dotnet build Knip.slnx -warnaserror
dotnet test tests/Knip.Core.Tests/Knip.Core.Tests.csproj --no-build
dotnet run --project src/Knip.Cli -- tests/fixtures/<relevant>/Fixture.slnx
```

The test battery is the feature contract. Each fixture asserts the exact finding set. Every alive assertion needs a dead sibling or another check that proves the fixture can fail. A skipped test records a deferred feature. Promote it when the feature lands; do not delete it.

Check the changed behavior through the CLI, not only through tests. Also check that the change did not add build output, delete an invariant, or add conditional compilation outside the CLI and compatibility layer.

Behavior, config, or output changes update their schemas and user documentation in the same change.

Review rules:

1. Reject an invariant violation by number.
2. A heuristic that could miss runtime-only use needs a fixture for the safe case and a dead sibling. A new confidence or hazard rule needs a test for its tier.
3. Escalate any change to an existing test assertion or fixture. Removing a skip after the feature lands is the exception.
4. Keep analysis, loading, and CLI work in their owning modules.

Before recommending deletion in a real repository:

1. Restore must succeed without unresolved-type warnings.
2. Apply the deletion on a branch of the target repository. Its build and full test suite must pass.
3. Review runtime-shaped findings by hand. Build and tests cannot prove reflection, serialization, configuration, or dependency injection use.

## 6. Escalation

Ask the owner before:

- changing an invariant;
- changing the public config schema or plugin contract;
- changing an existing test assertion or fixture;
- adding a confidence or hazard demotion rule;
- publishing a package;
- relying on a Windows legacy run or a private-feed run that this environment cannot perform.

Stop after two failed attempts at the verification gate. Report both failures and the remaining blocker.

## 7. Git

- Keep `main` green. Do not rewrite it.
- Use one branch and one coherent commit per change.
- Keep behavior, tests, and affected documentation together.
- Do not commit `bin`, `obj`, IDE state, credentials, private feed configuration, Hdir source, or Hdir analysis output.

## 8. Current work

The tests and public schemas define shipped behavior. Do not maintain feature counts here.

- [RB-01](runbooks/RB-01-field-fixes-2026-07.md) has shipped Tasks A and B. Task C, the `unusedProject` finding for dead test projects, remains open.
- [RB-02](runbooks/RB-02-unused-config-detection.md) specifies unused `appsettings*.json` key detection. No implementation has started.
- The skipped H7 through H10 tests record unsupported XAML binding, WebForms code-behind, configuration string references, and dynamic dispatch.
- Large solutions still need an incremental index and baseline support.
- The legacy fixture still needs an end-to-end run on Windows with Visual Studio Build Tools.

Publishing the global tool remains owner-approved work.
