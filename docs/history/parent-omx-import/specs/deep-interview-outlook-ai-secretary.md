# Deep Interview Spec — Outlook COM AI Secretary

## Metadata

- Slug: `outlook-ai-secretary`
- Profile: standard
- Context type: greenfield
- Rounds captured: 6
- Final ambiguity: **13.1%**
- Threshold: **20%**
- Context snapshot: `.omx/context/outlook-ai-secretary-20260514T125112Z.md`
- Transcript: `.omx/interviews/outlook-ai-secretary-20260514T130354Z.md`
- Generated: 20260514T130354Z UTC

## Clarity Breakdown

| Dimension | Score | Notes |
| --- | ---: | --- |
| Intent | 0.90 | Build an actually useful always-on office assistant, not a one-off prompt tool. |
| Outcome | 0.88 | Outlook mail-driven follow-up radar with local task/reminder UX. |
| Scope | 0.82 | All four product capabilities stay in the roadmap, with Phase 1 centered on follow-up monitoring. |
| Constraints | 0.90 | Closed Samsung environment; no Graph/M365/Exchange assumption; read/analyze/suggest first; local/company LLM only by default. |
| Success | 0.80 | Success judged by follow-up detection quality, low false positives, security/storage discipline, and daily usability. |

Weighted ambiguity is below threshold and readiness gates are satisfied.

## Intent

Create a Windows 11 resident AI secretary that can be left running during the workday and proactively prevents missed work caused by Outlook email overload. The app should be viable in a closed Samsung office environment where Microsoft Graph, Exchange, M365, Knox/mySingle APIs, or direct server integrations are unavailable or unreliable.

## Desired Outcome

A practical desktop app that uses **Classic Outlook COM** as the source of truth for mail already visible in Outlook. Phase 1 should automatically watch mail for follow-up signals, create local tasks/reminders when confidence is high, notify the user, and keep a local record without mutating Outlook or relying on external cloud LLMs.

## Product Phasing

### Phase 0 — Probe Harness / Skeleton

Purpose: make home development and company-PC validation safe.

- WPF/.NET 10 tray app shell.
- SQLite local database.
- Outlook COM capability probes:
  - Outlook installed and Classic COM automation available.
  - Default profile accessible.
  - Inbox readable.
  - Recent MailItem subject/sender/received time/body readable.
  - New-mail event available; if not, polling fallback can be enabled.
  - Calendar folder readable as optional signal only.
- LLM endpoint probe:
  - Company/local endpoint reachable.
  - External providers default off in company mode.
- Diagnostics export with **no mail subject/body/content**.
- Feature flags: connector-level failures disable only affected features, not the whole app.

### Phase 1 — Follow-up Radar MVP

Automatic core loop:

1. Read allowed Outlook MailItems and metadata.
2. Normalize mail body and thread context lightly.
3. Run allowed local/company LLM analysis.
4. Detect follow-up candidates:
   - user needs to reply,
   - someone requested action,
   - a deadline or implied due date exists,
   - user is waiting for another person’s reply,
   - a previously created follow-up is still unresolved.
5. If confidence is high, create a local task/reminder automatically.
6. If confidence is medium/low, send to review inbox rather than noisy notification.
7. Notify only for important/high-confidence reminders.
8. Allow task done/snooze/edit from tray/task inbox.
9. Store analysis state locally with raw-content minimization.

Manual/supporting features in Phase 1:

- Open task inbox.
- Manually create quick task/reminder.
- Manually analyze selected/current mail if automatic watcher fails.
- Export sanitized diagnostics.

### Phase 2 — Mail Triage / Summary

- Daily briefing of important mail and follow-up load.
- Mail importance classification.
- Thread summaries.
- Review inbox for medium-confidence items.
- Optional bounded historical backfill, if Phase 1 is stable.

### Phase 3 — Quick Capture / Assistant Command Palette

- Global hotkey quick capture.
- Natural-language task entry.
- Convert selected/copied mail text into task/reminder.
- Manual reply-draft generation on explicit user action.

### Phase 4 — Meeting / Calendar Preparation

- Parse meeting invitation mails and ICS attachments only when explicitly allowed.
- Build local shadow calendar from mail-derived signals.
- Meeting prep cards: related mails, open follow-ups, prep checklist.
- Outlook Calendar COM integration only if capability probe confirms it works.
- No full calendar synchronization requirement in initial phases.

## In Scope

- Windows user-session tray app, not a Windows Service.
- WPF UI on .NET 10 LTS.
- Classic Outlook COM integration as primary email connector.
- SQLite storage.
- Local/company LLM provider abstraction, OpenAI-compatible where useful.
- Capability-probe-first architecture.
- Follow-up detection, local task/reminder creation, notifications, task inbox UX.
- Clear docs for assumptions, probes, failure modes, and phase boundaries.
- Home-development test fixtures that mimic Korean/English corporate emails.

## Out of Scope / Non-goals

Phase 1 explicitly excludes:

- Automatic Outlook mutation: no auto send, delete, move, read/unread changes, forwarding, or folder manipulation.
- Direct Knox/mySingle/internal Samsung format parsing.
- Full server/calendar synchronization.
- Automatic attachment analysis.
- External LLM usage by default in company mode.
- Automatic reply-draft generation. Reply drafts require explicit user action or later phase.

Not a hard non-goal, but deferred unless later selected:

- Full historical inbox analysis/backfill.
- Broad RAG over all mail.
- Team/shared assistant behavior.
- Mobile app or cloud sync.

## Decision Boundaries

The app may do automatically in Phase 1:

