# MailWhere skill install and repair

MailWhere v0.13.0 ships a bundled read-only skill for local Codex and Claude use. The skill is an offline helper around MailWhere's sanitized CLI/provider output. It does not read raw mail bodies, does not automate Outlook COM, and does not mutate the mailbox.

## Install targets

| Host | User skill folder |
| --- | --- |
| Codex | `%USERPROFILE%\.agents\skills\mailwhere` |
| Claude | `%USERPROFILE%\.claude\skills\mailwhere` |

Install from the `skills\mailwhere` folder inside the portable MailWhere directory. Keep the folder name `mailwhere`.

## Repair behavior

If the target folder already exists, the installer uses this fixed conflict policy:

| Choice | Result |
| --- | --- |
| Yes | Delete/overwrite the installed bundled content without creating a backup. Use this to repair a broken or stale bundled skill. |
| No | Preserve the current folder and open it so the user can inspect or move it manually. |

There is no automatic merge. A merge would risk mixing old instructions with the release-bundled safety contract.

## Safety contract

The skill may read only sanitized MailWhere outputs such as health, manifest, task/review lists, archive metadata, reply-progress metadata, and explicit search results. Normal CLI/skill/export output remains body-free and raw-locator-free.

Allowed original-open flow:

```powershell
.\MailWhere.exe --open-source-token <token>
```

This command is Windows-only and must be smoke-tested with Classic Outlook on the target PC. It is the only supported Skill launch path for original-open.

Rules:

- Use the token only after an explicit user request to open the original mail.
- Treat `open_source_token` as opaque.
- Do not print, derive, store, or request StoreID, EntryID, source id, or source hash.
- Do not automate Outlook directly from the skill.
- Return only sanitized success/failure status.

Internal MailWhere WPF/Outlook adapter code may retain raw locator values inside the trusted local process and SQLite boundary so explicit source-open can work. The skill must treat those values as unavailable implementation details, not as an API.

## Portable packaging check

A v0.13.0 portable ZIP should contain:

```text
skills/mailwhere/SKILL.md
skills/mailwhere/references/...
skills/mailwhere/manifest.json
docs/releases/v0.13.0.md
```

It should not contain `.omx` runtime files, local SQLite databases, mailbox exports, API keys, prompt logs, or raw-mail artifacts.
