# Roadmap

## Release 0.1.0 — portable read-only secretary

Goal: 집에서 빌드한 portable zip을 Windows 11에서 실행해 Outlook read-only scan, LLM/rule 분석, local task, tray reminder를 검증한다.

Included:

- Classic Outlook COM read-only mail scan.
- Recent-month mail scan with optional advanced max-count cap.
- Rule-based + optional LLM analyzer.
- Ollama native `/api/chat` and OpenAI-compatible `/v1/chat/completions` providers.
- Local SQLite task storage with source-derived redaction.
- Review candidate list display for low-confidence items.
- D-day labels and D-7/D-1/D-day reminder planning.
- 30-minute reminder timer while app is running.
- Korean-first WPF UI, tray host, app icon.
- Portable zip build in GitHub Actions with NuGet cache.

Not included yet:

- Mail mutation of any kind.
- Attachment auto analysis.
- Full calendar sync.
- Review candidate edit actions.
- Native app-notification activation actions.

## Release 0.1.2 — calm bulk scan and LLM visibility

Goal: 초기/대량 스캔을 상시 비서답게 조용하게 만들고, LLM endpoint 동작 여부를 사용자가 직접 확인할 수 있게 한다.

Included:

- Initial/bulk scan suppresses per-candidate popup storms and emits one digest.
- Daily board can be reopened from the main header or tray context menu.
- Scan progress/busy state is visible before Outlook/LLM work finishes.
- Recent-month scan defaults to date-window only; max-count is an advanced optional cap.
- LLM endpoint test sends a non-mail JSON probe and reports sanitized success/failure.
- LLM-enabled analysis is LLM-first, with explicit `LlmOnly` or `LlmThenRules` fallback policy.
- `LlmOnly` is the default; rule fallback requires explicit user selection or a failure modal confirmation.
- LLM model names can be loaded from Ollama `/api/tags` or OpenAI-compatible `/v1/models`.
- Scan status includes LLM attempts, success, fallback, failure, and average response time.
- Provider naming is protocol-first: `OllamaNative`, `OpenAiChatCompletions`, `OpenAiResponses`, with legacy config strings kept compatible.
- Review candidates can be snoozed from 검토함 and are hidden until the snooze time.
- Daily board has card-like list items and can jump directly to 검토함.
- Tray balloon click opens the actionable board instead of being a dead-end notification.

Not included yet:

- Bulk triage edit controls.
- Native Action Center click actions.
- Reply drafts or agentic mail mutation.

## Phase 0.2 — false positive control and daily-use inbox

Priority: high.

- Rich triage queue: bulk selection, due-date edit, and richer keyboard hints on visible buttons.
- Conservative automatic scan timer after smoke gate so new review candidates surface while the app stays in tray.
- Review tab: approve as task, dismiss as not-a-task, edit title/due date.
- “왜 이게 떴는지” panel: summary/reason/evidence/confidence/fallback source.
- Feedback loop: candidate-id not-a-task decisions suppress the current candidate; future duplicate suppression should stay non-destructive and avoid source-wide redaction.
- Scan result grouping: task created / review / ignored / duplicate / warning.
- Candidate-level LLM source markers: LLM result vs rule fallback vs LLM failure review.

Acceptance:

- A non-developer can clear all review candidates without opening a database.
- False positives can be dismissed permanently without raw body retention.
- Daily board appears once per local day without spamming repeated popups.

## Phase 0.3 — safer always-on behavior

Priority: high after 0.2.

- Outlook event subscription or conservative polling behind smoke gate.
- Startup auto scan when `AutomaticWatcherRequested && SmokeGatePassed`.
- Quiet hours and notification frequency settings.
- Native Windows app notification track if packaging identity is available.
- Local reminder history to prevent duplicate notifications across app restarts.

Acceptance:

- App can stay in tray all day without spamming notifications.
- Watcher failures degrade to manual scan and show a clear diagnostic code.

## Phase 0.4 — calendar shadow and OfficeWhere bridge

Priority: medium.

- Extract meeting/calendar candidates into a local shadow calendar table.
- Optional ICS export instead of direct calendar writes.
- OfficeWhere search handoff for task-related documents.
- Read-only task export for OfficeWhere indexing.

Acceptance:

- Meeting-like mails create local date-aware candidates.
- Document search handoff does not store full mail body in another index.

## Phase 0.5 — approved non-local LLM profiles

Priority: medium, only when approved endpoint details are known.

- Provider profiles: local Ollama, OpenAI-compatible Chat Completions/Responses, and only-approved remote/internal gateways.
- Per-profile data policy: endpoint, retention assumption, region, auth source, max prompt size, allowed fields.
- Structured-output schema validation for providers that support it.
- Redaction/preflight layer before remote calls.

Acceptance:

- Switching provider does not require code changes.
- API keys are read from environment variables or OS credential store, not committed settings.
- Remote providers stay disabled by default.

## Phase 1.0 — agentic secretary

Priority: later.

- Tool registry with side-effect labels: read, suggest, local-mutate, external-mutate.
- Agent planner that can call only allowed tools and emits a human-readable preamble.
- State compaction for long-running context: completed actions, open blockers, reminder IDs, next concrete goal.
- Audit log of tool proposals and local task mutations.
- Optional VLM for user-provided screenshots or explicitly selected files, never automatic attachment crawling.

Acceptance:

- The agent can explain why it proposed a reminder/task.
- The agent cannot send or modify mail unless a future explicit approval workflow is designed.
