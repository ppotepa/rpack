<p align="center">
  <img src="assets/rpack.svg" width="96" alt="rpack logo">
</p>

# rpack

**Repository Pack**: portable, validated patch packages for Git working trees.

`rpack` is a small CLI tool for moving code changes between repositories as a single `.rpack` file. It does not create commits, branches, or otherwise modify Git history. It only validates and applies patches to the working tree.

```bash
rpack create -o change.rpack
rpack inspect change.rpack
rpack check change.rpack
rpack lint change.rpack
rpack diagnose change.rpack
rpack rebase change.rpack
rpack open change.rpack
rpack apply change.rpack
rpack undo
```

## Why

Plain patch files are useful, but they are easy to lose context around. `rpack` wraps a Git patch with:

- a manifest
- SHA-256 checksums
- package metadata
- dry-run validation
- local apply history
- undo support

The result is still simple: after `rpack apply`, you review the working tree and decide whether to commit.

## Current Status

This repository is in active MVP/refactor stage. The current stable package format is `rpack-v1` for ordered Git patch packages, with a newer `package-root` flow for agent-generated payload operations.

Implemented:

- `.rpack` as ZIP
- `manifest.json`
- one or more ordered `git diff --binary` patches
- `create`, `inspect`, `check`, `diagnose`, `lint`, `rebase`, `apply`, `undo`, `history`
- `open` for guided package application
- `pack-root`, `validate-package-root`, `plan`, `apply-root`, `apply-remaining-root`, `diagnose-root`, `repair-root`, `undo-root` for agent package-root workflows
- checksum verification
- clean working tree requirement by default
- `git apply --check` before apply
- per-repository local state under Git metadata
- detailed package summary and per-patch/per-file diff stats in `inspect`
- Windows `.rpack` file association through the MSI installer
- .NET global tool package metadata
- GitHub Actions CI
- runtime path prefix mapping for packages built from repository subdirectory snapshots

Not implemented yet:

- package signing
- binary file overlay mode
- NuGet publication
- GitHub release automation

## Install

Download the latest Windows MSI from the GitHub releases page:

```txt
https://github.com/ppotepa/rpack/releases
```

The MSI installs `rpack.exe`, `rpack-open.exe`, associates `.rpack` files with
rpack, and adds the installation directory to `PATH`.
Open a new terminal after installation if `rpack` is not immediately found.

Install or update the CLI on Linux with one command:

```bash
curl -fsSL https://raw.githubusercontent.com/ppotepa/rpack/main/install.sh | bash
```

The script clones or updates `https://github.com/ppotepa/rpack.git`, installs a
user-local .NET SDK 10 if needed, publishes `src/Rpack.Cli`, and writes
`~/.local/bin/rpack`. It does not install the Windows `rpack-open` GUI.

You can also build from source:

```bash
dotnet build
```

Run through `dotnet run`:

```bash
dotnet run --project src/Rpack.Cli -- create -o change.rpack
```

Build a local .NET tool package:

```bash
dotnet pack src/Rpack.Cli -c Release
```

Build Windows MSI locally (both variants at once):

```powershell
.\scripts\build-windows-msi.ps1 -Version 0.1.22
```

This creates two installers:

- `rpack-<version>-win-x64-self-contained.msi` includes the .NET runtime and is best for clean Windows machines.
- `rpack-<version>-win-x64-framework-dependent.msi` is much smaller and requires the .NET 10 Desktop Runtime x64 to be installed.

Build only one variant when needed:

```powershell
.\scripts\build-windows-msi.ps1 -Version 0.1.22 -Variant framework-dependent
.\scripts\build-windows-msi.ps1 -Version 0.1.22 -Variant self-contained
```

## Usage

Create a package from current unstaged working tree changes:

```bash
rpack create -o change.rpack
```

`rpack create` currently emits one aggregate patch entry. Packages created manually or by agents may include multiple ordered patch entries in `Patches`.

Set package metadata when useful:

```bash
rpack create -o change.rpack --id change-123 --title "Fix parser" --description "Parser and tests"
```

Create a package from staged changes:

```bash
rpack create --staged -o change.rpack
```

Create a package from a revision range, still as a working tree patch:

```bash
rpack create --from HEAD~2 --to HEAD -o change.rpack
```

### Manual multi-patch packages

`rpack create` writes one aggregate patch. To build a multi-patch package,
create the ZIP manually and declare every patch in `manifest.json`.

Each patch must be a Git diff that `git apply` can read, and patches must be
listed in the exact order they should be applied. Do not rely on filename
sorting; the `Patches` array is the source of truth.

For a patch series based on commits, create one diff per step:

```bash
mkdir -p package/patches
git diff --binary HEAD~2 HEAD~1 > package/patches/0001-core.patch
git diff --binary HEAD~1 HEAD > package/patches/0002-tests.patch
```

For independent working-tree areas, split by path:

```bash
mkdir -p package/patches
git diff --binary -- src/Rpack.Core > package/patches/0001-core.patch
git diff --binary -- tests > package/patches/0002-tests.patch
```

Compute SHA-256 for each patch and put the lowercase value into the matching
manifest entry. The current reader expects JSON property names in the casing
shown here, such as `Format`, `Mode`, `Patches`, `Path`, `Kind`, and `Sha256`.

```bash
sha256sum package/patches/*.patch
```

On PowerShell:

```powershell
Get-FileHash package\patches\*.patch -Algorithm SHA256
```

```json
"Patches": [
  {
    "Path": "patches/0001-core.patch",
    "Kind": "git-diff",
    "Sha256": "<lowercase-sha256-of-0001-core.patch>"
  },
  {
    "Path": "patches/0002-tests.patch",
    "Kind": "git-diff",
    "Sha256": "<lowercase-sha256-of-0002-tests.patch>"
  }
]
```

Recommended archive layout:

```txt
change.rpack
|-- manifest.json
|-- patches/
|   |-- 0001-core.patch
|   `-- 0002-tests.patch
|-- checksums.sha256
`-- README.md
```

`checksums.sha256` is optional for the current reader, but useful for humans:

```txt
<lowercase-sha256-of-0001-core.patch>  patches/0001-core.patch
<lowercase-sha256-of-0002-tests.patch>  patches/0002-tests.patch
```

Place `manifest.json` at the archive root and ZIP the package contents, not the
parent directory:

```bash
(cd package && zip -r ../change.rpack manifest.json patches checksums.sha256 README.md)
```

Before sharing a manual multi-patch package, verify it:

```bash
rpack inspect change.rpack
rpack check change.rpack
```

Rebase a package against another repository working tree:

```bash
rpack rebase change.rpack ./other-repo -o change-rebased.rpack
```

`rebase` applies the package to a detached worktree at the target repository
HEAD, then writes a new working-tree patch package. The rebased package currently
contains one aggregate patch entry, even if the input package had multiple
ordered patches.

Inspect a package without applying it:

```bash
rpack inspect change.rpack
```

`inspect` now shows a full patch breakdown:

- total file/line/hunk counts
- per-patch summaries
- per-file +/− lines, hunk counts, and category (Code/Tests/Docs/Scripts/Assets/Other)

Example:

```txt
Package summary:
- patches: 3
- files changed: 18
- lines added: 1240
- lines removed: 310
- total diff hunks: 86
- binary files: 0
```

Then for each patch:

```txt
PATCH 004 — InkFrame
- DefaultBuildInkFrameStep.cs +280 / -90 h:8 Code
- ... 
Subtotal: +420 -90 hunks:12
```

Check whether a package applies to the current repository:

```bash
rpack check change.rpack
```

Diagnose a failed check without modifying the working tree:

```bash
rpack diagnose change.rpack
```

Lint a package for generated output, common secret markers, local machine paths,
and fragile patch quality:

```bash
rpack lint change.rpack
```

Apply a package to the current repository:

```bash
rpack apply change.rpack
```

Handle existing-file add conflicts explicitly when needed:

```bash
rpack apply change.rpack --allow-existing-added-files modify
rpack apply change.rpack --resolve-added-file-conflicts as-modify
rpack apply change.rpack --allow-existing-added-files skip
rpack apply change.rpack --allow-existing-added-files overwrite
```

By default, `rpack` fails on added-file conflicts (`abort`). The same conflict
resolution modes are accepted by `check`, `diagnose`, `rebase`, and `apply`;
`as-modify` is accepted as an alias for `modify`.

Current text-file conflict behavior:

- `abort` reports the conflict and stops.
- `skip` removes that added-file block from the temporary patch when target
  content differs.
- `modify`, `as-modify`, and `overwrite` rewrite the added-file block as a
  modify patch against the existing target text file.

These modes do not overwrite binary or non-text added-file conflicts; those
still fail when Git cannot apply them safely.

Open a package with a guided inspect/check/apply flow:

```bash
rpack open change.rpack
```

`open` first looks for a Git repository by walking upward from the `.rpack`
file location. Packages created by current rpack versions also include
`Source.ProjectPath` as a local repository hint. When present and valid,
`rpack open` and `rpack-open.exe` can use that path, so a package can be opened
from Downloads or Desktop without copying it into the target repository.

An explicit repo argument still wins over the manifest hint:

```bash
rpack open change.rpack ./repo
rpack open change.rpack --repo ./repo
```

