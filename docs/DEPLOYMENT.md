# Deployment

## Recommendation: portable first, MSIX later

For the first managed-PC smoke tests, use a portable self-contained zip.

Why portable is the default:

- It does not require admin install, MSIX sideload enablement, package identity, or a trusted code-signing certificate.
- It keeps the Phase 0/1 promise simple: unzip, run diagnostics, keep 새 메일 자동 확인 disabled until local readiness checks pass.
- It is easier to replace during rapid home-development iterations because a failed build can be deleted without touching Windows app registration state.

MSIX remains a good later option only after the target Windows PC confirms:

- sideloading or enterprise app deployment is allowed;
- the signing certificate chain is trusted by the managed Windows PC;
- package identity, update channel, and install location are acceptable;
- toast notification/app identity benefits are worth the packaging overhead.

Until those are proven, MSIX is a higher-risk packaging path for this project.

## GitHub Actions portable build

The repository includes `.github/workflows/windows-portable.yml`.

It runs on:

- manual `workflow_dispatch`;
- pushes to `main`;
- tags beginning with `v` or `phase`;
- pull requests targeting `main`.

The workflow performs the Windows verification path and uploads:

```text
artifacts/MailWhere-v0.13.0-win-x64-portable.zip
```

The zip contains the published app, the read-only CLI provider, the bundled `skills/mailwhere/**` tree, a portable README, curated operational docs, the README SVG logo, `appsettings.sample.json`, `MailWhere.defaults.sample.json`, and `BUILD-MANIFEST.json`. Historical plans, reviews, screenshots, `.omx` runtime files, and unused logo variants stay in the repository rather than the package.

Before compression, the publish scripts update only the published `MailWhere.exe` modified time. This makes the executable easier to find when users extract the portable folder and sort by recent modified time. No helper touch script is copied into the release payload.

## Read-only CLI provider in portable releases

Starting with v0.10.0, portable releases include:

```text
MailWhere.exe
MailWhere.Cli.exe
```

`MailWhere.Cli.exe` is for local automation such as contextWhere and Codex. It reads the existing MailWhere SQLite database in read-only mode and emits JSON only:

```powershell
.\MailWhere.Cli.exe health --json
.\MailWhere.Cli.exe manifest --json
.\MailWhere.Cli.exe export --json [--db PATH] [--archived-limit N]
.\MailWhere.Cli.exe list-tasks --json [--status open|archived|all] [--due-window today|overdue|7d|30d|none|all] [--limit N] [--db PATH]
.\MailWhere.Cli.exe list-review-candidates --json [--limit N] [--db PATH]
```

Safety boundaries:

- CLI references only `MailWhere.Core` and `MailWhere.Storage`.
- CLI does not load Outlook COM, WPF, or the tray app.
- CLI read commands do not initialize schema and do not create the database.
- Missing DB returns JSON error code `database-not-found` with exit code `2`.
- Exported JSON omits raw body, source id/hash, evidence snippet, full recipient lists, prompt logs, and API keys.
- CLI/skill/exported normal outputs may include `open_source_token` for explicit source-open, but they do not expose StoreID, EntryID, source id, or source hash.

Explicit source-open for automation goes through the Windows app boundary:

```powershell
.\MailWhere.exe --open-source-token <token>
```

The token is opaque and must be resolved locally by MailWhere. This Windows-only command is the only supported Skill launch path for original-open and must be smoke-tested with Classic Outlook on the target PC. Internal WPF/Outlook adapter code may keep raw locator values inside the trusted local process/database boundary, but the portable CLI/skill/export surfaces must never emit them.

## Bundled MailWhere skill

Starting with v0.13.0, portable releases include a static read-only skill bundle:

```text
skills/mailwhere/SKILL.md
skills/mailwhere/references/...
skills/mailwhere/manifest.json
```

Install/repair targets:

- Codex: `%USERPROFILE%\.agents\skills\mailwhere`
- Claude: `%USERPROFILE%\.claude\skills\mailwhere`

Conflict policy is fixed and intentionally simple:

- **Yes** overwrites the installed bundled content without backup.
- **No** preserves the current folder and opens it.

Do not merge old installed skill files with the release bundle. See [`SKILL_INSTALL.md`](SKILL_INSTALL.md).

## Team default settings seed

If a small team should start with the same approved local LLM endpoint/model, copy `MailWhere.defaults.sample.json` to `MailWhere.defaults.json` in the same folder as `MailWhere.exe` and edit only non-secret defaults such as:

- `ExternalLlmEnabled`
- `LlmProvider`
- `LlmEndpoint`
- `LlmModel`
- `LlmTimeoutSeconds`
- `LlmFallbackPolicy`
- `LlmThinkingControl`
- `LlmStructuredOutputMode`
- `LlmTemperature`
- `LlmMaxOutputTokens`
- `LlmMaxBatchSize`
- `RecentScanDays`
- `AutomaticScanIntervalMinutes`

On first run, if the user's `%LOCALAPPDATA%\\MailWhere\\runtime-settings.json` does not exist, MailWhere reads the seed file and saves it as the user setting. Do not put API keys, personal tokens, or mailbox-specific data in the seed file.

## Local Windows portable build

On a Windows machine with the .NET 10 SDK:

```powershell
cd MailWhere
.\scripts\publish-portable.ps1
```

Optional parameters:

```powershell
.\scripts\publish-portable.ps1 -Configuration Release -RuntimeIdentifier win-x64
.\scripts\publish-portable.ps1 -SkipTests
```

`-SkipTests` is only for local packaging experiments. Release artifacts should run the full default verification.

## Managed-PC smoke process

1. Download the portable artifact from GitHub Actions.
2. Unzip to a user-owned folder, for example `%USERPROFILE%\Apps\MailWhere`.
3. Start `MailWhere.exe`.
4. Run diagnostics first.
5. Keep 새 메일 자동 확인 disabled until `docs/MANAGED_PC_SMOKE_TEST.md` passes. Direct launch opens the board; Windows startup uses the tray-only `--tray` command.
6. Do not enable external LLM providers unless approved policy explicitly allows it.

## Artifact safety boundaries

Do not commit or package:

- local SQLite databases;
- Outlook mailbox exports;
- runtime readiness approval files from a managed Windows PC;
- `.omx` runtime files;
- API keys, endpoint credentials, or prompt logs containing mail bodies.

The portable artifact is allowed to include documentation and sample config only.

## Future MSIX track

Add MSIX only as a separate release lane after policy checks pass. The expected follow-up work is:

1. add a Windows Application Packaging Project or equivalent MSIX packaging step;
2. define package identity and app manifest capabilities;
3. configure signing with a trusted certificate stored in GitHub Actions secrets or an internal build system;
4. test install/update/uninstall on a non-production managed Windows PC;
5. keep the portable workflow as a fallback artifact.
