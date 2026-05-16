# MailWhere Project Context

This repository is the continuation point for the MailWhere / Outlook AI Secretary work that was initially planned from the parent workspace (`/home/knowgyu/workspace`).

## Imported parent-workspace artifacts

The parent workspace stored the early discussions and planning outputs under `../.omx`. Full-day logs were filtered to MailWhere/Outlook-relevant excerpts before being imported. They have been copied into this repository in two forms:

1. Runtime continuity copy under `.omx/` so local OMX/Ralph/plan workflows can discover the same context from this repo.
2. Versioned history copy under [`docs/history/parent-omx-import/`](history/parent-omx-import/) so the context travels with the repository.

The import manifest and checksums are in [`docs/history/parent-omx-import/README.md`](history/parent-omx-import/README.md).

## Reading order for future work

Use these artifacts when picking up product or implementation work:

1. [`README.md`](../README.md) and [`docs/README.md`](README.md) — current product model and document map.
2. [`docs/VISUAL_QA_WORKFLOW_2026-05-16.md`](VISUAL_QA_WORKFLOW_2026-05-16.md) — latest tray-first, scheduled-board, `나중에`/`보관`, edit-dialog decisions.
3. [`docs/history/parent-omx-import/context/outlook-ai-secretary-20260514T125112Z.md`](history/parent-omx-import/context/outlook-ai-secretary-20260514T125112Z.md) — initial context snapshot and constraints.
4. [`docs/history/parent-omx-import/specs/deep-interview-outlook-ai-secretary.md`](history/parent-omx-import/specs/deep-interview-outlook-ai-secretary.md) — clarified requirements and phased scope.
5. [`docs/history/parent-omx-import/plans/prd-outlook-ai-secretary.md`](history/parent-omx-import/plans/prd-outlook-ai-secretary.md) — original product requirements.
6. [`docs/history/parent-omx-import/plans/prd-mailwhere-board-triage-ux.md`](history/parent-omx-import/plans/prd-mailwhere-board-triage-ux.md) and [`docs/history/parent-omx-import/plans/triage-policy-mailwhere-board-triage-ux.md`](history/parent-omx-import/plans/triage-policy-mailwhere-board-triage-ux.md) — earlier UX/triage refinement.
7. [`docs/history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md`](history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md) — MailWhere + OfficeWhere Codex CLI integration research.
8. [`docs/history/parent-omx-import/logs/`](history/parent-omx-import/logs/) — filtered parent-session turn-log excerpts from the relevant dates.

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
- Agent CLI/skill/hook 연계는 sanitized read-only export seam으로 염두에 두되, 현재 제품 코드에는 MCP/full work-agent를 구현하지 않는다.
