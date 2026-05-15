# Test Spec — MailWhere board triage UX

## Static/product checks

- Inspect daily board display strings to ensure confidence and long reasons are not part of primary cards.
- Inspect main window XAML to ensure diagnostics are not a top-level primary action/tab.
- Confirm dev triage policy stays under `.omx/plans/` and is not linked as user-facing release documentation.

## Automated tests

- Add/maintain core tests around LLM prompt policy:
  - prompt contains current/forwarded/quoted policy
  - prompt contains examples/few-shot anchors
  - prompt continues to request JSON-only response
- Add/maintain UI-format helper tests if formatting helpers can be made testable without WPF runtime.

## Build/verification

- `dotnet test MailWhere.sln`
- `./scripts/verify-static.sh`
- If possible in the environment: `pwsh ./scripts/publish-portable.ps1`

## Manual Windows follow-up

- Open app on Windows.
- Confirm main screen does not feel like a diagnostics console.
- Open daily board and verify only key tasks/review count are visually dominant.
- Confirm review details remain accessible via 검토 후보 or original mail open.

