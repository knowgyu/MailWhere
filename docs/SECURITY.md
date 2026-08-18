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
- CLI, skill-facing, export, diagnostics, and other normal machine-readable outputs are body-free and raw-locator-free. Search/list output may expose only an opaque `open_source_token` for explicit source-open.
- `MailWhere.exe --open-source-token <token>` is the only supported Skill launch path for opening an original mail. It resolves the token inside the local app/database boundary and returns only sanitized status.
- Internal WPF and Outlook adapter code may retain StoreID/EntryID/source locator values inside the trusted local process and SQLite boundary so explicit source-open can work. Those values are implementation details and must not be exported through CLI/skill/export/diagnostics normal outputs.
- Phase 0/1 must not mutate Outlook mailbox state.


## Mail mirror retention

When the mail mirror is enabled, normalized plain-text mail bodies are retained locally in SQLite/FTS5 for search. This is a mirror, not an archive: completed warning-free Outlook folder inventories remove local searchable bodies for deleted or moved-away mail. The SQLite file is visible to the Windows user account and may be inspected by company EDR/security tools.

Mirror progress and diagnostics stay content-free: folder names, counts, warning codes, and sanitized error classes only; no subject, sender, recipient, StoreID, EntryID, or body text. Default exports and contextWhere-oriented machine-readable surfaces remain body-free: no raw body, StoreID, EntryID, source id/hash, prompt payloads, or full recipient lists. Explicit search returns bounded snippets only, and explicit source-open uses an opaque token externally while resolving the current `(StoreID, EntryID)` internally without exporting that locator. Attachment contents belong in OfficeWhere, not MailWhere.

## External LLM and skill boundaries

External LLM access remains opt-in. v0.13.0 adds a stricter analysis-shaped probe before LLM analysis can run; a shallow JSON ping is not enough. Probe records and diagnostics may name provider/model/control modes and sanitized failure classes, but they must not include prompt payloads, raw responses, subjects, bodies, addresses, attachment names, API keys, StoreID, EntryID, source id, or source hash.

The bundled MailWhere skill is offline and read-only. It can help a local agent interpret sanitized MailWhere output, but it does not create a new mailbox access path. It must not automate Outlook COM, request raw locators, or display StoreID/EntryID. Its repair flow may overwrite the installed bundled folder only after the user chooses **Yes**; choosing **No** preserves the current folder and opens it.
