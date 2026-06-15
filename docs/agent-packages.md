# Agent Packages

Agent packages are intended for packages generated from a repository concat or similar source snapshot.

The package-root flow supports:

- payload-backed file add/modify/replace/delete operations
- deterministic plan generation
- journal-backed apply and undo
- compact conflict diagnostics
- repair package generation from conflict output

This flow is intentionally local and repository-scoped.
