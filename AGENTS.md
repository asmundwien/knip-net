# Agent instructions

Read [RUNBOOK.md](RUNBOOK.md) before changing source, tests, schemas, or CLI behavior. It owns the maintenance invariants, verification gate, and escalation rules.

Read [docs/architecture.md](docs/architecture.md) before changing module boundaries or the analysis flow.

The installable `knip` Agent Skill is the sole agent workflow. Update `skills/knip/SKILL.md` and its disclosed references when that workflow or its autonomy rules change; do not duplicate it in repository instructions or the CLI.

Keep this file limited to repository-specific instructions that project files cannot enforce. Do not repeat build commands, compiler settings, or formatting rules here.

## Git hygiene

- Check `git status --short --branch` before editing and before handoff. Treat every pre-existing change as user work; preserve it unless the task explicitly owns it.
- Keep commits coherent and reviewable. Stage exact paths or hunks, separate unrelated behavior, and use imperative commit subjects that describe what the commit does.
- Run the repository checks covering the changed behavior before committing. Record failures honestly; never hide them by weakening checks or excluding affected files.
- Keep the working tree clean at handoff. Commit the source and documentation changed for the task. Ignore only generated, secret, machine-local, or disposable artifacts. Use narrow ignore rules; never ignore source merely to make status clean.
- Fetch before publishing, push the current branch to its configured upstream, then verify the branch is neither ahead nor behind and `git status --short` is empty.
- Preserve shared history. Do not amend, rebase, force-push, or discard unrecognized work unless the user explicitly requests it.
