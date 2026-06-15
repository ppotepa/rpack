# rpack Progress

Last updated: 2026-06-15

## Done

- Captured the target refactoring direction in `target-refactoring-plan.agent-complete.md`.
- Fixed a real regression in `RpackPackageService.RunActions`: actions without `Path` no longer throw `ArgumentNullException`.
- Verified the repository baseline with `dotnet test`.
- Added structured result and issue model types in `src/Rpack.Core/Issues` and `src/Rpack.Core/Results`.
- Added `RpackResultMapper` to bridge legacy `RpackResult` and structured operation results.
- Added tests for legacy-to-structured result mapping and round-tripping.
- Extracted package reading, manifest normalization, manifest validation, and checksum verification into `src/Rpack.Core/Packages`.
- Extracted patch parsing and path-prefix transformation into `src/Rpack.Core/Patches`.
- Added focused component tests for manifest normalization, manifest validation, and checksum verification.
- Added focused component tests for patch parsing and path transformation.
- Extracted patch preparation, temp patch extraction, added-file conflict parsing, and added-file rewrite/convert handling into `src/Rpack.Core/Patches/RpackPatchPreparer.cs`.
- Added focused tests for patch preparation, added-file conflict handling, and conflict-resolution parsing.
- Extracted action execution and temp action handling into `src/Rpack.Core/Actions/RpackActionExecutionService.cs`.
- Extracted apply-log and stored-package state access into `src/Rpack.Core/State/RpackStateStore.cs`.
- Added focused tests for action extraction, state persistence, and stored-package loading.
- Extracted repository policy checks and patch-failure localization into `src/Rpack.Core/State/RpackRepositoryPolicyService.cs`.
- Moved apply-id generation into `RpackStateStore`.
- Added focused tests for apply-id generation and state-path resolution.
- Added `src/Rpack.App` with first package-level use cases for `check` and `apply`.
- Routed CLI `check` and `apply` through `Rpack.App` use cases.
- Added focused integration-style tests for `CheckPackageUseCase` and `ApplyPackageUseCase`.
- Added `Rpack.App` use cases for `diagnose`, `lint`, `rebase`, `undo`, and `history`.
- Routed CLI `diagnose`, `lint`, `rebase`, `undo`, and `history` through `Rpack.App`.
- Introduced shared DI registration in `src/Rpack.App/RpackServiceCollectionExtensions.cs`.
- Routed CLI composition through a single `ServiceCollection`/`ServiceProvider`.
- Added composition tests for service resolution and CLI use-case wiring.
- Added `src/Rpack.AgentPackages` with package-root manifest, operations, reader, validator, packer, and basic plan builder.
- Added CLI commands `pack-root`, `validate-package-root`, and `plan`.
- Expanded `src/Rpack.AgentPackages` with structured apply-plan modeling, payload inspection, and semantic conflict detection for package-root flows.
- Added `apply-root` for package-root payload operations with journal and rollback support under `.git/rpack/operation-journal`.
- Added `apply-remaining-root`, `diagnose-root`, and `undo-root` for agent-package recovery workflows.
- Added `repair-root` for generating repair package-roots from conflicted agent packages.
- Added `--json` support for core CLI `inspect`, `check`, `diagnose`, `lint`, and `apply`, plus `--llm`/`--conflicts` support for `diagnose`.
- Added `--json` support for agent `pack-root`, `validate-package-root`, `plan`, `diagnose-root`, and `repair-root` outputs, plus LLM-oriented conflict rendering.
- Added agent-root security scanning for generated outputs and secret markers, with fixture-backed rejection tests.
- Added agent-root security scanning for binary content, with structured `agent.risk-binary-change` rejection tests.
- Added structured `action.network-risk` lint coverage for action content that downloads or executes remote content.
- Added structured `action.untrusted-script` lint coverage for high-risk script execution patterns in actions.
- Added structured checksum verification coverage for `action.script-missing` and `action.script-checksum-mismatch`.
- Added structured undo coverage for `state.apply-log-missing` and `state.stored-package-missing`.
- Added structured history coverage for `state.history-invalid`.
- Added structured agent undo coverage for `state.journal-missing` and `state.backup-missing`.
- Added structured apply-state coverage for `state.store-failed`.
- Added structured agent undo safety coverage for `state.target-changed-after-apply` and `--force`.
- Added agent-root declared-files and validation-command manifest semantics, with structured issue codes for mismatch and command failure.
- Added agent-root fixture coverage for payload base mismatch and already-applied states.
- Added agent-root and agent-plan issue-code coverage for metadata missing, payload missing, base-hash mismatch, duplicate paths, and payload hash mismatch.
- Removed the dead helper block from `RpackPackageService`, leaving it much closer to a true compatibility facade.
- Aligned agent-package validator issue codes with the plan for metadata missing, duplicate paths, payload hash mismatch, and entry-missing handling.
- Added `PackageJobRunner`, `GuiResultMapper`, and `GuiIssueViewModel`, and routed `OpenBatchForm` check/apply orchestration through shared `Rpack.App` use cases.
- Added docs for architecture, package formats, validation, diagnostics, agent packages, state, actions, LLM authoring, and repair flow.
- Updated top-level docs after the fake-project E2E scenarios: README now reflects the current project layout and agent package commands, and `docs/testing.md` documents the single-package and five-package CLI scenarios.
- Added `install.sh` and README instructions for one-command Linux CLI install/update via `curl ... | bash`.
- Added fixtures-backed golden tests for agent package-root flows and current `rpack-v1` package creation/inspection.
- Added a fake-project end-to-end CLI scenario that creates, inspects, applies, and undoes a package on a synthetic repository.
- Added an incremental fake-project end-to-end CLI scenario that creates and applies five sequential feature packages, each changing at least three files.
- Added real `apply-remaining-root` skip-by-journal behavior so already-applied and repair-applied paths are no longer re-applied blindly.
- Added tests for CLI JSON/LLM formatting, agent package root packing, validation, semantic plan generation, package-root apply/journal rollback, repair metadata, conflict reporting, JSON output, repair-root generation, undo, and the existing composition/use-case suite.
- Added CLI integration tests that exercise JSON inspection, invalid-args exit codes, checksum failures, unsafe path-prefix failures, and action failure exit mapping through the built `rpack` binary.
- Added CLI integration tests that exercise agent-root JSON validation and pack output through the built `rpack` binary.
- Cleaned up the shared composition root so `AddRpackApp` now wires the package, patch, action, and state registrations explicitly instead of relying on empty stub methods.
- Removed the last direct `new RpackPackageService(...)` from the CLI open-path helper so package inspection now flows through the shared service instance.
- Added `CreatePackageUseCase` and `InspectPackageUseCase` in `src/Rpack.App` and routed CLI `create` and `inspect` through them.
- Extracted `check` into `RpackPackageCheckService` and routed both `CheckPackageUseCase` and `RpackPackageService.Check` through the shared pipeline.
- Added `RpackPackageApplyService` and routed `ApplyPackageUseCase` through the shared apply pipeline.
- Added `RpackPackageCreateService` and `RpackPackageInspectService`, then routed both App use cases and the compatibility service through the shared create/inspect pipelines.
- Added `RpackPackageDiagnoseService`, then routed both App and compatibility diagnose entry points through the shared diagnose pipeline.
- Added `RpackPackageLintService`, then routed `LintPackageUseCase` through the shared lint pipeline.
- Added `RpackPackageRebaseService`, then routed `RebasePackageUseCase` through the shared rebase pipeline.
- Added `RpackPackageUndoService` and `RpackPackageHistoryService`, then routed undo/history through shared state pipelines.
- Reduced `RpackPackageService` further so its public `Lint`, `Apply`, `Rebase`, `UndoLastApply`, and `ReadHistory` entry points now delegate to the shared Core services.
- Added a shared `RpackApplyPlanBuilder` and `RpackApplyPlan` so `check` and `apply` now share the same prepared package plan boundary.
- Added focused Git adapter interfaces for repository inspection, working-tree status, patch operations, worktree operations, and mutations, and routed the shared package pipelines through them.
- Added concrete Git adapter classes over `GitClient` and registered them in the shared composition root, so the new narrow interfaces are now backed by explicit adapters.
- Added structured check reporting to `RpackApplyPlanBuilder` so the shared check path now emits `RpackIssue` codes and warnings alongside the legacy result.
- Added a structured `check --json` path and taught diagnose to consume structured issues instead of only legacy strings.
- Added a structured `lint --json` path so lint now also emits `RpackIssue` codes and warning metadata through the shared runtime surface.
- Added a structured `apply --json` path so apply now emits `RpackIssue` codes for plan, patch, pre-action, and post-action failures.
- Added a structured `rebase --json` path so rebase now emits `RpackIssue` codes and output artifacts through the shared runtime surface.
- Updated `RpackAppUseCaseTests` to resolve use cases from the shared DI graph instead of constructing stale constructor signatures manually.
- Current test status: `99/99` passing.

