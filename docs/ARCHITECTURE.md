# Architecture

The app is layered so the risky Windows/Outlook pieces are adapters, not the core product logic.

```text
MailWhere.Core        cross-platform domain, analyzer, gates, pipeline
MailWhere.Storage     SQLite persistence, raw-body-minimizing schema
MailWhere.OutlookCom  Windows-only Classic Outlook COM read adapter
MailWhere.Windows     WPF tray app, diagnostics, task/review UI
```

Phase 0/1 implementation is intentionally limited to read-only mailbox extraction, follow-up analysis, local task/reminder creation, diagnostics, and manual/degraded UX.

Runtime safety notes:

- Windows composition loads `runtime-settings.json` from local app data, defaulting to managed-safe manual mode when missing or unreadable.
- Outlook COM reads are dispatched through an STA executor before any future background polling/event watcher uses the adapter.
- Diagnostics are exported through safe codes and validated allowlist values only; probe messages are not exported.
