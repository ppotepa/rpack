# Roadmap

This roadmap is intentionally short. `rpack` should stay focused: validated patch packages for Git working trees, without touching Git history.

## Product Direction

`rpack` is already useful as:

```txt
diff -> package -> inspect -> check -> apply -> undo
```

The next evolution is:

```txt
change package + diagnostics + idempotency + validation + semantic operations
```

## Batch 001 - Diagnostics + Quality Gate

- Add `rpack diagnose` for non-mutating check reports with likely causes and suggested fixes.
- Add `rpack lint` for package safety checks.
- Detect generated output, `.rpack` files, common secrets, and local machine paths.
- Report quality risks such as binary patches for text files, large hunks, no-final-newline markers, and trailing whitespace.
- Keep diagnostics readable for humans and LLM agents.

## Batch 002 - Idempotent Check/Apply

- Add per-file final-state metadata with before/after SHA-256.
- Report `Pending`, `AlreadyApplied`, `Conflict`, `Missing`, and `Partial`.
- Make repeated application of the same package safe.
- Treat already-applied changes as success or warning, not failure.

## Batch 003 - Validation Runner

- Add `rpack validate`.
- Add `rpack apply --run-validation`.
- Show validation commands in `rpack-open` and require explicit approval before running them.
- Store validation logs under `.git/rpack/applied/<id>/validation-log.txt`.

## Batch 004 - Semantic Operations

- Add `operation-set` entries alongside `git-diff` patches.
- Support `delete-file`, `replace-file`, `add-file`, `rename-file`, `ensure-directory`, and `remove-empty-directory`.
- Make operations idempotent.
- Store preimages so undo can restore deleted/replaced files.

## Batch 005 - Snapshot Workflow

- Add `rpack snapshot create`.
- Add `rpack snapshot diff`.
- Add optional `rpack from-codecat` with explicit incomplete-snapshot warnings.
- Preserve exact file bytes, line endings, final-newline state, and file hashes.

## Batch 006 - Three-Way Apply

- Add `--3way` check/apply mode.
- Add `--allow-conflicts` only as an explicit expert mode.
- Prefer temporary worktree checks before touching the real working tree.

## Later

- Add `rpack-v2` for final-state metadata, operation sets, validation, and snapshots.
- Add release builds for Linux and macOS.
- Publish the .NET global tool to NuGet.
- Automate GitHub release artifacts.
- Add optional package signing and provenance metadata.

## Non-Goals

- Do not replace Git.
- Do not create commits by default.
- Do not modify Git history.
- Do not silently overwrite conflicting target files by size or timestamp.
- Do not leave conflict markers unless the user explicitly opts into an expert conflict mode.
