# Repair Flow

The repair flow is:

1. apply a package
2. inspect conflicts
3. generate a compact conflict report
4. author a repair package-root
5. pack the repair package
6. apply the repair package
7. optionally apply remaining work and undo from journal history

The repair package must not mutate the original package.
