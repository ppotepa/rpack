# Architecture

rpack is split into a small core, shared application use cases, an agent package engine, and a Windows shell.

Current intent:

- `Rpack.Core` holds domain models, Git/process adapters, and low-level package helpers.
- `Rpack.App` owns orchestration for create, inspect, check, apply, diagnose, lint, rebase, undo, and history.
- `Rpack.AgentPackages` owns `package-root`/`.rpack` agent packaging, planning, apply, repair, and journal flow.
- `Rpack.Cli` is the command-line shell and formatting layer.
- `Rpack.Open` is the Windows launcher and batch UI.

The refactor target is to keep orchestration out of UI surfaces and to keep package-specific behavior behind focused boundaries.

Current validation coverage includes process-level CLI scenarios for both a single package lifecycle and five sequential feature packages on a synthetic repository.
