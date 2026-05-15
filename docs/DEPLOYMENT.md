# Deployment

## Recommendation: portable first, MSIX later

For the first managed-PC smoke tests, use a portable self-contained zip.

Why portable is the default:

- It does not require admin install, MSIX sideload enablement, package identity, or a trusted code-signing certificate.
- It keeps the Phase 0/1 promise simple: unzip, run diagnostics, keep watcher/offline automation disabled until the local smoke gate passes.
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
artifacts/MailWhere-v0.1.4-win-x64-portable.zip
```

The zip contains the published app, README, operator docs, `appsettings.sample.json`, and `BUILD-MANIFEST.json`.

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
5. Keep watcher/automation disabled until `docs/MANAGED_PC_SMOKE_TEST.md` passes.
6. Do not enable external LLM providers unless approved policy explicitly allows it.

## Artifact safety boundaries

Do not commit or package:

- local SQLite databases;
- Outlook mailbox exports;
- runtime smoke-gate approval files from a managed Windows PC;
- API keys, endpoint credentials, or prompt logs containing mail bodies.

The portable artifact is allowed to include documentation and sample config only.

## Future MSIX track

Add MSIX only as a separate release lane after policy checks pass. The expected follow-up work is:

1. add a Windows Application Packaging Project or equivalent MSIX packaging step;
2. define package identity and app manifest capabilities;
3. configure signing with a trusted certificate stored in GitHub Actions secrets or an internal build system;
4. test install/update/uninstall on a non-production managed Windows PC;
5. keep the portable workflow as a fallback artifact.