Check or apply to an explicit repository:

```bash
rpack check change.rpack ./repo
rpack apply change.rpack ./repo
```

Apply a package whose patch paths are relative to a repository subdirectory snapshot:

```bash
rpack inspect change.rpack --path-prefix src
rpack check change.rpack ./repo --path-prefix src
rpack apply change.rpack ./repo --path-prefix src
```

Use this when a package contains paths such as `aot/project/file.cs`, but the
real Git-root path is `src/aot/project/file.cs`.

By default, `rpack` lets Git ignore whitespace-only differences in patch context
lines. This makes packages more robust to CRLF/LF and final-newline drift between
working trees while still applying the actual changed lines from the patch.

Use strict patch context only when exact whitespace context is important:

```bash
rpack check change.rpack --strict
rpack apply change.rpack --strict
rpack open change.rpack --strict
```

`--strict-whitespace` is accepted as a more explicit alias. The older
`--ignore-space-change` flag is still accepted for compatibility, but it is now
the default behavior.

Undo the last applied package:

```bash
rpack undo
```

Show local rpack history:

```bash
rpack history
```

Print the installed version:

```bash
rpack --version
```

## Windows Double-Click

The Windows MSI registers `.rpack` files with `rpack-open.exe` and installs the
rpack icon for the file association. Both `rpack.exe` and `rpack-open.exe` are
published with the same embedded icon on Windows builds.

When one or more packages are double-clicked, rpack opens a single batch window.
Additional `.rpack` files opened while that window is already running are added
to the same list instead of opening more windows.
During multi-select opens, helper processes wait for the first window to finish
starting and hand their package paths to that same window. This also applies to
Shift dirty-tree mode.

For each package, rpack:

- finds the target Git repository from the package location
- uses `Source.ProjectPath` from the manifest when the package is outside a repository and the path exists locally
- inspects the package
- verifies checksums
- runs `git apply --check`
- shows status, warnings, and detailed errors in the batch window
- captures hidden `git` command output in the `Console log` tab
- applies checked ready packages only after explicit confirmation

Normal double-click keeps the default safety model and requires a clean working
tree. The clicked `.rpack` file itself is ignored for this clean-tree check when
it is stored inside the target repository, so an untracked incoming package does
not block its own application.

Hold Shift while double-clicking to open the same package with dirty working
tree allowed. This only enables the equivalent of:

```bash
rpack open change.rpack --allow-dirty
```

It does not bypass checksum verification, dry-run apply, or strict base behavior
when `--strict-base` is used. The context menu also includes an extended
Shift-right-click action named `Apply with rpack allowing dirty`.

The `Allow whitespace context match` checkbox is enabled by default. Uncheck it
to force strict patch context matching for pending packages.

The batch window stops applying at the first failed package and keeps the raw Git
or package error available in the `Status / errors` tab for diagnosis. Git
processes launched by `rpack-open.exe` run without visible console windows; their
stdout, stderr, working directory, and exit codes are shown in the `Console log`
tab.

## LLM Agents

If you want a coding agent such as ChatGPT or Codex to return changes as an `.rpack` file, see [LLM_AGENTS.md](LLM_AGENTS.md).

## Safety Model

By default, `rpack check` and `rpack apply` require:

- valid `.rpack` archive
- valid `manifest.json`
- matching SHA-256 checksums
- safe archive paths
- clean Git working tree based on real tracked diffs plus untracked files
- successful `git apply --check` with whitespace-compatible context matching

`rpack lint` additionally scans package paths and patch content for risky
generated outputs, `.rpack` files, common secret markers, local machine paths,
large hunks, no-final-newline markers, and trailing whitespace. Lint does not
modify the target repository.

Lint errors include generated or unsafe package paths such as `logs/`,
`artifacts/`, `release/`, `bin/`, `obj/`, `.env`, `.key`, `.pem`, `.pdb`,
`.exe`, `.dll`, `.rpack`, `.user`, and `.suo`. Lint also warns on local path
markers such as `D:\Git\`, `C:\Users\`, and `/home/`.

Source commit mismatch is a warning by default. This is intentional: `rpack` is meant to apply patches to compatible working trees, even when Git history differs.

Use `--strict-base` when the target repository must be at the recorded source base commit:

```bash
rpack apply change.rpack --strict-base
```

Use `--allow-dirty` only when applying into a dirty working tree is intentional:

```bash
rpack apply change.rpack --allow-dirty
rpack open change.rpack --allow-dirty
```

`rpack undo` blocks extra dirty paths by default and allows them only with:

```bash
rpack undo --allow-dirty
```

`--path-prefix` is applied only at check/apply time after package checksum
verification. It does not modify the `.rpack` file or its manifest.
The prefix must be a relative safe path. Absolute paths and prefixes containing
`..` are rejected.

Default whitespace-compatible context matching changes only Git patch context
matching. It does not skip checksum verification, clean-tree checks, base
checks, or package path safety.

Use `--strict` or `--strict-whitespace` to require exact context whitespace.

When a package tries to add files that already exist in the target repository,
`rpack` compares the existing target file with the file content encoded in the
patch. If the content matches, allowing CRLF/LF differences, rpack safely skips
that added-file block and applies the remaining patch. If the content differs,
`rpack check` reports an added-file conflict with target/package byte counts,
target last-write time, and package creation time.

rpack deliberately does not choose a winner by file size or timestamp. A larger
or newer file is useful diagnostic information, not a safe overwrite policy.

## Package Format

The MVP stores `.rpack` files as ZIP archives:

```txt
manifest.json
patches/
  change.patch
