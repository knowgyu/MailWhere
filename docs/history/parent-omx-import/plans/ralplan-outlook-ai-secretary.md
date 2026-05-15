# RALPLAN — Outlook COM AI Secretary

Generated: 20260514T131504Z UTC  
Requirements source: `.omx/specs/deep-interview-outlook-ai-secretary.md`

## RALPLAN-DR Summary

### Principles

1. **Probe before promise** — every Outlook/LLM/notification capability is runtime-probed and can degrade independently.
2. **Read-only mailbox first** — Phase 1 never sends, deletes, moves, forwards, or changes read state.
3. **Local/company privacy default** — company mode disables external LLM providers and avoids raw mail persistence.
4. **Useful daily loop over broad feature spread** — Phase 1 optimizes the follow-up radar; summary, quick capture, and meeting prep are phased afterward.
5. **Cross-platform core, Windows-only shell** — business rules and tests must run without Outlook/Windows; WPF/COM are adapters.

### Decision Drivers

1. **Closed corporate environment**: no Graph/M365/Exchange/Knox API assumptions.
2. **Company-PC uncertainty**: home development must ship probes, diagnostics, and fallbacks for unknown Outlook policies.
3. **Trust and noise control**: the assistant only becomes useful if it catches clear follow-ups without notification spam or data leakage.

### Viable Options

#### Option A — Windows-first monolith

- Pros: fastest WPF/COM integration; fewer projects.
- Cons: hard to test on Linux/home CI; business logic coupled to Outlook and UI; more fragile under company-PC failures.

#### Option B — Layered app with cross-platform core + Windows adapters **(Chosen)**

- Pros: follow-up detection, storage contracts, diagnostics, and test fixtures run without Outlook; COM risk isolated; future connectors possible.
- Cons: slightly more upfront structure; more interfaces to maintain.

#### Option C — Outlook add-in first

- Pros: best in-Outlook UX for current mail.
- Cons: harder deployment/policy story; weaker background secretary behavior; not ideal for POP3/classic uncertainty.

### ADR

**Decision:** Build a layered .NET solution: core domain/analyzer contracts target `net10.0`, Windows app/Outlook adapter target `net10.0-windows` with WPF and COM late binding, storage uses SQLite, and Phase 0/1 are implemented first.  
**Drivers:** closed environment, runtime capability uncertainty, privacy/noise control.  
**Alternatives considered:** Windows monolith, Outlook add-in first.  
**Why chosen:** isolates Outlook/Windows risk while preserving the intended tray-app user experience.  
**Consequences:** more scaffolding, but safer testing and clearer fallback behavior.  
**Follow-ups:** verify on a real Windows + Classic Outlook machine; revisit NewMailEx event adapter after polling proves stable.

## Product Roadmap

### Phase 0 — Probe Harness / Skeleton

Deliverables:
- WPF tray shell.
- Capability probes for Outlook COM, Inbox/body access, polling/new-mail support, optional calendar, LLM endpoint, notifications, DB path.
- Sanitized diagnostics export.
- Feature flags/degraded modes.
- Core fixture test harness.

### Phase 1 — Follow-up Radar MVP

Deliverables:
- Outlook mail ingestion through COM polling fallback.
- Normalization and follow-up analysis.
- High-confidence local task/reminder creation.
- Medium/low confidence review inbox model.
- Notification throttling contract.
- Task list UX: view/edit/snooze/done.
- Security storage defaults.

### Phase 2 — Mail Triage / Summary

Deliverables:
- Important-mail briefing.
- Thread summaries.
- Daily/closing digest.
- Bounded historical backfill gated by user action.

### Phase 3 — Quick Capture / Command Palette

Deliverables:
- Global hotkey command palette.
- Natural-language quick task capture.
- Clipboard/selected text mail analysis.
- Explicit reply-draft generation only on demand.

### Phase 4 — Meeting / Calendar Preparation

Deliverables:
- Meeting invitation / ICS parser gated by explicit allowlist.
- Local shadow calendar.
- Meeting prep cards.
- Optional Outlook Calendar COM connector if probe passes.

