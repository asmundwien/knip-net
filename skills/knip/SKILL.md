---
name: knip
description: Analyze and safely remove unused C# code and project or package references with Knip.NET. Use for dead-code discovery or cleanup in .NET solutions, Knip.NET JSON reports, knip.json, or installing and operating dotnet-knip.
---

# Knip.NET

Use Knip.NET as a read-only analyzer unless the user explicitly asks to remove or delete findings. Requests to find, analyze, explain, or review authorize reporting only.

## Workflow

1. Establish the user's requested scope: report-only or deletion-authorized. Preserve it throughout the run.
2. Check `dotnet tool list --global` and `dotnet-knip --version`. This skill accepts only the global package `AsmundWien.Knip` and command version `0.1.0-preview.1`. If either is absent or incompatible, read [installation.md](references/installation.md) and obtain the required approval before changing tools. Report a repository tool manifest, but use or modify it only when the user requests repository-local tooling.
3. Use the target named by the user. Otherwise omit the target and let the CLI perform strict discovery. If discovery is ambiguous, show the candidates and ask the user to choose.
4. Discover repository-owned restore instructions and run the documented restore command before analysis when one exists. Do not invent a substitute for an unavailable required restore.
5. If a `knip.json` applies, run `dotnet-knip config doctor [target]` with the same explicit `--config` when one was requested. With no applicable config, use built-in defaults. Do not edit `knip.json` unless asked.
6. Run ordinary analysis unless the user explicitly requests production analysis or removal of code used only by tests. Capture `dotnet-knip analyze [target] --format json --no-fail` outside the repository, adding the selected `--config` and `--production` options unchanged.
7. Read [json-v2.md](references/json-v2.md), then validate the complete report before interpreting any finding. Stop deletion at the first failed gate and report that gate.
8. Establish library posture from `knip.json` or the human before acting on public or protected findings. While posture is unknown, continue processing eligible private and internal findings.
9. For a report-only request, present findings grouped by confidence with IDs, locations, hazards, remediation, and root-cause relationships. Then stop.
10. For a deletion-authorized request, triage in deterministic report order:
    - `high`: eligible without further approval only when `rootCause` is absent and `span` exists.
    - `medium`: when `rootCause` is absent and `span` exists, run `dotnet-knip explain` for the exact finding ID, then request approval naming that ID. One response may approve several named IDs. Otherwise report it without requesting deletion approval.
    - `low`: report-only.
    - A finding with `rootCause` is covered by its ancestor and is never a separate deletion unit. A finding without `span` is not machine-editable.
11. Before the first deletion, read [verification.md](references/verification.md). Process one approved deletion unit through its complete verification cycle before touching the next. Apply the same rules to code, project references, and package references.

## Completion

A report-only run ends after the evidence-bearing triage. A deletion run ends when no eligible approved unit remains or a stop condition fires.

Report exact deletions, verification commands and results, rollbacks, failures, and remaining medium or low findings. Claim only checks that ran.