checksums.sha256
README.md
```

Only these archive parts are required by the current reader:

- `manifest.json`
- one or more patch files referenced by `manifest.json`
- a matching `Sha256` value in each manifest patch entry

`checksums.sha256` and package `README.md` are recommended for humans and future
tooling, but they are not currently required for validation.

Packages may contain multiple patch files. The `Patches` array in `manifest.json`
is ordered. `rpack` checks and applies patches in that order, and `rpack undo`
reverses the same patch list in reverse order.

Manifest validation rules:

- `Format` must be `rpack-v1`.
- `Mode` must be `working-tree-patch`.
- `Patches` must contain at least one entry.
- each patch entry must have non-empty `Path` and `Sha256`.
- each patch `Kind` must be `git-diff`.
- patch paths must be relative archive paths and must not contain `..`.
- every declared patch path must exist in the archive.
- the patch bytes must match the declared SHA-256 value.

Minimal valid manifest:

```json
{
  "Format": "rpack-v1",
  "Mode": "working-tree-patch",
  "RequiresCleanTree": true,
  "Patches": [
    {
      "Path": "patches/change.patch",
      "Kind": "git-diff",
      "Sha256": "<lowercase-sha256-of-patches/change.patch>"
    }
  ],
  "Validation": []
}
```

Example manifest:

```json
{
  "Format": "rpack-v1",
  "Id": "rpack-20260606154000",
  "Title": "Repository patch package",
  "Description": "",
  "CreatedAtUtc": "2026-06-06T15:40:00Z",
  "BaseCommit": "abc123",
  "Source": {
    "Repository": "example",
    "ProjectPath": "D:\\Git\\example",
    "BaseCommit": "abc123",
    "HeadCommit": "abc123"
  },
  "RequiresCleanTree": true,
  "Mode": "working-tree-patch",
  "Patches": [
    {
      "Path": "patches/0001-core.patch",
      "Kind": "git-diff",
      "Sha256": "..."
    },
    {
      "Path": "patches/0002-tests.patch",
      "Kind": "git-diff",
      "Sha256": "..."
    }
  ],
  "Validation": []
}
```

`Validation` must be an array of objects, not an array of strings. Use an empty
array when no package-level validation commands are declared. When validation is
included, each item uses the `Name`, `Command`, and `Optional` fields:

```json
"Validation": [
  {
    "Name": "Build",
    "Command": "dotnet build",
    "Optional": false
  }
]
```

In the current MVP, `Validation` entries are package metadata only. `rpack`
validates that the manifest shape is correct, but it does not execute these
commands yet.

## Local State

Because `rpack` is intended to be installed and run from `PATH`, state is stored per target repository under Git's metadata path for `rpack`:

```txt
.git/rpack/
├─ apply-log.json
└─ applied/
   └─ <apply-id>/
      ├─ patches/
      │  ├─ 0001-core.patch
      │  └─ 0002-tests.patch
      └─ manifest.json
```

`rpack undo` uses the stored manifest and patch files. It runs `git apply --reverse --check` for each patch before reverting them in reverse order.

## Development

Build:

```bash
dotnet build
```

Test:

```bash
dotnet test
```

Important end-to-end test coverage includes:

- a single fake-project CLI flow that creates, inspects, checks, applies, and undoes a package
- an incremental fake-project CLI flow that creates and applies five sequential feature packages, with each package changing at least three files
- agent package-root validation, planning, apply, repair, and undo fixtures

Project layout:

```txt
src/
  Rpack.App/
  Rpack.AgentPackages/
  Rpack.Cli/
  Rpack.Core/
  Rpack.Open/
tests/
  Rpack.Tests/
docs/
  architecture.md
  testing.md
  package-format-current.md
  package-format-agent.md
```

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

MIT. See [LICENSE](LICENSE).
