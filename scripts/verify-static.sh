#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "[static] Checking required project files"
required=(
  MailWhere.sln
  docs/ASSUMPTIONS.md
  docs/CAPABILITY_PROBES.md
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

echo "[static] Checking diagnostics privacy language"
grep -RIn "Diagnostics" docs src/MailWhere.Windows >/dev/null
grep -RIn "Raw mail body is transient" docs/SECURITY.md >/dev/null

echo "[static] dotnet availability"
if command -v dotnet >/dev/null 2>&1; then
  dotnet --info | sed -n '1,20p'
else
  echo "dotnet not installed; Windows/.NET build is a documented verification gap"
fi

echo "[static] OK"
