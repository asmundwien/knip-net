# Knip.NET — agent consumer protocol

You are an agent cleaning up dead code with Knip.NET. Its JSON output is the product API. Follow
this protocol exactly. Do not delete anything until every gate below passes.

## 1. Run

```bash
dotnet-knip --format json --no-fail
```

`--no-fail` gives you the JSON for triage without the exit-code gate. Assert `formatVersion == 2`
before parsing anything else — if it is not `2`, stop; you were built against a different contract.

## 2. Trust the run before trusting a finding

If `reliability.degraded == true`, do NOT delete anything. Restore/load was incomplete, so the
reachability graph under every finding is untrustworthy. Report the `reliability` details to a human
and stop.

## 3. Triage by `confidence`

| `confidence` | Action | Precondition |
|---|---|---|
| `high`   | may DELETE | only via the full verify loop (§6), and only when `reliability.degraded == false` |
| `medium` | PROPOSE for human review | — |
| `low`    | SURFACE only — never touch | — |

`hazards[]` is advisory: it means "verify harder", never "auto-safe". Its absence never upgrades
autonomy. `confidence` is the only field that gates action.

`--production` / `onlyUsedByTests` findings are human-review only (`medium`) unless another rule
demotes them; never delete them autonomously. Their deletion unit is the production code AND its tests.

## 4. Delete by `span`, never by `location`

`span` is the complete, safe single-file deletion unit (leading XML-doc comments and attribute lists
through the closing `}` / terminating `;`). `location` is only the jump-to line for humans — never
reconstruct a range from it.

- An omitted `span` means no complete independently removable declaration can be represented (for example,
  multiple symbol declarations or sibling field/event declarators). Never auto-delete it; surface it.
- Delete only eligible `rootCause == null` findings in the current pass. A finding with a non-null
  `rootCause` is covered by deleting its parent; cascades are handled by re-running (§6).
- For `onlyUsedByTests`, follow `rootCause` to the direct test boundary carrying
  `details.testReferrers`. That boundary's confidence governs the whole unit; never bypass a `low` or
  `medium` parent to delete its child.

## 5. Library posture — set this first if the repo ships a library

Public and protected symbols are flagged by default (nothing in the solution references them). If
this repo is a library consumed by other repos, set `roots.treatAllPublicAsUsed` or
`roots.publicApiProjects` in `knip.json` before triage, or you will drown in `publicApi` findings you
must not touch.

## 6. The verify loop (mandatory before any `high` deletion)

1. Delete the `high`, `rootCause == null` finding by `span`.
2. Run `dotnet build`.
3. Run the full test suite.
4. Re-run Knip.NET.

Any build or test failure means the finding was not safe → drop it to `medium` handling (propose,
don't delete).

Re-run assertion: do NOT require identical output. Deleting dead code legitimately uncovers newly
dead symbols (cascades) — that is the tool working. The failure condition is a symbol that was ALIVE
before your deletion now being flagged. That is a regression → revert.

Repeat until a run reports no new deletable `high` findings.

## 7. Exit codes

| Code | Meaning |
|---|---|
| `0` | clean — no findings |
| `1` | findings present (the CI gate) |
| `2` | error (bad args, malformed config, load failure) |

`--no-fail` masks findings only (forces `0` when findings exist); exit `2` always wins.
