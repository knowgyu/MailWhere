#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "[static] Checking required project files"
required=(
  MailWhere.sln
  docs/ASSUMPTIONS.md
  docs/CAPABILITY_PROBES.md
  docs/BASELINE_METRICS.md
  docs/SECURITY.md
  docs/UX_AND_INTEGRATION_REVIEW.md
  docs/ROADMAP.md
  docs/MANAGED_PC_SMOKE_TEST.md
  docs/DEPLOYMENT.md
  docs/ADR/0004-portable-first-packaging.md
  .github/workflows/windows-portable.yml
  scripts/publish-portable.ps1
  src/MailWhere.Core/MailWhere.Core.csproj
  src/MailWhere.Storage/MailWhere.Storage.csproj
  src/MailWhere.Cli/MailWhere.Cli.csproj
  src/MailWhere.Cli/Program.cs
  src/MailWhere.Cli/CliApp.cs
  src/MailWhere.OutlookCom/MailWhere.OutlookCom.csproj
  src/MailWhere.Windows/MailWhere.Windows.csproj
  tests/MailWhere.Tests/Program.cs
)
for f in "${required[@]}"; do
  test -f "$f" || { echo "missing $f" >&2; exit 1; }
done

echo "[static] Checking Phase 0/1 Outlook adapter forbidden mutation calls"
if grep -RInE '\.(Send|Delete|Move|Save|Reply|ReplyAll|Forward)\s*\(|\bUnRead\s*=|\bCategories\s*=|\bFlagStatus\s*=|\bSaveAsFile\s*\(' src/MailWhere.OutlookCom; then
  echo "Forbidden Outlook mutation/display/attachment call found" >&2
  exit 1
fi

display_hits=$(grep -RInE '\bDisplay\s*\(' src/MailWhere.OutlookCom || true)
if [[ -n "$display_hits" ]] && ! grep -q 'OutlookComMailOpener.cs' <<<"$display_hits"; then
  echo "$display_hits"
  echo "Unexpected Outlook Display call found outside audited read-only opener" >&2
  exit 1
fi

if grep -RInE '\.(Send|Delete|Move|Reply|ReplyAll|Forward)\s*\(|\bUnRead\s*=|\bCategories\s*=|\bFlagStatus\s*=|\bSaveAsFile\s*\(' src/MailWhere.Windows; then
  echo "Forbidden Outlook mutation/display/attachment call found" >&2
  exit 1
fi

echo "[static] Checking CLI provider dependency and read-only boundaries"
cli_refs=$(grep -RIn '<ProjectReference' src/MailWhere.Cli/MailWhere.Cli.csproj)
grep -q '../MailWhere.Core/MailWhere.Core.csproj' <<<"$cli_refs" || { echo "CLI must reference MailWhere.Core" >&2; exit 1; }
grep -q '../MailWhere.Storage/MailWhere.Storage.csproj' <<<"$cli_refs" || { echo "CLI must reference MailWhere.Storage" >&2; exit 1; }
if grep -RInE 'MailWhere\.Windows|MailWhere\.OutlookCom|Microsoft\.Office\.Interop|UseWPF|UseWindowsForms|EnableWindowsTargeting' src/MailWhere.Cli; then
  echo "CLI must not depend on Windows/WPF/Outlook COM surfaces" >&2
  exit 1
fi
if grep -RInE 'InitializeAsync\s*\(|ReadWriteCreate' src/MailWhere.Cli; then
  echo "CLI read commands must not initialize or create the SQLite database" >&2
  exit 1
fi

echo "[static] Checking production mirror wiring and state keys"
grep -RIn 'new OutlookComMailInventorySource' src/MailWhere.Windows/MainWindow.xaml.cs >/dev/null || { echo "Windows app does not construct OutlookComMailInventorySource" >&2; exit 1; }
grep -RIn 'new SqliteMailMirrorStore' src/MailWhere.Windows/MainWindow.xaml.cs >/dev/null || { echo "Windows app does not construct SqliteMailMirrorStore" >&2; exit 1; }
grep -RIn 'new MailMirrorBackfillService' src/MailWhere.Windows/MainWindow.xaml.cs >/dev/null || { echo "Windows app does not construct MailMirrorBackfillService" >&2; exit 1; }
grep -RIn 'mail-mirror-initial-sync-completed-at' src tests >/dev/null || { echo "missing initial mirror sync state key assertion" >&2; exit 1; }
grep -RIn 'mail-mirror-last-authoritative-reconcile-at' src tests >/dev/null || { echo "missing authoritative mirror reconcile state key assertion" >&2; exit 1; }

echo "[static] Checking diagnostics privacy language"
grep -RIn 'content-free' src/MailWhere.Core src/MailWhere.Windows tests docs >/dev/null
grep -RInE 'Raw mail body.*transient by default.*SQLite task schema' docs/SECURITY.md >/dev/null
grep -RIn 'normalized plain-text mail bodies are retained locally in SQLite/FTS5 for search' docs/SECURITY.md >/dev/null
grep -RIn 'normalized plain-text subject/body metadata' docs/ARCHITECTURE.md >/dev/null

echo "[static] dotnet availability"
if command -v dotnet >/dev/null 2>&1; then
  dotnet --info | sed -n '1,20p'
else
  echo "dotnet not installed; Windows/.NET build is a documented verification gap"
fi

echo "[static] OK"