## Implementation Steps

1. Create solution skeleton and docs (`OutlookAiSecretary/`).
2. Implement core domain: mail snapshot, analysis result, local task, capability diagnostic, settings, clock.
3. Implement follow-up analyzer with deterministic rule-based fallback and LLM-provider interface.
4. Implement SQLite repository and raw-content-minimizing storage model.
5. Implement Windows/Outlook adapter with COM late-bound probe and polling recent-mail source.
6. Implement WPF shell: tray host, diagnostics window, task inbox, manual process button.
7. Implement test harness with Korean/English mail fixtures and precision/recall assertions.
8. Document company smoke test and phase roadmap.
9. Run verification: static file inspection in Linux; `dotnet build/test` on Windows/dev machine when .NET SDK is available.
10. Phase-by-phase autopilot: Phase 0/1 now; Phase 2 after real Outlook probe; Phase 3 after task UX stabilizes; Phase 4 after calendar/ICS feasibility.

## Risks and Mitigations

| Risk | Mitigation |
| --- | --- |
| Outlook COM blocked or security prompts appear | Capability probe; manual clipboard/file mode; no crash loop. |
| NewMailEx unreliable | Polling fallback based on EntryId/ReceivedTime/window. |
| LLM endpoint unavailable | Rule-based analyzer + review inbox; no automatic high-confidence tasks. |
| False positives create noise | Confidence threshold, evidence snippets, notification throttling, review inbox. |
| Sensitive content leakage | Company mode external off; raw body not persisted; sanitized diagnostics. |
| Linux environment cannot build WPF | Keep core code cross-platform; document Windows build command; add no-NuGet static checks in current environment. |

## Acceptance Criteria

See `.omx/plans/test-spec-outlook-ai-secretary.md`. Phase 0/1 is accepted when docs exist, core contracts are implemented, fixture analyzer passes on a .NET machine, WPF/Outlook adapter compiles on Windows, and Linux static verification passes here.

## Available Agent-Type Roster

- `explore`: repo mapping and file/symbol lookup.
- `executor`: bounded implementation work.
- `test-engineer`: fixture/test strategy and failure hardening.
- `architect`: design review and tradeoff validation.
- `code-reviewer`: final diff review.
- `verifier`: completion evidence and claim validation.
- `writer`: docs and smoke-test guidance.

## Follow-up Staffing Guidance

### Ralph path

Use one executor lane for implementation, one test-engineer/verifier lane for fixture and evidence review, and one architect lane for final design verification. Ralph should execute Phase 0/1 only against the PRD/test-spec, then stop with evidence and residual Windows verification gap if not running on Windows.

### Team path

Launch hints if coordinating in parallel:

```text
$team .omx/plans/prd-outlook-ai-secretary.md .omx/plans/test-spec-outlook-ai-secretary.md
```

Suggested lanes:
- Executor A: core domain/analyzer/storage contracts.
- Executor B: Windows WPF/Outlook adapter.
- Test-engineer: fixtures and probe smoke tests.
- Writer: docs/ADR/company smoke test.
- Verifier: integration evidence and gap audit.

Team verification path: prove core tests, storage behavior, sanitized diagnostics, and compile plan. Then Ralph can run a focused sequential verification/fix pass.

## Goal-Mode Follow-up Suggestions

- `$ultragoal`: recommended after Phase 0/1 to track the full multi-phase roadmap durably.
- `$performance-goal`: not needed until analyzer/COM polling latency becomes measurable.
- `$autoresearch-goal`: not needed unless researching Samsung/Outlook policy alternatives.

## Architect Iteration 1 Revisions

### Explicit Outlook COM Safety Contract

Phase 0/1 Outlook adapters are **read-only extractors**. Implementation must not call or expose automatic flows for:

- `MailItem.Send`, `Delete`, `Move`, `Save`, `Reply`, `ReplyAll`, `Forward`.
- Folder create/delete/move operations.
- Flag/category/importance/read-unread mutation.
- Rule manipulation.
- Attachment open/save/execute unless a later explicit user-approved feature is added.
- Inspector display/open side effects as part of automatic background processing.

Allowed Phase 0/1 calls are limited to application/session acquisition, folder/item enumeration, `GetItemFromID`, and reading scalar fields/body fields needed for analysis. Any write-like API must be isolated behind an interface absent from Phase 0/1 composition root.

### Hard Phase 0 → Phase 1 Runtime Gate

Phase 1 automatic watching is feature-flagged off until a real Windows/Classic Outlook smoke test records:

- COM available.
- default profile accessible.
- Inbox readable.
- recent item metadata readable.
- body readable or metadata-only fallback accepted.
- storage writable.
- company/local LLM endpoint reachable or rule-only mode accepted.

If the gate fails, the app remains in manual/degraded mode. Phase 1 code may exist, but the automatic watcher must not start in company mode without a passing gate record.

### SQLite Privacy and Retention Policy

- Raw mail body is transient by default and not persisted.
- Persisted evidence snippets must be short, user-visible, and deletable; default max length: 240 chars.
- Persisted display titles may contain sensitive derived content; UI must offer delete-source-derived-data per task.
- Diagnostics never include subjects, sender addresses, body text, evidence, or attachment names.
- DB path is under user-local app data by default, not shared folders.
- Windows implementation should apply current-user ACL assumptions; DPAPI/encryption-at-rest is a Phase 1.5 hardening option if company policy requires it.
- Retention settings must allow clearing completed tasks, evidence snippets, and analysis records.

### Source-to-PRD Alignment Fix

Phase 1 includes basic manual support:

- manual quick task/reminder creation from the task inbox;
- manual analyze-current/selected mail path when automatic watching fails;
- mark candidate as not-a-task and store feedback locally.

The richer global hotkey command palette remains Phase 3.

### Verification Boundary

- Cross-platform projects (`Core`, `Storage.Abstractions`, analyzer fixtures/test harness) must build/test on non-Windows .NET SDK environments.
- Windows-only projects (`Windows`, `OutlookCom`) target `net10.0-windows` and are verified on Windows only.
- Current Linux environment lacks `dotnet`; this run must use file/static verification and document the build gap.

## Critic Iteration 1 Revisions — Deliberate Gate

### Pre-mortem

1. **Sensitive content leakage**
   - Failure: diagnostics, logs, DB, or LLM requests include raw subjects/body/addresses.
   - Prevention: sanitized diagnostics schema; company mode external providers off; raw body transient; evidence capped; redaction tests; no request/response logging of body.
   - Detection: PRIV/OBS tests scan exported diagnostics and DB schema/records for fixture raw bodies.

2. **COM mutation accidentally introduced**
   - Failure: a later adapter calls `Save`, `Move`, `Delete`, `Send`, read-state mutation, or attachment open in an automatic path.
   - Prevention: no write-capable interface in Phase 0/1 composition root; static forbidden-call scan; code review checklist.
   - Detection: SAFE-001 grep/static test plus code-review blocker.

3. **Phase gate bypass on company PC**
   - Failure: automatic watcher starts before a real smoke gate passes, causing prompts, performance issues, or policy alarms.
   - Prevention: company mode default `AutomaticWatcherEnabled=false` until signed/local gate record exists.
   - Detection: GATE tests verify watcher disabled without gate and UI shows manual/degraded mode.

4. **Notification spam / low trust**
   - Failure: ambiguous mails create too many tasks/alerts, causing the user to disable the app.
   - Prevention: confidence threshold; review inbox; duplicate suppression; not-a-task feedback.
   - Detection: fixture false-positive tests and notification throttling tests.

### Expanded Test Plan

#### Unit

- Follow-up classifier fixtures in Korean/English.
- Confidence threshold behavior.
- Due-date extraction for explicit dates and unknown dates.
- Evidence truncation/redaction.
- Task state transitions: open, done, snoozed, not-a-task.
- Startup setting model toggles.

