# PRD — Outlook COM AI Secretary

Generated: 20260514T131504Z UTC  
Requirements source: `.omx/specs/deep-interview-outlook-ai-secretary.md`

## 1. Problem

Samsung office email is critical, but server/API access is uncertain or unavailable. The user wants an always-on Windows assistant that watches Outlook-visible mail and prevents missed follow-ups without relying on Graph, Exchange, M365, Knox/mySingle internals, or external AI services by default.

## 2. Goals

- Build a Windows 11 user-session tray app that uses Classic Outlook COM as the primary mail source.
- Detect follow-up obligations and create local tasks/reminders with evidence and confidence.
- Keep the system safe in a closed company environment through read-only mailbox behavior, local/company LLM defaults, sanitized diagnostics, and graceful degradation.
- Plan all phases while implementing Phase 0/1 first.

## 3. Non-goals for Phase 0/1

- No automatic send/delete/move/forward/read-state changes.
- No Knox/mySingle reverse engineering or direct parsing.
- No full calendar synchronization.
- No automatic attachment analysis.
- No external LLM providers enabled by default in company mode.
- No automatic reply-draft generation.
- No broad historical inbox indexing unless later explicitly enabled.

## 4. Users and Jobs

### Primary user

A Samsung employee using Classic Outlook with POP3/exported company mail visible in Outlook.

### Primary job

"Keep this running so I do not miss important follow-ups, reply obligations, or deadline-bearing email requests."

## 5. Functional Requirements

### Phase 0: Probe Harness / Skeleton

- FR0.1 App starts as a Windows user-session tray app.
- FR0.2 App exposes diagnostics for Outlook COM, default profile, Inbox, body access, event/polling support, optional calendar, LLM endpoint, notification, storage.
- FR0.3 Diagnostics export is sanitized and excludes mail contents, subjects, addresses, and attachments.
- FR0.4 If a probe fails, only that feature is disabled; app remains usable in degraded/manual mode.
- FR0.5 App can run core analyzer tests without Outlook/Windows.

### Phase 1: Follow-up Radar MVP

- FR1.1 App reads Outlook MailItems via COM without mutating mailbox state.
- FR1.2 App normalizes mail snapshots into a safe internal model.
- FR1.3 App analyzes follow-up signals through local/company LLM provider when enabled, and deterministic rule fallback when unavailable.
- FR1.4 High-confidence follow-ups automatically become local tasks/reminders.
- FR1.5 Medium/low confidence candidates go to review inbox.
- FR1.6 Tasks include title, due date or unknown, source reference, confidence, reason/evidence, status, and snooze/done support.
- FR1.7 Notifications are throttled and never repeatedly alert the same source without state change.
- FR1.8 App stores only minimized raw-content data by default.

### Phase 2: Mail Triage / Summary

- FR2.1 Generate daily digest of important mail and open follow-ups.
- FR2.2 Summarize threads and classify mail importance.
- FR2.3 Support bounded, user-approved historical backfill.

### Phase 3: Quick Capture / Command Palette

- FR3.1 Global hotkey opens quick capture.
- FR3.2 Natural language creates local tasks/reminders.
- FR3.3 Clipboard/selected mail text can be analyzed manually.
- FR3.4 Reply draft generation is explicit action only.

### Phase 4: Meeting / Calendar Preparation

- FR4.1 Parse meeting invitation mails/ICS only when allowed.
- FR4.2 Maintain local shadow calendar.
- FR4.3 Generate meeting prep cards from mail-derived context.
- FR4.4 Probe Outlook Calendar COM as optional integration.

## 6. Non-functional Requirements

- NFR1 Privacy: company mode disables external LLM providers by default.
- NFR2 Safety: no Outlook mutation in Phase 0/1.
- NFR3 Reliability: degraded mode instead of crash loops.
- NFR4 Testability: core analyzer/storage abstractions test without Outlook.
- NFR5 Portability: portable/self-contained packaging is the first deployment target.
- NFR6 Maintainability: docs must record assumptions, probes, failure modes, ADRs.

## 7. UX Requirements

- Tray icon indicates normal/degraded/blocked state.
- Main window has tabs or sections: Tasks, Review Inbox, Diagnostics, Settings.
- Task actions: done, snooze, edit, open source reference placeholder.
- Diagnostics screen includes copy/export sanitized report.

## 8. Data Model

- `EmailSnapshot`: stable source id, received time, sender display/hash, subject hash/display policy, body for transient analysis only.
- `FollowUpAnalysis`: summary, confidence, follow-up type, reason, evidence snippet, due date, suggested title.
- `LocalTask`: id, title, due, confidence, source id/hash, evidence, status, snooze until, created/updated.
- `CapabilityReport`: probe id, status, severity, message, timestamp, sanitized details.

## 9. Phase 0/1 Definition of Done

- Planning/docs exist under `.omx/plans` and `OutlookAiSecretary/docs`.
- Solution scaffold exists with core, storage, Outlook adapter, Windows UI, and test harness projects.
- Core analyzer supports fixtures and produces structured analysis.
- SQLite schema/repository code exists and avoids raw body persistence.
- Outlook COM adapter has probe and polling-style recent mail reader with no mutation calls.
- WPF shell can run on Windows with diagnostic/task surfaces.
- Current Linux environment records verification limits because `dotnet` is unavailable.

## 10. Source References

- Microsoft documents WPF as a .NET desktop UI framework that runs on Windows.
- Microsoft documents Outlook `NewMailEx` as an Inbox new-item event that can fire for POP3 accounts.
- Microsoft warns Office apps are designed/tested for interactive client workstation use and not recommended as unattended non-interactive services.
- Microsoft lists .NET 10 as LTS supported until November 2028.

## 11. Architect Revision Requirements

### COM Safety Contract

Phase 0/1 code must not perform Outlook mutation. Forbidden automatic COM calls include `Send`, `Delete`, `Move`, `Save`, `Reply`, `ReplyAll`, `Forward`, read-state mutation, flag/category mutation, folder manipulation, attachment open/save/execute, and Inspector display side effects. The adapter layer must be structured so write-capable interfaces are not available to Phase 0/1 services.

### Phase Gate

Automatic Outlook watching/body analysis is disabled in company mode until a Phase 0 smoke gate passes on real Windows/Classic Outlook. If the gate has not passed, only diagnostics, manual task entry, and manual selected-text/current-mail analysis are available.

### Privacy / Retention

Persisted evidence snippets are capped and deletable. Raw bodies are transient by default. Diagnostics are sanitized. Retention settings must allow clearing completed tasks, evidence snippets, and analysis records. DB defaults to current-user local app data.

### Phase 1 Manual Support

Phase 1 includes basic manual task/reminder creation, manual selected/current mail analysis, and “not a task” feedback. The richer global command palette remains Phase 3.

## 12. Deliberate Revision Requirements

- Startup registration can be toggled on/off from settings and is off by default until the user enables it.
- Phase 1 includes manual quick task/reminder creation and manual selected/current-mail analysis when watcher is gated off.
- Implementation project path is `OutlookAiSecretary/`; docs live under `OutlookAiSecretary/docs/`.
- Automatic watcher in company mode is disabled until a Phase 0 smoke gate passes.
