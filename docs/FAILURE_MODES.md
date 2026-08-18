# Failure Modes

| Failure | Behavior |
| --- | --- |
| Outlook COM unavailable | Outlook connector disabled; manual mode remains. |
| Inbox unreadable | Show degraded diagnostics; no crash loop. |
| Body unreadable | Metadata-only/manual selected-text mode. |
| LLM unavailable | `LlmOnly` leaves review-needed failure candidates; `LlmThenRules` falls back to rule-based analysis. |
| LLM capability proof missing or stale | Fail closed: do not start LLM analysis until the analysis-shaped probe passes for the current provider/endpoint/model/control fingerprint. |
| LLM thinking leaks or output truncates | Reject the probe or analysis result; keep affected items retryable in 확인 필요. |
| Readiness gate missing | Managed-mode 새 메일 자동 확인 disabled. |
| Storage unavailable | Disable task persistence and show blocked state. |

| Mail mirror inventory interrupted | Keep unseen local rows; only completed folder generations may delete. |
| Outlook item moved or deleted | Next completed folder inventory treats old locator as gone; source-open returns sanitized stale/unavailable status if explicit open fails. |
| Invalid `open_source_token` | Return sanitized failure; do not expose StoreID/EntryID or try broad Outlook lookup. |
| Skill install conflict | Yes overwrites bundled content without backup; No preserves and opens the current folder. |
| Outlook event missed | Event hints only wake sync; periodic inventory/reconcile recovers without trusting the event as authority. |
| EDR slows SQLite/FTS | Search remains local and bounded; collect timing metrics before choosing a separate DB. |