## What That Means

- The current codebase is still the MVP shape, with most behavior concentrated in `src/Rpack.Core/RpackPackageService.cs`.
- The plan now exists as the implementation contract for future work.
- The immediate stability issue around action execution is resolved.
- The first refactoring phase is now started and verified.
- The package/manifest validation boundary is now split out of the god service.
- The patch parsing and rewrite boundary is now split out as well.

## Remaining Debt

- `RpackPackageService` is still a god service and needs to be split.
- Validation, patch parsing, conflict resolution, actions, state handling, and package-level orchestration now live largely in `Rpack.App`, but the compatibility service still exists.
- Structured issue/result models exist, but the runtime still needs to be migrated onto them.
- CLI and GUI still duplicate some orchestration and repository-resolution behavior, although the shared formatter, job runner, shared service wiring, and shared `create`/`inspect`/`check`/`apply`/`diagnose`/`lint`/`rebase`/`undo`/`history` pipelines have reduced the duplication.
- Agent-package support now has a working package-root MVP plus semantic planning, payload apply/journal rollback, conflict reporting, undo, apply-remaining skip-by-journal behavior, repair-root generation, and JSON/LLM automation output, while core CLI JSON/LLM output, GUI orchestration cleanup, docs, fixture-backed golden tests, and process-level CLI integration coverage are now in place; richer cross-package orchestration semantics are still planned but not implemented.
- Agent package root validation and packing now also emit structured results, so the agent-root boundary is starting to share the same issue/artifact model as the core CLI flows.
- Agent package root validation now also rejects secret markers and generated-output paths with structured agent risk codes.
- Agent package root validation now also rejects binary content with structured `agent.risk-binary-change` codes.
- Core lint now emits structured `action.network-risk` codes for remote-download and inline-execution action content.
- Core lint now emits structured `action.untrusted-script` codes for high-risk script execution patterns in action content.
- Core checksum verification now emits structured `action.script-missing` and `action.script-checksum-mismatch` codes while preserving legacy messages.
- Core undo now emits structured `state.apply-log-missing` and `state.stored-package-missing` codes while preserving legacy behavior.
- Core history now emits structured `state.history-invalid` when `.git/rpack/apply-log.json` is malformed.
- Agent undo now emits structured `state.journal-missing` and `state.backup-missing` codes, plus a structured target-changed undo failure.
- Core apply now emits structured `state.store-failed` when persisted apply state cannot be written, while preserving rollback behavior and legacy messaging.
- Agent undo now supports `--force` for target-changed-after-apply cases and keeps the structured undo failure path for non-forced runs.
- Agent package root validation now also supports declared-files matching and validation command execution, rejecting mismatches with structured agent issue codes.
- Agent package root fixture coverage now includes payload base mismatch and already-applied planner states, which strengthens the golden test coverage for phase 12 semantics.
- Agent-package planning now emits structured issue codes for missing metadata, missing payloads, base-hash mismatch, duplicate paths, and payload hash mismatch, so more of the agent taxonomy is visible in both text and JSON output.
- `RpackPackageService` no longer carries the old private helper block for patch checking and linting, which reduces the remaining facade debt in the core refactor.
- Agent-package validator codes now line up more closely with the plan taxonomy, especially `agent.metadata-missing`, `agent.operation-duplicate-path`, `agent.payload-hash-mismatch`, and `package.entry-missing`.
- The shared dependency-registration layer is now less stubbed and the CLI open helper is no longer constructing a fresh package service ad hoc.
- The main CLI entry points now go through `Rpack.App` use cases for `create`, `inspect`, `check`, `diagnose`, `lint`, `apply`, `rebase`, `undo`, `history`, and the agent-package workflows.
- The CLI `open` path now also routes inspection, check, and apply through shared `Rpack.App` use cases instead of calling `RpackPackageService` directly.
- The compatibility shell is thinner than before because `create`, `inspect`, and `check` are now backed by shared pipelines instead of living only inside `RpackPackageService`.
- The compatibility shell still exists, but the main `App` entry points now share both the check and apply pipelines instead of each carrying its own copy of the workflow.
- The compatibility shell now delegates `create` and `inspect` to shared Core services as well, so those entry points no longer maintain a duplicate implementation in `Rpack.App`.
- The compatibility shell now delegates `diagnose` to a shared Core service too, which removes one more copy of the inspect-plus-check formatting path.
- The lint workflow is also now shared instead of being carried only inside `Rpack.App`.
- Rebase is now also shared in Core instead of living only as a separate App implementation.
- Undo and history are now shared in Core instead of living only as separate App implementations.
- `check` and `apply` now share a dedicated `RpackApplyPlanBuilder` instead of each rebuilding their own package-preparation path.
- The shared package pipelines now depend on narrower Git interfaces instead of talking only to `GitClient` directly.
- The narrower Git interfaces now have explicit adapter implementations instead of being mapped straight back to `GitClient` in DI.
- The shared apply-plan boundary now carries a structured issue report, which is the first real runtime consumer of the new issue/result model.
- `check --json` now serializes structured issues directly, and diagnose now classifies failures from issue codes instead of raw message heuristics.
- `lint --json` now serializes structured issues as well, so another shared flow is no longer stuck on legacy string-only output.
- `apply --json` now serializes structured issues too, bringing the apply runtime onto the same issue/result surface as check and lint.
- `rebase --json` now serializes structured issues and exposes the rebased package artifact in JSON output.

## Next Step

Target refactoring work is complete for the current plan snapshot. Any further changes should start from a new backlog item or a revised target plan.

## Notes

- Do not treat this file as a spec; it is a living progress log.
- Treat `target-refactoring-plan.agent-complete.md` as the source of truth for the refactor.
