# Failure Modes

| Failure | Behavior |
| --- | --- |
| Outlook COM unavailable | Outlook connector disabled; manual mode remains. |
| Inbox unreadable | Show degraded diagnostics; no crash loop. |
| Body unreadable | Metadata-only/manual selected-text mode. |
| LLM unavailable | `LlmOnly` leaves review-needed failure candidates; `LlmThenRules` falls back to rule-based analysis. |
| Readiness gate missing | Managed-mode 새 메일 자동 확인 disabled. |
| Storage unavailable | Disable task persistence and show blocked state. |

| Mail mirror inventory interrupted | Keep unseen local rows; only completed folder generations may delete. |
| Outlook item moved or deleted | Next completed folder inventory treats old locator as gone; source-open returns sanitized stale/unavailable status if explicit open fails. |
| Outlook event missed | Event hints only wake sync; periodic inventory/reconcile recovers without trusting the event as authority. |
| EDR slows SQLite/FTS | Search remains local and bounded; collect timing metrics before choosing a separate DB. |
