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

This repository is in early MVP stage. The current format is `rpack-v1` and focuses on one validated Git patch per package.

Implemented:

- `.rpack` as ZIP
- `manifest.json`
- one `git diff --binary` patch
- `create`, `inspect`, `check`, `apply`, `undo`, `history`
- checksum verification
- clean working tree requirement by default
- `git apply --check` before apply
- per-repository local state under Git metadata

Not implemented yet:

- package signing
- multiple patch files
- binary file overlay mode
- NuGet/global tool packaging
- GitHub release automation

## Install

For now, build from source:

```bash
dotnet build
```

Run through `dotnet run`:

```bash
dotnet run --project src/Rpack.Cli -- create -o change.rpack
```

Later releases should provide a .NET global tool and standalone binaries.

## Usage

Create a package from current unstaged working tree changes:

```bash
rpack create -o change.rpack
```

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

Undo the last applied package:

```bash
rpack undo
```

Show local rpack history:

```bash
rpack history
```

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

## Package Format

The MVP stores `.rpack` files as ZIP archives:

```txt
manifest.json
patches/change.patch
checksums.sha256
README.md
```

Example manifest:

```json
{
  "format": "rpack-v1",
  "id": "rpack-20260606154000",
  "title": "Repository patch package",
  "description": "",
  "createdAtUtc": "2026-06-06T15:40:00Z",
  "baseCommit": "abc123",
  "source": {
    "repository": "example",
    "baseCommit": "abc123",
    "headCommit": "abc123"
  },
  "requiresCleanTree": true,
  "mode": "working-tree-patch",
  "patches": [
    {
      "path": "patches/change.patch",
      "kind": "git-diff",
      "sha256": "..."
    }
  ],
  "validation": []
}
```

## Local State

Because `rpack` is intended to be installed and run from `PATH`, state is stored per target repository under Git's metadata path for `rpack`:

```txt
.git/rpack/
├─ apply-log.json
└─ applied/
   └─ <apply-id>/
      ├─ change.patch
      └─ manifest.json
```

`rpack undo` uses the stored patch and runs `git apply --reverse --check` before reverting it.

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
