# Code Review 0.1.0

Date: 2026-05-15

## Scope

Reviewed the sanitized 0.1.0 release candidate after the tray-first Outlook secretary implementation:

- Classic Outlook COM read-only adapter
- rule-based + optional LLM analysis
- SQLite local task/review storage
- WPF Korean-first UI and tray notifications
- portable Windows GitHub Actions release flow
- privacy/security docs and managed-PC smoke process

## Evidence

- Static verification: `bash scripts/verify-static.sh` passes on Linux; local `dotnet` is unavailable in this environment and is intentionally covered by Windows CI.
- Windows CI: GitHub Actions run `25869573870` succeeded for commit `f62a99e5679e8e21a5925f6c1cbea94a727798fd` with restore, build, tests, publish, and artifact upload.
- Privacy grep: no tracked-tree hits for project-specific sensitive terms after sanitization checks.
- Outlook mutation static check: `scripts/verify-static.sh` checks forbidden COM mutation patterns in the adapter.

## Applied review fixes before final release

1. `src/OutlookAiSecretary.Core/Analysis/RuleBasedFollowUpAnalyzer.cs`
   - Meeting/date-like text without an explicit action no longer auto-creates a task.
   - This reduces false positives for ambiguous calendar references.
2. `src/OutlookAiSecretary.Core/Analysis/LlmBackedFollowUpAnalyzer.cs`
   - LLM prompt payload keeps Korean text readable instead of JSON escaping every Hangul character.
   - This improves local LLM quality and made payload tests meaningful.
3. `src/OutlookAiSecretary.Windows/MainWindow.xaml` and `.xaml.cs`
   - Review candidates are now visible in the 검토함 tab instead of being only stored in SQLite.
   - Manual successful scans can record the smoke gate, but only when at least one mail is read and no blocked warning exists.
4. `src/OutlookAiSecretary.Storage/SqliteFollowUpStore.cs`
   - Added a read path for unsuppressed review candidates so non-developers do not need to open the database.
5. `tests/OutlookAiSecretary.Tests/Program.cs`
   - Added/updated coverage for LLM JSON, recent scan request window, ambiguous mail, reminders, and review-candidate listing.

## Security review

### CRITICAL

None found.

### HIGH

None found.

### MEDIUM

None blocking 0.1.0.

### LOW / follow-up

- `RuntimeSettings` still supports a direct `LlmApiKey` field for advanced/manual config. The UI nudges users to environment variables, but future docs should keep repeating “do not store secrets in config files”.
- `ShowErrorAsync` displays exception type and message in the UI. It is not persisted by the app, but a future sanitized error mapper would be safer for screenshots/support.
- Native Windows app notifications are not implemented yet; tray balloon fallback is acceptable for portable 0.1.0, but native notification activation should be evaluated when packaging identity is available.

## Code quality review

### CRITICAL/HIGH

None found.

### MEDIUM

None blocking 0.1.0.

### LOW / follow-up

- `MainWindow.xaml.cs` is now doing UI orchestration, settings persistence, scanning, reminder notification, and startup registration. For 0.2, split an application service/view-model layer before adding approve/dismiss/edit actions.
- Rule-based date parsing intentionally covers common Korean expressions only. It is test-backed but should remain conservative to avoid false positives.
- Review candidates are displayed as simple strings. For 0.2, use typed item models and buttons for approve/dismiss/edit.

## Architecture review

Architectural status: **CLEAR for 0.1.0**.

The current architecture matches the release goal:

- Outlook boundary is read-only and isolated in `OutlookAiSecretary.OutlookCom`.
- LLM boundary is opt-in and abstracted as provider clients.
- Local state is SQLite with source-derived truncation/redaction paths.
- WPF/tray shell is intentionally portable-first.

Watchlist, not release blockers:

- Add a local agent/tool boundary before expanding agentic behavior. Mutating tools should stay limited to local task state.
- Add a structured output validator for OpenAI-compatible providers that support strict schemas.
- Add local reminder history to avoid duplicates across restarts.
- Add OfficeWhere handoff through a read-only query/export bridge instead of sharing raw mail text.

## Usability review

0.1.0 is usable for a developer-friendly pilot:

1. Download portable zip.
2. Run `OutlookAiSecretary.Windows.exe`.
3. Run diagnostics.
4. Optionally configure local LLM endpoint.
5. Click “최근 1개월 스캔”.
6. Keep the app in tray for D-day reminders.

The largest non-developer gap is not extraction but **review control**: users can see low-confidence candidates now, but they cannot approve/dismiss/edit them in the UI yet. That should be 0.2 priority one.

## Final verdict

- Code-review recommendation: **APPROVE**
- Architectural status: **CLEAR**
- Release readiness: **Ready for v0.1.0 after the latest Windows CI pass on the final tagged commit**
