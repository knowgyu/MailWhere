# MailWhere Project Context

This repository is the continuation point for the MailWhere / Outlook AI Secretary work that was initially planned from the parent workspace (`/home/knowgyu/workspace`).

## Imported parent-workspace artifacts

The parent workspace stored the early discussions and planning outputs under `../.omx`. Full-day logs were filtered to MailWhere/Outlook-relevant excerpts before being imported. They have been copied into this repository in two forms:

1. Runtime continuity copy under `.omx/` so local OMX/Ralph/plan workflows can discover the same context from this repo.
2. Versioned history copy under [`docs/history/parent-omx-import/`](history/parent-omx-import/) so the context travels with the repository.

The import manifest and checksums are in [`docs/history/parent-omx-import/README.md`](history/parent-omx-import/README.md).

## Reading order for future work

Use these artifacts when picking up product or implementation work:

1. [`docs/history/parent-omx-import/context/outlook-ai-secretary-20260514T125112Z.md`](history/parent-omx-import/context/outlook-ai-secretary-20260514T125112Z.md) — initial context snapshot and constraints.
2. [`docs/history/parent-omx-import/specs/deep-interview-outlook-ai-secretary.md`](history/parent-omx-import/specs/deep-interview-outlook-ai-secretary.md) — clarified requirements and phased scope.
3. [`docs/history/parent-omx-import/plans/prd-outlook-ai-secretary.md`](history/parent-omx-import/plans/prd-outlook-ai-secretary.md) — original product requirements.
4. [`docs/history/parent-omx-import/plans/ralplan-outlook-ai-secretary.md`](history/parent-omx-import/plans/ralplan-outlook-ai-secretary.md) and [`docs/history/parent-omx-import/plans/test-spec-outlook-ai-secretary.md`](history/parent-omx-import/plans/test-spec-outlook-ai-secretary.md) — execution/test plan.
5. [`docs/history/parent-omx-import/plans/prd-mailwhere-board-triage-ux.md`](history/parent-omx-import/plans/prd-mailwhere-board-triage-ux.md) and [`docs/history/parent-omx-import/plans/triage-policy-mailwhere-board-triage-ux.md`](history/parent-omx-import/plans/triage-policy-mailwhere-board-triage-ux.md) — later UX/triage refinement.
6. [`docs/history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md`](history/parent-omx-import/specs/autoresearch-codex-where-integration/report.md) — MailWhere + OfficeWhere Codex CLI integration research.
7. [`docs/history/parent-omx-import/logs/`](history/parent-omx-import/logs/) — filtered parent-session turn-log excerpts from the relevant dates.

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
- Daily Brief and 업무 보드는 분리한다: Daily Brief는 foreground highlight summary, 업무 보드는 active ledger.
- Low-confidence confirmation candidates are explicit-on-demand, not default noise.
- Snooze/complete/hide are local MailWhere task-state actions; Outlook 원본은 mutate하지 않는다.
- Agent CLI/skill/hook 연계는 sanitized read-only export seam으로 염두에 두되, 현재 제품 코드에는 MCP/full work-agent를 구현하지 않는다.
