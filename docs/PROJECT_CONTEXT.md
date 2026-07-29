# MailWhere Project Context

This repository is the continuation point for the MailWhere / Outlook AI Secretary work that was initially planned from the parent workspace (`/home/knowgyu/workspace`).

## Imported parent-workspace artifacts

The parent workspace stored the early discussions and planning outputs under `../.omx`. Full-day logs were filtered to MailWhere/Outlook-relevant excerpts before being imported. Durable imported context lives in the versioned [`docs/history/parent-omx-import/`](history/parent-omx-import/) copy. Local `.omx/` state is generated, ignored runtime data and is not a source of truth.

The import manifest and checksums are in [`docs/history/parent-omx-import/README.md`](history/parent-omx-import/README.md).

## Reading order for future work

Use these artifacts when picking up product or implementation work:

1. [`README.md`](../README.md), [`DESIGN.md`](../DESIGN.md), and [`docs/README.md`](README.md) — current product, search UX, and document map.
2. [`docs/ARCHITECTURE.md`](ARCHITECTURE.md) and [`docs/SECURITY.md`](SECURITY.md) — current runtime, provider, retention, and export boundaries.
3. [`docs/releases/v0.12.1.md`](releases/v0.12.1.md) and [`docs/MANAGED_PC_SMOKE_TEST.md`](MANAGED_PC_SMOKE_TEST.md) — latest shipped behavior and remaining managed-PC validation.
4. [`docs/VISUAL_QA_WORKFLOW_2026-05-16.md`](VISUAL_QA_WORKFLOW_2026-05-16.md) — historical tray-first UI decisions.
5. [`docs/history/parent-omx-import/`](history/parent-omx-import/) — historical context, plans, research, and filtered logs. These records do not override current docs.

## Current implementation anchor

The current codebase already contains the implementation artifacts created from that planning work:

- Solution: [`MailWhere.sln`](../MailWhere.sln)
- Core logic: [`src/MailWhere.Core/`](../src/MailWhere.Core/)
- Outlook COM integration: [`src/MailWhere.OutlookCom/`](../src/MailWhere.OutlookCom/)
- WPF tray app: [`src/MailWhere.Windows/`](../src/MailWhere.Windows/)
- SQLite storage: [`src/MailWhere.Storage/`](../src/MailWhere.Storage/)
- Tests: [`tests/MailWhere.Tests/`](../tests/MailWhere.Tests/)
- Release scripts: [`scripts/`](../scripts/)
- Operational docs: [`docs/`](./)

## Key preserved product constraints

- Classic Outlook COM is the primary mail source; Microsoft Graph/Exchange/M365/Knox internals are not assumed.
- Phase 0/1 must remain read-only against the mailbox: no automatic send, delete, move, forward, or read-state mutation.
- External LLM usage is off by default; company/local endpoint mode must be explicit.
- Raw mail bodies, subjects, addresses, attachments, prompts, and sensitive diagnostics should not be persisted unnecessarily.
- Missing COM/LLM/notification/storage capabilities should degrade features rather than crash the whole app.
- Scheduled 오늘 업무 보드는 primary morning surface다. Notification is fallback when the board surface cannot be opened.
- 업무 보드는 active ledger이고, tray의 `오늘 업무 보기`로 다시 열 수 있다.
- Low-confidence confirmation candidates are explicit-on-demand, not default noise.
- Snooze and archive are local MailWhere task-state actions; Outlook 원본은 mutate하지 않는다. `나중에`는 다시 표시되고 `보관`은 active board에서 제외된다.
- contextWhere/agent 연계는 sanitized read-only CLI/export seam만 사용한다. Cross-provider evidence/wiki/context-pack orchestration과 MCP/full work-agent는 MailWhere에 구현하지 않는다.
