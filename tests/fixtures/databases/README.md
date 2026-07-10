# Test Databases

> ## ⚠️ LEGACY — Electron implementation (status as of 2026-07-10)
>
> These fixtures belong to the **legacy Electron/Node.js** test suite.
> The **primary, actively developed product is now the Windows Native app (.NET 8 / WinUI 3)** under [`windows-native/`](../../../windows-native/).
> See [`windows-native/TESTING.md`](../../../windows-native/TESTING.md) for the primary test suite.

This directory contains test databases of various sizes.

Run `npm run test:seed` to generate:
- test-tiny.db (10 events)
- test-small.db (100 events)
- test-medium.db (500 events)
- test-large.db (1,000 events)
- test-stress.db (5,000+ events)

These databases are .gitignored and generated on-demand.
