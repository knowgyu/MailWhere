# Architecture

The app is layered so the risky Windows/Outlook pieces are adapters, not the core product logic.

```text
MailWhere.Core        cross-platform domain, analyzer, gates, pipeline, UI-agnostic product route/action semantics
MailWhere.Storage     SQLite persistence, raw-body-minimizing schema
MailWhere.OutlookCom  Windows-only Classic Outlook COM read adapter
MailWhere.Windows     WPF tray app, diagnostics, task/review UI
```

Phase 0/1 implementation is intentionally limited to read-only mailbox extraction, follow-up analysis, local task/reminder creation, diagnostics, and manual/degraded UX.
Core may define small semantic routing contracts such as scheduled board origins, today-board filtering, and task visibility so route behavior stays testable without WPF. Window presentation details—toast glyphs, colors, durations, layout, and control styling—belong in `MailWhere.Windows`.

Current product surface:

- `App` starts in tray-first mode and keeps shutdown explicit so closing the shell does not kill the assistant accidentally.
- `MainWindow` is now the primary 업무 보드 surface. Tray `열기`, tray `오늘 업무 보기`, scheduled daily-board routes, and toast CTAs converge here.
- The primary board defaults to `오늘`, then offers `이번 주`, `날짜 없음`, and `전체`. `DailyBoardWindow` remains as compatibility code but is no longer the primary route target.
- Review candidates, settings, and developer tools are separate WPF windows so the task list stays compact.
- Task rows expose only `열기`, `나중에`, `보관`; double-click opens the bounded edit dialog with Enter/Esc behavior.
- Scheduled daily-board time opens or updates the unified board first. Notification is a fallback only when the board cannot be surfaced.
- `LocalTaskStatus.Archived` is the active-list exit state for the user-facing `보관` action. Legacy `Done`/`Dismissed` values remain readable but are not primary UI actions.
- Future-snoozed tasks and archived tasks are excluded from primary active lists by `FollowUpPresentation.IsVisibleInPrimary`.

Runtime safety notes:

- Windows composition loads `runtime-settings.json` from local app data, defaulting to managed-safe manual mode when missing or unreadable.
- Outlook COM reads are dispatched through an STA executor before any future background polling or automatic mail-check loop uses the adapter.
- Diagnostics are exported through safe codes and validated allowlist values only; probe messages are not exported.
