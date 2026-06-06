# rpack for LLM Agents

This document explains how coding agents should prepare and deliver `.rpack` files.

`rpack` is designed for agents that can edit a repository but should not directly commit, push, or rewrite Git history. The agent prepares a validated patch package. The human then inspects and applies it locally.

## Agent Contract

When asked to deliver changes as an `.rpack` package, the agent should:

- modify the working tree normally
- avoid creating commits
- avoid changing branches
- avoid rewriting Git history
- create a `.rpack` package from the final diff
- inspect the package
- check the package against the source repo when possible
- provide the package file and a short summary

The human applies the package with:

```bash
rpack check change.rpack
rpack apply change.rpack
```

## Basic Workflow

From the repository root:

```bash
git status --short
```

Make the requested code changes.

Review the final diff:

```bash
git diff --stat
git diff
```

Create the package from unstaged working tree changes:

```bash
rpack create -o change.rpack
```

Inspect the package:

```bash
rpack inspect change.rpack
```

Check that the package applies cleanly to the current repo:

```bash
rpack check change.rpack
```

Deliver `change.rpack` to the user.

## Staged-Only Workflow

If the agent needs precise control over what enters the package, stage only the intended files:

```bash
git add path/to/file1 path/to/file2
git diff --cached --stat
rpack create --staged -o change.rpack
```

Use this when the working tree contains unrelated changes that must not be included.

## Revision Range Workflow

If the agent is working from an existing commit range but still wants to deliver a working-tree patch package:

```bash
rpack create --from HEAD~2 --to HEAD -o change.rpack
```

This does not preserve commits. It packages the resulting diff as a patch.

## Expected Response To The User

When returning an `.rpack`, the agent should include:

```txt
Package: change.rpack
Format: rpack-v1
Mode: working-tree-patch
Changed files: <number>
Added lines: <number>
Removed lines: <number>
Validation:
- rpack inspect: passed
- rpack check: passed/failed/not run
Notes:
- <important assumptions or manual follow-up>
```

Do not paste the full patch into chat unless explicitly requested. Attach or provide the `.rpack` file.

## Applying A Package

The user should apply a received package from the target repository:

```bash
rpack inspect change.rpack
rpack check change.rpack
rpack apply change.rpack
```

If the target repo has different Git history but the patch context matches, `rpack check` may pass with a base commit warning. This is expected.

Use strict base matching only when the package must be applied to the exact recorded base commit:

```bash
rpack apply change.rpack --strict-base
```

## Undo

If the applied patch should be reverted:

```bash
rpack undo
```

By default, undo refuses to continue when there are dirty paths outside the last applied package. To override:

```bash
rpack undo --allow-dirty
```

## Safety Rules For Agents

Agents should not:

- include unrelated local changes
- include generated build outputs
- include secrets, tokens, credentials, or local machine paths
- create commits unless explicitly requested
- push branches unless explicitly requested
- bypass `rpack check` failures without explaining why

Agents should prefer:

- small focused packages
- clear package names, for example `camera-refactor.rpack`
- `--staged` packages when the working tree contains unrelated files
- short summaries with test results

## Recommended Package Names

Use descriptive lowercase filenames:

```txt
fix-login-validation.rpack
camera-refactor.rpack
update-readme-agent-docs.rpack
```

Avoid:

```txt
patch.rpack
final.rpack
stuff.rpack
```

## Troubleshooting

If `rpack create` says there are no changes:

```bash
git diff
git diff --cached
```

If `rpack check` fails because the working tree is dirty:

```bash
git status --short
```

Then either clean the tree or, only when intentional:

```bash
rpack check change.rpack --allow-dirty
```

If `rpack check` warns about base commit mismatch but still says the patch can be applied, the package is compatible by patch context.

If `rpack check` fails at `git apply --check`, the target repository is not compatible with the patch in its current state.
