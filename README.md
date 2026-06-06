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
- checksum verification
- clean working tree requirement by default
- `git apply --check` before apply
- per-repository local state under Git metadata
- patch summary in `inspect`
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

The MSI installs `rpack.exe` and adds the installation directory to `PATH`.
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
.\scripts\build-windows-msi.ps1 -Version 0.1.4
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

## LLM Agents

If you want a coding agent such as ChatGPT or Codex to return changes as an `.rpack` file, see [LLM_AGENTS.md](LLM_AGENTS.md).

## Safety Model

By default, `rpack check` and `rpack apply` require:

- valid `.rpack` archive
- valid `manifest.json`
- matching SHA-256 checksums
- safe archive paths
- clean Git working tree
- successful `git apply --check`

Source commit mismatch is a warning by default. This is intentional: `rpack` is meant to apply patches to compatible working trees, even when Git history differs.

Use `--strict-base` when the target repository must be at the recorded source base commit:

```bash
rpack apply change.rpack --strict-base
```

Use `--allow-dirty` only when applying into a dirty working tree is intentional:

```bash
rpack apply change.rpack --allow-dirty
```

`rpack undo` blocks extra dirty paths by default and allows them only with:

```bash
rpack undo --allow-dirty
```

`--path-prefix` is applied only at check/apply time after package checksum
verification. It does not modify the `.rpack` file or its manifest.

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
tests/
  Rpack.Tests/
```

## Roadmap

See [ROADMAP.md](ROADMAP.md).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

MIT. See [LICENSE](LICENSE).
