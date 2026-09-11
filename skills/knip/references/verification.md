# Verified deletion

## Establish the baseline

Before the first deletion:

1. Preserve the validated JSON report and repository state outside the repository.
2. Record pre-existing staged, unstaged, and untracked changes without modifying them. A clean working tree is not required.
3. Compare the candidate span with those changes. Refuse automatic deletion when they overlap or when a narrow reversal cannot preserve the pre-existing state.
4. Preserve enough pre-edit material outside the repository to reverse only the candidate edit.

Discover authoritative restore, build, and full-test commands in this order:

1. Applicable agent instruction files and documents they require.
2. Maintainer or contributor documentation.
3. CI workflows.
4. Unambiguous native build metadata when no written command exists.

More specific instructions override general ones. Ask before deletion when sources conflict, several full suites are plausible, a required check cannot run on the current platform, or no authoritative verification commands exist.

## Process one unit

A top-level finding and everything covered by its span are one deletion unit. Process units in deterministic report order.

For each eligible or explicitly approved unit:

1. Apply only the edit described by that unit's span.
2. Run the repository's build and full test suite.
3. Run Knip.NET again with the same target, config, and analysis mode, writing the report outside the repository.
4. Apply every JSON v2 gate to the new report.
5. Compare finding IDs with the baseline for this cycle.

The cycle succeeds only when the build and tests pass, the rerun is reliable, and no finding ID appears that was absent from the baseline. After success, use the new report and repository state as the next cycle's baseline.

On a failed build, failed test, degraded or invalid rerun, or newly appearing finding ID:

1. Restore only the current deletion from the preserved pre-edit material.
2. Confirm that the repository matches its state immediately before that unit while retaining every pre-existing and earlier verified change.
3. Stop the deletion run and report the failed command or gate, the rollback, and the remaining findings.

Never reset, discard, or overwrite unrelated work. Remove reports and rollback material from outside the repository when the run ends.