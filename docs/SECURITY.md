# Security and Privacy

- External LLM providers are disabled by default in managed mode.
- AI endpoint keys can be supplied directly for local testing or read from an environment variable. They must stay out of diagnostics, probe logs, sample defaults, and committed artifacts.
- Raw mail body is transient by default and is not part of the SQLite task schema.
- Evidence snippets are capped at 240 characters and can be deleted.
- Deleting source-derived data redacts task titles/reasons and review-candidate titles/reasons, not only evidence snippets.
- New tasks/review candidates may store a local Outlook source id, sender display name, received time, recipient-role label, and for sent multi-recipient waiting tasks only conversation/recipient display metadata needed for reply progress; source-derived deletion/not-task suppression clears or de-identifies these fields.
- Diagnostics must not include subjects, bodies, addresses, attachment names, or evidence text.
- Diagnostics exports are allowlist-based, validate allowed values, and omit free-form probe messages.
- `MailWhereExportService` is also allowlist-style: it exports board/archive/review/reply-progress metadata, not source ids/hashes, raw bodies, prompt payloads, evidence snippets, or full recipient lists.
- Phase 0/1 must not mutate Outlook mailbox state.


## Mail mirror retention

When the mail mirror is enabled, normalized plain-text mail bodies are retained locally in SQLite/FTS5 for search. This is a mirror, not an archive: completed warning-free Outlook folder inventories remove local searchable bodies for deleted or moved-away mail. The SQLite file is visible to the Windows user account and may be inspected by company EDR/security tools.

Mirror progress and diagnostics stay content-free: folder names, counts, warning codes, and sanitized error classes only; no subject, sender, recipient, StoreID, EntryID, or body text. Default exports and contextWhere-oriented machine-readable surfaces remain body-free: no raw body, StoreID, EntryID, source id/hash, prompt payloads, or full recipient lists. Explicit search returns bounded snippets only, and explicit source-open uses the current `(StoreID, EntryID)` internally without exporting that locator. Attachment contents belong in OfficeWhere, not MailWhere.
