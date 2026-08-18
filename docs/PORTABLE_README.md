# MailWhere portable package

## Start

1. Extract the ZIP to a user-writable folder.
2. Read `START_HERE_시작하기.txt`.
3. Run `MailWhere.exe`.
4. Keep automatic mail checking disabled until
   `docs/MANAGED_PC_SMOKE_TEST.md` passes on the target PC.

`MailWhere.Cli.exe` is the read-only JSON provider. It reads the existing local
SQLite database and does not load Outlook COM.

The package also includes the offline read-only MailWhere skill bundle under
`skills/mailwhere`. Install/repair instructions are in
`docs/SKILL_INSTALL.md`.

## Safety

- Outlook access is read-only.
- External LLM access is disabled by default.
- Mail bodies and the FTS mirror stay on the local PC.
- CLI/provider exports omit raw bodies and source locators.
- Skill-facing search/list output may include only an opaque
  `open_source_token`; explicit original-open stays inside `MailWhere.exe`.
  Internal WPF/Outlook adapter locators stay inside the local trusted
  process/database boundary and are not exported.

The package contains only the current operational documents and
`BUILD-MANIFEST.json`; repository history, planning, and review material is not
included.
