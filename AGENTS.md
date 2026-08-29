# Agent instructions

Read [RUNBOOK.md](RUNBOOK.md) before changing source, tests, schemas, or CLI behavior. It owns the maintenance invariants, verification gate, and escalation rules.

Read [docs/architecture.md](docs/architecture.md) before changing module boundaries or the analysis flow.

The JSON v2 output and `knip.json` are public contracts. Change their schemas with the behavior they describe. Update the embedded consumer protocol only when an agent's workflow or autonomy rules change.

The consumer protocol has one source: `src/Knip.Cli/Resources/AgentInstructions.md`. The CLI prints it through `--agent-instructions` and writes it through `init --agent`. Do not copy it into this file.

Keep this file limited to repository-specific instructions that project files cannot enforce. Do not repeat build commands, compiler settings, or formatting rules here.

## Git hygiene

- Check `git status --short --branch` before editing and before handoff. Treat every pre-existing change as user work; preserve it unless the task explicitly owns it.
- Keep commits coherent and reviewable. Stage exact paths or hunks, separate unrelated behavior, and use imperative commit subjects that describe what the commit does.
- Run the repository checks covering the changed behavior before committing. Record failures honestly; never hide them by weakening checks or excluding affected files.
- Keep the working tree clean at handoff. Commit the source and documentation changed for the task. Ignore only generated, secret, machine-local, or disposable artifacts. Use narrow ignore rules; never ignore source merely to make status clean.
- Fetch before publishing, push the current branch to its configured upstream, then verify the branch is neither ahead nor behind and `git status --short` is empty.
- Preserve shared history. Do not amend, rebase, force-push, or discard unrecognized work unless the user explicitly requests it.
