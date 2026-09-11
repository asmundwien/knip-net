# Installation and compatibility

The skill and CLI are release-paired. Version `v0.1.0-preview.1` of this skill accepts only global package `AsmundWien.Knip` version `0.1.0-preview.1` and `dotnet-knip --version` output `Knip.NET 0.1.0-preview.1`.

## Install the skill

Global installation:

```bash
npx skills add https://github.com/asmundwien/knip-net/tree/v0.1.0-preview.1/skills/knip --global
```

Project installation:

```bash
npx skills add https://github.com/asmundwien/knip-net/tree/v0.1.0-preview.1/skills/knip
```

Repository contributors may install the current checkout rather than a release:

```bash
npx skills add asmundwien/knip-net --skill knip
```

Keep the installer's confirmation prompt. Human-facing commands do not add `--yes`.

## Check the CLI

Run both:

```bash
dotnet tool list --global
dotnet-knip --version
```

Proceed only when the global tool list identifies `AsmundWien.Knip` at `0.1.0-preview.1` and the command reports `Knip.NET 0.1.0-preview.1`.

Also report a repository tool manifest when present. Do not install into it, restore from it, update it, or otherwise use it unless the user requests repository-local tooling.

## Ask before changing tools

Show the applicable command or complete command sequence, explain why it is needed, and ask for explicit approval before running it.

Missing package:

```bash
dotnet tool install --global AsmundWien.Knip --version 0.1.0-preview.1
```

Different installed `AsmundWien.Knip` version:

```bash
dotnet tool update --global AsmundWien.Knip --version 0.1.0-preview.1
```

When `0.1.0-preview.1` is lower than the installed version:

```bash
dotnet tool update --global AsmundWien.Knip --version 0.1.0-preview.1 --allow-downgrade
```

When another package owns `dotnet-knip`, show every removal and installation command before asking. For the legacy package:

```bash
dotnet tool uninstall --global Hdir.Knip
dotnet tool install --global AsmundWien.Knip --version 0.1.0-preview.1
```

Use the actual package ID from `dotnet tool list --global` for any other collision. Never install, update, downgrade, uninstall, or modify a tool manifest without explicit approval. If package identity and command version still disagree after an approved change, stop and report the PATH and package mismatch.