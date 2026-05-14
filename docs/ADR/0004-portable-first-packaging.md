# ADR 0004: Use portable zip before MSIX

## Status

Accepted for Phase 0/1.

## Context

The app must be developed mostly at home and then copied to a locked-down managed Windows 11 PC for Outlook COM smoke tests. The target environment may block installers, MSIX sideloading, untrusted signing certificates, or enterprise app registration. Phase 0/1 also keeps automation gated until diagnostics and smoke tests pass.

## Decision

Ship Phase 0/1 builds as a self-contained portable `win-x64` zip produced by GitHub Actions and `scripts/publish-portable.ps1`.

Do not add MSIX packaging until the target Windows PC confirms sideload/install policy and signing-certificate trust.

## Consequences

- The first deployment path is unzip-and-run, which has the highest chance of working on a closed managed Windows PC.
- The artifact can include operator docs and sample config without registering a Windows package identity.
- Automatic update, package identity, and some notification integration benefits are deferred.
- MSIX remains available as a later separate release lane once policy blockers are known.
