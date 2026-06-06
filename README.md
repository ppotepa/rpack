<p align="center">
  <img src="assets/rpack.svg" width="96" alt="rpack logo">
</p>

# rpack

**Repository Pack**: portable, validated patch packages for Git working trees.

`rpack` is a small CLI tool for moving code changes between repositories as a single `.rpack` file. It does not create commits, branches, tags, rebases, or otherwise modify Git history. It only validates and applies patches to the working tree.

```bash
rpack create -o change.rpack
rpack inspect change.rpack
rpack check change.rpack
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

This repository is in early MVP stage. The current format is `rpack-v1` and focuses on ordered Git patch packages for working trees.

Implemented:

- `.rpack` as ZIP
- `manifest.json`
- one or more ordered `git diff --binary` patches
- `create`, `inspect`, `check`, `apply`, `undo`, `history`
- `open` for guided package application
- checksum verification
- clean working tree requirement by default
- `git apply --check` before apply
- per-repository local state under Git metadata
- patch summary in `inspect`
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

Build a Windows MSI locally:

```powershell
.\scripts\build-windows-msi.ps1 -Version 0.1.10
```

## Usage

Create a package from current unstaged working tree changes:

```bash
rpack create -o change.rpack
```

`rpack create` currently emits one aggregate patch entry. Packages created manually or by agents may include multiple ordered patch entries in `Patches`.

Create a package from staged changes:

```bash
rpack create --staged -o change.rpack
```

Create a package from a revision range, still as a working tree patch:

```bash
rpack create --from HEAD~2 --to HEAD -o change.rpack
```

Inspect a package without applying it:

```bash
rpack inspect change.rpack
```

`inspect` shows package metadata, changed file count, added/removed line counts,
and a short per-file patch summary.

Check whether a package applies to the current repository:

```bash
rpack check change.rpack
```

Apply a package to the current repository:

```bash
rpack apply change.rpack
```

Open a package with a guided inspect/check/apply flow:

```bash
rpack open change.rpack
```

`open` first looks for a Git repository by walking upward from the `.rpack`
file location. If the package is outside a repository, it falls back to the
current directory or an explicit repo argument:

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

For each package, rpack:

- finds the target Git repository from the package location
- inspects the package
- verifies checksums
- runs `git apply --check`
- shows status, warnings, and detailed errors in the batch window
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
or package error available in the details panel for diagnosis.

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

Packages may contain multiple patch files. The `Patches` array in `manifest.json`
is ordered. `rpack` checks and applies patches in that order, and `rpack undo`
reverses the same patch list in reverse order.

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

Project layout:

```txt
src/
  Rpack.Cli/
  Rpack.Core/
  Rpack.Open/
tests/
  Rpack.Tests/
```

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

MIT. See [LICENSE](LICENSE).
