# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

## [0.1.23] - 2026-06-15

### Added

- Added `install.sh` for Linux CLI installs via `curl -fsSL https://raw.githubusercontent.com/ppotepa/rpack/main/install.sh | bash`.
- Added Linux PATH integration, `rpack-open`, optional `.rpack` file association through `xdg-mime`, and executable `.rpack` support through `binfmt_misc`.

### Fixed

- Fixed apply history recording for Git repositories without an initial `HEAD` commit.

## [0.1.22] - 2026-06-15

### Added

- Added `Rpack.App` use cases and shared DI registration for CLI and GUI orchestration.
- Added focused package, patch, action, state, Git adapter, and structured result boundaries.
- Added `Rpack.AgentPackages` with package-root validation, packing, planning, apply, repair, apply-remaining, and undo flows.
- Added structured issue/result output for JSON-friendly `check`, `lint`, `apply`, `rebase`, and agent package-root commands.
- Added docs for architecture, validation, diagnostics, package formats, agent packages, state, actions, LLM authoring, repair flow, and testing.
- Added fake-project CLI end-to-end coverage, including a five-package incremental scenario.

### Changed

- Routed CLI and GUI package workflows through shared use cases instead of direct domain orchestration.
- Reduced `RpackPackageService` to a compatibility facade backed by shared core services.
- Split Git operations into narrower adapter interfaces for repository inspection, patch operations, working tree state, worktree operations, and mutations.

### Fixed

- Fixed action execution for actions without `Path`, including `command` and `rpack.commit` actions.

## [0.1.17] - 2026-06-07

### Added

- Added `rpack rebase` for rewriting existing packages against the current target repository working tree and producing a new package.
- Added conflict policy options for already-present added files in `check` and `apply`:
  - `--resolve-added-file-conflicts <abort|skip|modify|overwrite>`
  - `--allow-existing-added-files <same>`
  - `as-modify` is accepted as an alias of `modify`.
- Added automatic `ADD`-to-`MODIFY` rewrite during package application when a new-file patch conflicts with an existing file and policy allows conversion.

### Changed

- Rebase and add-conflict handling are now exposed and documented in CLI usage and README.
- `rpack` still does not alter git history; `rebase` rewrites patch content only.

## [0.1.16] - 2026-06-07

### Added

- `inspect` now computes and surfaces full diff statistics from every patch in `manifest.Patches`:
  - patch/file totals
  - added and removed lines
  - hunk counts
  - binary file count
  - per-patch file breakdown
  - per-file +/− and hunk summary
- `rpack-open` batch view now shows aggregate package summary and per-file file/hunk stats in status details.
- `rpack` CLI now uses per-patch aggregated inspection for `inspect` and `open` output.

### Changed

- CLI/open output was updated to treat multi-patch manifests as ordered patch arrays (not just first/flattened file summaries) when producing changed-file stats.

## [0.1.15] - 2026-06-07

### Added

- Added a `Console log` tab to `rpack-open.exe` showing hidden process commands, working directories, exit codes, stdout, and stderr.

### Changed

- `ProcessRunner` now starts child processes with no visible console window.
- `rpack-open.exe` now keeps package errors in a `Status / errors` tab and captures Git command output during check/apply.

## [0.1.14] - 2026-06-07

### Changed

- Windows MSI builds now create both self-contained and framework-dependent installers.
- Framework-dependent MSI builds install all required publish output files and require the .NET 10 Desktop Runtime x64.

## [0.1.13] - 2026-06-07

### Changed

- Hardened `rpack-open.exe` startup handoff so multi-select and Shift-open package launches are collected into one batch window more reliably.
- Delayed launcher IPC startup until the WinForms window is ready and extended secondary process retry time during startup.

## [0.1.12] - 2026-06-07

### Added

- Added optional `Source.ProjectPath` manifest metadata for packages created by rpack.
- `rpack open` and `rpack-open.exe` now use `Source.ProjectPath` as a local repository hint when no explicit repository is provided.

### Changed

- `rpack inspect` now prints the source project path when present.

## [0.1.11] - 2026-06-07

### Added

- Added `rpack diagnose` for non-mutating package check reports with likely-cause summaries.
- Added `rpack lint` for package safety and patch quality checks.
- Added lint rules for generated output paths, `.rpack` files, common secret markers, local machine paths, large hunks, no-final-newline markers, and trailing whitespace.

### Changed

- Expanded the roadmap around diagnostics, idempotency, validation runner, semantic operations, snapshots, and three-way apply.

## [0.1.10] - 2026-06-07

### Added

- Added added-file comparison when a patch wants to add a file that already exists in the target repository.
- Added target/package byte count and timestamp diagnostics for differing already-present added files.

### Changed

- Already-present added files with matching content are now skipped safely so the rest of the package can apply.
- Empty transformed patches are now allowed during apply/check/undo.
- rpack still refuses to choose between differing files by size or timestamp.

## [0.1.9] - 2026-06-07

### Added

- Added `--strict` and `--strict-whitespace` for exact patch context whitespace matching.

### Changed

- `check`, `apply`, and `open` now use whitespace-compatible patch context matching by default.
- The Windows launcher now enables `Allow whitespace context match` by default.
- `--ignore-space-change` is now a compatibility no-op because that mode is the default.

## [0.1.8] - 2026-06-07

### Added

- Added `--ignore-space-change` for `check`, `apply`, and `open`.
- Added a Windows launcher checkbox for applying packages with whitespace context matching.
- Added whitespace diagnostics when strict patch validation fails but the full patch set passes with `--ignore-space-change`.

### Changed

- `rpack check` now validates all ordered patches in one non-mutating `git apply --check` call.
- Clean-tree detection now refreshes the Git index and checks real tracked diffs plus untracked files, avoiding false dirty-tree blockers from index stat drift.
- Multi-patch dry-run failures now try to report the manifest patch that owns the failing file.

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
