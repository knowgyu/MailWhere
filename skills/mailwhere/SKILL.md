---
name: mailwhere
description: Read-only local MailWhere skill for searching the local mail mirror and opening originals through MailWhere.exe only when explicitly requested.
---

# MailWhere read-only skill

Use this skill only when the user explicitly asks to inspect MailWhere tasks, review candidates, or local mail search results on this Windows machine.

## Safety contract

- Read only: never send, delete, move, mark read, forward, or edit Outlook mail.
- Offline/local only: call the bundled `MailWhere.Cli.exe`; do not use Graph, COM, MCP, plugins, Python, or network services.
- Body-free by default: normal output may include bounded snippets only.
- Never print or ask for raw `store_id`, `entry_id`, or `source_id`.
- To open an original message, use only `MailWhere.exe --open-source-token <token>` after the user explicitly asks to open that result.

## Commands

Run from the portable MailWhere folder in PowerShell 7.6+:

```powershell
.\MailWhere.Cli.exe manifest --json
.\MailWhere.Cli.exe health --json
.\MailWhere.Cli.exe list-tasks --json --status open --due-window all --limit 50
.\MailWhere.Cli.exe list-review-candidates --json --limit 50
.\MailWhere.Cli.exe search-mail --json --query "검색어" --folder all --limit 20
```

Open original only on explicit user request:

```powershell
.\MailWhere.exe --open-source-token "<open_source_token>"
```

Report only sanitized status from MailWhere. Do not reveal mailbox locators.

See `references/contract.md` for the output contract.
