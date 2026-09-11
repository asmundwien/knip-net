# JSON v2 gates

Apply these gates in order to every initial and post-deletion report. Stop deletion at the first failure and name it.

1. The analysis process exits `0` under `--no-fail`.
2. Stdout contains exactly one valid JSON object and no diagnostics. Diagnostics belong on stderr.
3. `formatVersion` is the integer `2`.
4. `tool.name` is `Knip.NET`.
5. `tool.version` is exactly `0.1.0-preview.1`.
6. The report has exactly the documented JSON v2 shape:
   - Top level requires `formatVersion`, `tool`, `run`, `reliability`, `summary`, and `findings`.
   - Every finding requires `id`, `kind`, `symbol`, `symbolKind`, `accessibility`, `project`, `confidence`, `hazards`, `remediation`, `location`, and `details`.
   - Recognized kinds are `unusedType`, `unusedMethod`, `unusedProperty`, `unusedField`, `unusedEvent`, `unusedEnumMember`, `unusedProjectReference`, `unusedPackageReference`, and `onlyUsedByTests`.
   - Recognized confidence values are `high`, `medium`, and `low`.
   - Recognized hazards are `publicApi`, `serializationShaped`, `configBoundType`, `diPluginShaped`, `internalsVisibleTo`, and `buildOnlyPackage`.
   - Recognized remediation values are `deleteSymbol`, `removeProjectReference`, `removeFromInterface`, `removePackageReference`, and `deleteCodeAndTests`.
   - `span`, `referencedProject`, and `rootCause` are optional. Omission means absence; explicit `null` is invalid.
7. Finding IDs match `^k1_[0-9a-f]{10}$` and are unique.
8. Every `rootCause` names another finding in the same report.
9. Every actionable `span` names an existing absolute file path and has valid 1-based, half-open start and end positions. The end must follow the start and both positions must fit the file.
10. `reliability.degraded` is `false`.
11. Before acting on a public or protected finding, library posture is explicit in `knip.json` or supplied by the human.

Validate counts and nested field types rather than trusting their presence. Treat unknown fields or enum values as an incompatible report. Do not infer safety from an empty hazards array.