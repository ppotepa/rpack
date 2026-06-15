# LLM Authoring

When authoring an agent package from a concat or snapshot:

- use repository-relative paths
- prefer payload operations over patches
- keep operation ids deterministic
- include base and target hashes where relevant
- avoid adding unsupported actions unless explicitly needed

The repair flow uses the same rule set, but only for the conflicted operations.
