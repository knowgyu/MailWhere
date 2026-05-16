# Failure Modes

| Failure | Behavior |
| --- | --- |
| Outlook COM unavailable | Outlook connector disabled; manual mode remains. |
| Inbox unreadable | Show degraded diagnostics; no crash loop. |
| Body unreadable | Metadata-only/manual selected-text mode. |
| LLM unavailable | `LlmOnly` leaves review-needed failure candidates; `LlmThenRules` falls back to rule-based analysis. |
| Readiness gate missing | Managed-mode 새 메일 자동 확인 disabled. |
| Storage unavailable | Disable task persistence and show blocked state. |
