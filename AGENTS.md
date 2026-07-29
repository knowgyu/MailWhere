# MailWhere agent notes

Use the installed user-level Codex/OMX surfaces from `~/.codex`. Do not commit
generated `.codex/` or `.omx/` runtime files into this repository.

Read durable project context in this order:

1. `docs/PROJECT_CONTEXT.md`
2. `docs/README.md`
3. `docs/history/parent-omx-import/README.md` only when historical provenance is needed

Keep the product boundaries intact:

- Outlook access is read-only.
- Mail bodies and the FTS mirror stay local.
- CLI/provider exports remain sanitized and body-free.
- External LLM access stays opt-in.
- Managed Windows/Classic Outlook/EDR claims require the real-PC smoke checklist.