#### Integration

- SQLite repository writes/reads tasks and analysis records without raw body persistence.
- Capability gate combines probe results into enabled/disabled feature flags.
- LLM provider failure routes items to rule/review mode.
- Fake mail source + analyzer + repository pipeline avoids duplicates.

#### E2E / Manual Windows Smoke

- Run WPF app on Windows 11 with Classic Outlook.
- Execute diagnostics only.
- Confirm no mailbox mutation.
- Confirm Inbox/body probes and degraded-mode behavior.
- Enable watcher only after gate passes.
- Process a test mail or test profile item.
- Toggle startup registration on/off.

#### Observability / Diagnostics

- Diagnostics export includes feature flags, probe statuses, version/error classes, and sanitized counts.
- No subject/body/address/attachment names in diagnostics.
- Local logs use event IDs and error categories, not mail content.
- UI clearly displays normal, degraded, blocked, and gate-not-passed states.

### Project Path Contract

All implementation artifacts live under:

```text
OutlookAiSecretary/
  OutlookAiSecretary.sln
  docs/
  docs/ADR/
  src/OutlookAiSecretary.Core/
  src/OutlookAiSecretary.Storage/
  src/OutlookAiSecretary.OutlookCom/
  src/OutlookAiSecretary.Windows/
  tests/OutlookAiSecretary.Tests/
  tests/OutlookAiSecretary.TestHarness/
```

Source-level docs requested by the deep-interview spec are created under `OutlookAiSecretary/docs/`.

## Autopilot Review Cycle 1 Return-to-Ralplan Addendum

Code review and architecture review blocked approval of Phase 0/1 as-is. The next implementation pass must fix these blockers before another code-review cycle:

1. Source-derived deletion must scrub task title/reason/evidence and review-candidate source-derived fields, not only task evidence.
2. Diagnostics export must be allowlist-based and must not serialize free-form probe messages/details that can contain mail content.
3. Runtime composition must wire settings + probes + `FeatureGate`; automatic watcher must remain blocked in company mode without smoke-gate evidence.
4. Outlook mail reading must surface sanitized read failures/skipped item counts instead of silently returning empty/partial results.
5. Tests must include real SQLite privacy/schema behavior, not only fake store tests.
6. Startup registry command must quote executable paths and initialize checkbox state.
7. COM objects must be released where feasible after probes/polling.

## Completion Addendum — Autopilot Review Closure

Updated: 2026-05-14T13:56:00Z UTC

Status: **APPROVED / COMPLETE**

The approved RALPLAN was executed through the requested Autopilot loop. Phase 0/1 implementation landed in `/home/knowgyu/workspace/OutlookAiSecretary` and the review ambiguity was resolved conservatively.

### Implementation Evidence

- Repository: `/home/knowgyu/workspace/OutlookAiSecretary`
- Commit: `175da9c Make Phase 0/1 Outlook automation safe to resume`
- Tag: `phase0-1-safety-hardening`

### Review Closure

- Final code-review recommendation: **APPROVE**
- Final architecture status: **CLEAR**
- Resolved blocker: persisted partial `runtime-settings.json` can no longer bypass the Phase 0 smoke gate because smoke-gate enforcement is unconditional and partial settings merge over company-safe defaults.

### Verification Evidence

- `git diff --check`
- `./scripts/verify-static.sh`
- `.csproj` / `.xaml` XML parse
- crude C# brace-balance check
- forbidden Outlook mutation grep
- subagent code-review final recheck: no remaining severity-rated findings
- subagent architecture final recheck: CLEAR

### Remaining Verification Gap

`dotnet` and Outlook Classic are unavailable in this Linux environment. Windows `dotnet build/test`, WPF runtime smoke, registry behavior, and Outlook COM smoke remain the required next verification before claiming Windows/company-PC validation.

### Terminal State

This RALPLAN artifact is terminal for Phase 0/1 planning. Future work should start from the committed repo and this tag, not reopen planning unless Phase 2+ scope changes.
