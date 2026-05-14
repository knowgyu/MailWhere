# ADR 0001 — Use Outlook COM read adapter

Decision: use Classic Outlook COM as the primary mail source for Phase 0/1.

Constraint: Managed desktop environments may not expose Graph, M365, Exchange, or vendor-specific mail APIs.
Rejected: direct vendor-specific mailbox export files parsing, because it is brittle and policy risky.
Confidence: medium
Scope-risk: moderate
Directive: keep the adapter read-only until a future explicit design changes this.
