# Security and Privacy

- External LLM providers are disabled by default in managed mode.
- Raw mail body is transient by default and is not part of the SQLite task schema.
- Evidence snippets are capped at 240 characters and can be deleted.
- Deleting source-derived data redacts task titles/reasons and review-candidate titles/reasons, not only evidence snippets.
- New tasks/review candidates may store a local Outlook source id, sender display name, received time, and recipient-role label only to reopen and summarize the original message read-only; source-derived deletion/not-task suppression clears or de-identifies these fields.
- Diagnostics must not include subjects, bodies, addresses, attachment names, or evidence text.
- Diagnostics exports are allowlist-based, validate allowed values, and omit free-form probe messages.
- Phase 0/1 must not mutate Outlook mailbox state.