- Read Outlook MailItem metadata/body through COM when Outlook exposes it.
- Run LLM analysis through allowed local/company endpoint.
- Create local SQLite tasks/reminders for high-confidence follow-ups.
- Show tray/Windows notifications.
- Record local message IDs/hashes, analysis status, extracted tasks, reminder state, summaries, and minimal evidence snippets.

The app must require explicit user action for:

- Generating reply drafts.
- Opening/analyzing attachments.
- Any Outlook-changing operation.
- Exporting diagnostics.
- Enabling external LLM providers.
- Running broad historical backfill.

The app must never attempt in Phase 1:

- Bypass or reverse-engineer Knox/mySingle internals.
- Send sensitive mail content to an external LLM by default.
- Mutate mailbox state without explicit future design approval.

## Constraints

- Company PC may have Classic Outlook 2016/2019-ish; treat version as capability-probe data, not a compile-time assumption.
- POP3/exported mail must be considered usable only if Outlook exposes it as MailItem.
- Outlook may be closed, restarted, locked by COM, or trigger security prompts.
- Home development cannot perfectly reproduce company PC policy; company smoke test must be first-class.
- Must be portable/self-contained where possible to avoid installer/admin friction.
- Diagnostics must not leak email subjects, bodies, addresses, or attachments unless explicitly requested and redacted.

## Testable Acceptance Criteria

### A. Follow-up Quality

- On a curated test fixture of Korean/English corporate mail snippets, the analyzer detects clear follow-up cases with target recall of **≥80%**.
- High-confidence auto-created tasks should target precision of **≥85%** on the fixture.
- Every created task includes:
  - title,
  - due date or “unknown due date”,
  - source mail reference,
  - confidence,
  - minimal evidence/reason.

### B. False-positive Control

- Medium/low confidence items go to review inbox, not automatic alert/task creation.
- Notification throttling prevents repeated alerts for the same mail/thread.
- User can mark a candidate as “not a task” and the system records that feedback locally.

### C. Security / Storage

- Company mode starts with external LLM providers disabled.
- No automatic Outlook mutations occur in Phase 1.
- Diagnostics export contains only capability statuses, error classes, versions, feature flags, and sanitized counts.
- Raw mail body persistence is off by default; store normalized summaries, extracted task fields, IDs/hashes, and minimal evidence only.

### D. Daily Usability

- App runs as a tray app in the user session.
- User can view, edit, snooze, and complete tasks/reminders.
- Startup registration can be toggled.
- If Outlook/LLM is unavailable, the app shows degraded mode clearly rather than crashing.

### E. Probe / Fallback Behavior

Although not the user's top PoC value metric, robust probing is required by the project premise:

- COM unavailable => Outlook connector disabled, manual clipboard/file mode remains available.
- Inbox unreadable => show blocker with sanitized diagnostic; no repeated crash loop.
- Body unreadable => metadata-only mode, manual selected-text mode available.
- NewMailEx unavailable => polling fallback.
- Calendar unreadable => local/shadow calendar only.
- LLM endpoint unavailable => rule-based review queue only; no automatic high-confidence tasks.

## Assumptions Exposed + Resolutions

| Assumption | Resolution |
| --- | --- |
| All four features are equally important. | Keep all four in the roadmap, but Phase 1 automatic loop is follow-up monitoring. |
| Outlook 2016 vs 2019 matters. | Treat as Classic Outlook COM capability, not exact version. Probe at runtime. |
| mySingle/Knox file formats might need parsing. | Do not parse them. Use Outlook-visible MailItem only. |
| Calendar integration may be unavailable. | Calendar is optional; start with mail-derived/local reminders. |
| AI can act autonomously. | Autonomy is bounded to read/analyze/local task/notify/index; mailbox mutation is prohibited. |
| External LLMs might be useful. | Dev mode may support them, but company mode defaults external providers off. |

## Pressure-pass Findings

The first answer said all four candidate capabilities were important. The follow-up forced a tradeoff: if only one loop can be automatic in v0.1, the user selected **Follow-up 자동 감시**. Later user clarified the other three should still be planned via phases. This resolves the product-shape tension: integrated assistant roadmap, follow-up radar MVP.

## Technical Context Findings

- Greenfield project; no existing relevant implementation identified in the current workspace.
- Recommended stack from prior planning discussion:
  - C# / .NET 10 LTS,
  - WPF tray app,
  - Microsoft.Office.Interop.Outlook / COM,
  - SQLite,
  - local/company LLM provider abstraction,
  - portable self-contained packaging first.

## Documentation Requirements

Create early docs before/with implementation:

- `docs/ASSUMPTIONS.md`
- `docs/CAPABILITY_PROBES.md`
- `docs/FAILURE_MODES.md`
- `docs/SECURITY.md`
- `docs/ARCHITECTURE.md`
- `docs/COMPANY_SMOKE_TEST.md`
- `docs/ADR/0001-use-outlook-com.md`
- `docs/ADR/0002-use-wpf-tray-app.md`
- `docs/ADR/0003-read-only-first.md`

## Recommended Handoff

Recommended next step: **`$ralplan`** using this spec.

Reason: requirements are now clear enough to stop interviewing, but architecture and test shape need validation before code generation because Outlook COM, company security posture, degraded modes, and packaging/fallbacks are central risks.

Suggested invocation:

```text
$plan --consensus --direct .omx/specs/deep-interview-outlook-ai-secretary.md
```

Alternative follow-ups:

- `$ultragoal`: useful after planning if you want durable goal tracking across phases.
- `$ralph`: useful after PRD/test-spec exist and a single persistent executor should build Phase 0/1.
- `$team`: useful later if splitting UI, Outlook connector, LLM analyzer, and storage/test harness into parallel implementation lanes.

## Condensed Transcript

See `.omx/interviews/outlook-ai-secretary-20260514T130354Z.md` for full captured rounds.
