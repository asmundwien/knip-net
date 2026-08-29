# Knip.NET agent consumer protocol

You are cleaning up dead code with Knip.NET. Its JSON output is the product API. Do not delete anything until every gate below passes.

## 1. Run

```bash
dotnet-knip --format json --no-fail
```

`--no-fail` returns the report without using findings as an exit-code gate. Assert `formatVersion == 2` before parsing anything else. A different version is a different contract, so stop.

## 2. Check reliability

If `reliability.degraded == true`, do not delete anything. Restore or workspace loading was incomplete, so the reachability graph is not fit for deletion decisions. Report the `reliability` details to a human and stop.

## 3. Triage by confidence

| `confidence` | Action | Precondition |
|---|---|---|
| `high` | May delete | Complete the verification loop in section 6. |
| `medium` | Propose for human review | None. |
| `low` | Report only | Never delete. |

`hazards[]` records known false-positive shapes. It is advisory. A missing hazard does not make a finding safe, and only `confidence` controls the action.

`onlyUsedByTests` findings cover production code and its tests. They require human review even when their confidence is `medium`.

## 4. Delete by span

`span` is the complete single-file deletion range. It includes owned XML documentation and attributes through the declaration's last token. Positions are 1-based and the range is half-open: `[start, end)`.

`location` is only the identifier position for navigation. Never derive a deletion range from it.

- A missing `span` means Knip.NET cannot describe one independent deletion. Report the finding without deleting it.
- Delete only eligible findings with `rootCause == null` in the current pass. A non-null `rootCause` points to a reported parent whose deletion already covers the finding.
- For `onlyUsedByTests`, follow `rootCause` to the direct test boundary with `details.testReferrers`. The boundary's confidence governs the whole code-and-tests deletion.

## 5. Set library posture before triage

Public and protected symbols are findings by default because the solution contains no external callers. If other repositories consume this solution as a library, set `roots.treatAllPublicAsUsed` or `roots.publicApiProjects` in `knip.json`. Matching public symbols then become roots and leave the finding set.

## 6. Verify every high-confidence deletion

1. Delete one eligible `high` finding by `span`.
2. Run `dotnet build`.
3. Run the full test suite.
4. Run Knip.NET again.

If the build or tests fail, revert the deletion and propose it for human review.

The second Knip run need not match the first. Deleting dead code can expose another dead layer. The failure condition is a symbol that was alive before the deletion and becomes a finding afterward. Revert that deletion.

Repeat until a run has no new eligible high-confidence findings.

## 7. Exit codes

| Code | Meaning |
|---|---|
| `0` | Clean, or findings with `--no-fail` |
| `1` | Findings present |
| `2` | Usage, config, or load error |
| `130` | Cancelled |

`--no-fail` changes exit `1` to `0`. It does not mask errors or cancellation.
