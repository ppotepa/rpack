# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.1.2] - 2026-06-06

### Added

- Added `rpack --version`.
- Added patch summary output to `rpack inspect`.
- Added .NET global tool package metadata.
- Added GitHub Actions CI.
- Added tests for checksum mismatch, unsafe archive paths, dirty working trees, and dirty undo behavior.

### Changed

- `rpack undo` now blocks extra dirty paths by default and allows them only with `--allow-dirty`.

## [0.1.1] - 2026-06-06

### Added

- Added MIT license.
- Added Windows MSI installer definition.
- Added `scripts/build-windows-msi.ps1`.
- Added release metadata to `Rpack.Cli.csproj`.

## [0.1.0] - 2026-06-06

Initial MVP.

### Added

- Created .NET 10 solution with `Rpack.Cli`, `Rpack.Core`, and `Rpack.Tests`.
- Added `.rpack` ZIP package creation.
- Added `manifest.json` with `rpack-v1` metadata.
- Added one-patch `working-tree-patch` mode.
- Added SHA-256 checksum verification.
- Added `rpack create`.
- Added `rpack inspect`.
- Added `rpack check`.
- Added `rpack apply`.
- Added `rpack undo`.
- Added `rpack history`.
- Added staged patch creation with `rpack create --staged`.
- Added revision range patch creation with `rpack create --from <rev> --to <rev>`.
- Added per-repository local state under Git metadata.
- Added tests for checksuming, patch transfer between repositories, staged patches, and undo.

### Changed

- Source/base commit mismatch is a warning by default.
- `--strict-base` makes base commit mismatch a blocker.
- `rpack` applies patches to the working tree and does not modify Git history.
