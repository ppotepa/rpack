# State

rpack keeps repository-local apply state under the Git metadata path.

State currently includes:

- apply log history
- stored manifests
- stored patch snapshots for `rpack-v1`
- operation journals for agent package applies

Undo should be driven from the recorded state rather than from ad hoc filesystem probing.
