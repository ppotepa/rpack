# Testing

The test suite combines component tests, golden fixtures, and process-level CLI integration tests.

Primary command:

```bash
dotnet test tests/Rpack.Tests/Rpack.Tests.csproj
```

Key end-to-end scenarios:

- `FakeProjectScenario_CreatesInspectsAppliesAndUndoes` creates a small synthetic repository, packages a change through the real CLI, inspects JSON output, checks, applies, and undoes it.
- `IncrementalFakeProjectScenario_AppliesFiveFeaturePackages` creates five sequential feature packages on a small synthetic repository. Each package changes at least three files, is inspected with JSON output, checked, applied, and then committed on the source and target before the next package.
- Agent package-root fixture tests validate planning, payload apply, conflict diagnosis, repair package generation, apply-remaining behavior, and journal-backed undo.

These scenarios are intended to catch regressions in the public package workflow, not just individual components.
