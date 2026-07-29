# MailWhere portable package

## Start

1. Extract the ZIP to a user-writable folder.
2. Read `START_HERE_시작하기.txt`.
3. Run `MailWhere.exe`.
4. Keep automatic mail checking disabled until
   `docs/MANAGED_PC_SMOKE_TEST.md` passes on the target PC.

`MailWhere.Cli.exe` is the read-only JSON provider. It reads the existing local
SQLite database and does not load Outlook COM.

## Safety

- Outlook access is read-only.
- External LLM access is disabled by default.
- Mail bodies and the FTS mirror stay on the local PC.
- CLI/provider exports omit raw bodies and source locators.

The package contains only the current operational documents and
`BUILD-MANIFEST.json`; repository history, planning, and review material is not
included.
