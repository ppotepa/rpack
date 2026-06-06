# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.1.7] - 2026-06-06

### Added

- Added single-instance IPC for `rpack-open.exe`, so multiple double-clicked packages are collected into one window.
- Added a Windows batch UI for inspecting, checking, and applying multiple `.rpack` files.
- Added per-package status, warning, and error details in the Windows launcher.
- Added clearer launcher error summaries for invalid packages, dirty trees, checksum failures, repository lookup failures, and patch dry-run failures.
- Added manifest patch path context to dry-run and apply failures.

## [0.1.6] - 2026-06-06

### Added

- Added the rpack SVG logo to the README.
- Added a generated Windows `.ico` asset from the rpack logo.
- Embedded the rpack icon into `rpack.exe` and `rpack-open.exe` for Windows builds.
- Installed the rpack icon through the MSI and used it for `.rpack` file association.

## [0.1.5] - 2026-06-06

### Added

- Added `rpack open` for a guided inspect/check/apply flow.
- Added Windows `rpack-open.exe` launcher with MessageBox confirmation dialogs.
- Added Shift-double-click dirty-tree mode for `.rpack` files installed through the MSI.
- Added MSI `.rpack` file association and extended context-menu action for allowing dirty working trees.
- Added repository discovery by walking upward from the package location.
- Added a clean-tree exception for the clicked package file during `open`.

## [0.1.4] - 2026-06-06

### Added

- Added `--path-prefix` for `inspect`, `check`, and `apply`.
- Added runtime patch path rewriting for packages built from repository subdirectory snapshots.
- Added tests for applying snapshot-root-relative package paths to Git-root-relative repositories.

### Changed

- Temporary transformed patches are written as UTF-8 without BOM before `git apply`.

## [0.1.3] - 2026-06-06

### Added

- Added `LLM_AGENTS.md` with instructions for coding agents preparing `.rpack` packages.
- Documented the manual `.rpack` ZIP structure and manifest format for agents that do not have `rpack create` available.
- Added support for multiple ordered patch entries in one `.rpack` package.
- Added tests for multi-patch apply, check failure rollback, and undo.

### Changed

- `rpack check`, `apply`, `inspect`, and `undo` now process the manifest `Patches` array in order.
- Stored apply state now preserves the applied manifest and all patch files for undo.

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
