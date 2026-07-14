# WS4Legacy fixture — legacy-format net48 solution

A hand-authored **non-SDK** (legacy) project that Knip.NET's net472 build exists to analyze:

- `LegacyLib.sln` — classic `.sln` (not `.slnx`; old MSBuild can't parse `.slnx`).
- `LegacyLib.csproj` — legacy XML: `<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">`,
  explicit `<Reference>` items, explicit `<Compile Include=.../>`, `TargetFrameworkVersion` = `v4.8`,
  and a `<Reference>` to a NuGet package resolved via `packages.config` + `HintPath`.
- `packages.config` — the pre-`PackageReference` NuGet manifest (`Newtonsoft.Json`).
- `LegacyClass.cs` — one used type, one deliberately-unused (`UnusedLegacyType`) so an end-to-end
  run has something to find.

## Status / how it is (not) used

- This fixture is **NOT** added to `Knip.slnx` and is **NOT** compiled by any test project.
- Evaluating a legacy `.csproj` + `packages.config` requires **full-framework MSBuild** (Visual Studio
  Build Tools), which is **Windows-only**. `MSBuildLocator` cannot register such an instance on
  macOS/Linux, so an end-to-end Knip run over this fixture is a **Windows-only e2e** and is **NOT
  verified** by the cross-platform CI gate. It is authored here so a Windows job can wire it up later
  (restore `packages.config`, then `dotnet-knip` built for net472 against `LegacyLib.sln`).
