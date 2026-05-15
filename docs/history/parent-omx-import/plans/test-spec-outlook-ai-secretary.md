# Test Specification — Outlook COM AI Secretary

Generated: 20260514T131504Z UTC

## 1. Test Strategy

Use a layered test strategy:

1. **Core tests**: deterministic analyzer, date hints, confidence thresholds, task creation rules; run without Windows/Outlook.
2. **Storage tests**: SQLite schema, no raw body persistence, task status transitions.
3. **Probe tests**: fake probe implementations plus Windows real-machine smoke test.
4. **UI smoke tests**: WPF starts, diagnostics visible, task actions callable.
5. **Company smoke test**: run sanitized diagnostics on the company PC before enabling automatic analysis.

## 2. Phase 0 Acceptance Tests

| ID | Test | Expected |
| --- | --- | --- |
| P0-001 | Start app on Windows user session | Tray app opens without admin rights. |
| P0-002 | Run diagnostics with Outlook unavailable | Outlook connector disabled; manual/degraded mode remains. |
| P0-003 | Run diagnostics with Outlook available | COM/profile/inbox/body probe results are shown. |
| P0-004 | Export diagnostics | No subject/body/address/attachment data in export. |
| P0-005 | LLM endpoint unavailable | Rule-based/review mode only; no automatic high-confidence tasks. |

## 3. Phase 1 Acceptance Tests

| ID | Test | Expected |
| --- | --- | --- |
| P1-001 | Clear Korean action request with date | High-confidence task created with evidence and due date. |
| P1-002 | Clear English reply request | High-confidence reply/follow-up task created. |
| P1-003 | FYI/newsletter mail | No auto task; maybe low review candidate. |
| P1-004 | Ambiguous request | Review inbox candidate, no notification. |
| P1-005 | Same mail processed twice | No duplicate task/notification. |
| P1-006 | Mark task done | Status persists in SQLite. |
| P1-007 | Snooze task | Next reminder time updates; duplicate notification suppressed. |

## 4. Fixture Quality Targets

- Clear follow-up recall target: ≥80%.
- High-confidence task precision target: ≥85%.
- Every auto-created task includes source id/hash, confidence, reason, and evidence snippet.
- Medium/low confidence items never trigger urgent notification.

## 5. Security Tests

- External LLM disabled in company mode by default.
- Diagnostics contain capability names/statuses only.
- Repository schema has no raw body column for persisted tasks.
- Outlook adapter uses read-only access patterns only in Phase 0/1.

## 6. Windows Smoke Test Commands

On a Windows dev/company test machine with .NET 10 SDK:

```powershell
cd OutlookAiSecretary
dotnet restore
dotnet build OutlookAiSecretary.sln -c Release
dotnet run --project tests/OutlookAiSecretary.TestHarness/OutlookAiSecretary.TestHarness.csproj
```

Then run the WPF app:

```powershell
dotnet run --project src/OutlookAiSecretary.Windows/OutlookAiSecretary.Windows.csproj
```

## 7. Current Environment Verification

The current `/home/knowgyu/workspace` environment is Linux and does not have `dotnet` installed. Verification here must include:

- file/artifact existence,
- syntax/static inspection where possible,
- grep checks for forbidden automatic mutation methods in Phase 0/1 adapter,
- documented Windows verification gap.

## 8. Architect Revision Tests

| ID | Test | Expected |
| --- | --- | --- |
| SAFE-001 | Static scan Outlook adapter for forbidden mutation calls | No Phase 0/1 automatic path calls `Send`, `Delete`, `Move`, `Save`, `Reply`, `ReplyAll`, `Forward`, flag/category/read-state mutation, folder manipulation, or attachment open/save. |
| GATE-001 | Company mode without passing smoke gate | Automatic watcher disabled; manual/degraded mode available. |
| PRIV-001 | Persist analyzed task | Raw mail body not persisted; evidence snippet length capped. |
| PRIV-002 | Delete source-derived data | Evidence/summary/source-derived text removed while task shell can remain. |
| UX-001 | Mark candidate not-a-task | Candidate is suppressed and feedback persists. |
| BOUNDARY-001 | Build matrix | Core/test harness are cross-platform; WPF/COM are Windows-only and documented. |

## 9. Deliberate Expanded Test Plan

### Unit Tests

- UNIT-001 Korean explicit request with deadline creates high-confidence follow-up.
- UNIT-002 English reply request creates high-confidence follow-up.
- UNIT-003 FYI mail does not create high-confidence task.
- UNIT-004 Ambiguous request goes to review.
- UNIT-005 Evidence snippet is capped and raw body is not persisted.
- UNIT-006 Task status transitions: open -> snoozed -> done.
- UNIT-007 Not-a-task feedback suppresses duplicate candidate.
- UNIT-008 Startup setting toggles without enabling watcher gate by itself.

### Integration Tests

- INT-001 Fake mail source -> analyzer -> repository creates one task and no duplicate on reprocess.
- INT-002 LLM provider failure falls back to rule/review mode.
- INT-003 Gate service disables watcher when required probes are missing.
- INT-004 SQLite repository does not expose raw body fields.
- INT-005 Manual quick task creation persists task without source mail.
- INT-006 Manual selected/current-mail analysis works when automatic watcher is disabled.

### E2E / Manual Windows Smoke Tests

- E2E-001 WPF app starts as tray app on Windows 11.
- E2E-002 Diagnostics run against Classic Outlook without admin rights.
- E2E-003 Automatic watcher remains disabled until smoke gate passes.
- E2E-004 Startup registration toggle can be turned on and off.
- E2E-005 No Outlook mailbox mutation occurs during diagnostics or polling.

### Observability / Diagnostics Tests

- OBS-001 Diagnostics export includes probe statuses and feature flags only.
- OBS-002 Diagnostics export excludes fixture subjects, bodies, senders, recipients, attachment names, and evidence text.
- OBS-003 UI/diagnostics distinguish normal, degraded, blocked, and gate-not-passed states.
- OBS-004 Logs use error classes/codes, not raw mail content.

## 10. Review Cycle 1 Regression Tests

- RC1-001 `DeleteSourceDerivedDataAsync` replaces source-derived task title/reason and clears evidence.
- RC1-002 Review candidate source-derived fields can be cleared/suppressed by source hash.
- RC1-003 Diagnostics exporter allowlists only id/status/severity/safe code and safe detail keys; it never exports free-form message text.
- RC1-004 Runtime gate composition returns watcher disabled when company mode smoke gate is false.
- RC1-005 Outlook read result includes sanitized warnings/errors and skipped count.
- RC1-006 SQLite schema integration test proves no raw body column and evidence cap behavior.
- RC1-007 Startup Run command is quoted and setting state can be read.
- RC1-008 COM release helper exists and is used in probe/source finally paths.
