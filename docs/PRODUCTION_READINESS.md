# MailWhere production readiness

## Shipped and locally verified

- Inbox/Sent mirror with FTS5, bounded transactional writes, safe resume, and
  warning-aware reconciliation.
- Equal-timestamp checkpoint ordering across page boundaries.
- SQLite-only CLI and WPF search; Outlook COM is reached only after an explicit
  source-open action.
- Read-only mailbox behavior and sanitized diagnostics/export contracts.

## Remaining required evidence

Run `MANAGED_PC_SMOKE_TEST.md` on one representative managed Windows PC with
Classic Outlook and company EDR. Record only content-free evidence:

- start-to-first-count and total sync duration;
- Stop-to-stopped latency;
- seen, hydrated, skipped, and warning counts;
- repeated SQLite-only search during sync;
- cancel/resume, event update, reconciliation, privacy, and mailbox-unchanged
  pass/fail.

Do not call the product environment-validated until this evidence exists.

## Evidence-gated follow-ups

- Improve progress/cancellation only if start-to-first-count or Stop-to-stopped
  exceeds 5 seconds in the managed-PC run.
- Optimize COM lifetime or SQLite access only if measured timings show they are
  the bottleneck.
- Keep COM pooling, another database, background services, vector/graph
  subsystems, and mailbox mutation out of scope without new evidence.
