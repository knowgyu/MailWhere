# ADR 0003 — Read-only first

Decision: Phase 0/1 may read/analyze/create local tasks/notify, but must not mutate Outlook.

Constraint: managed mailbox safety and user trust.
Rejected: automatic reply/send/delete/move, because the first version must prove safety and usefulness.
Confidence: high
Scope-risk: narrow
Directive: any mailbox mutation requires a new ADR and explicit user approval.
