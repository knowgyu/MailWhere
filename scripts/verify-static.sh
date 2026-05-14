#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

echo "[static] Checking required project files"
required=(
  OutlookAiSecretary.sln
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
  src/OutlookAiSecretary.Core/OutlookAiSecretary.Core.csproj
  src/OutlookAiSecretary.Storage/OutlookAiSecretary.Storage.csproj
  src/OutlookAiSecretary.OutlookCom/OutlookAiSecretary.OutlookCom.csproj
  src/OutlookAiSecretary.Windows/OutlookAiSecretary.Windows.csproj
  tests/OutlookAiSecretary.Tests/Program.cs
)
for f in "${required[@]}"; do
  test -f "$f" || { echo "missing $f" >&2; exit 1; }
done

echo "[static] Checking Phase 0/1 Outlook adapter forbidden mutation calls"
if grep -RInE '\.(Send|Delete|Move|Save|Reply|ReplyAll|Forward)\s*\(|\bUnRead\s*=|\bCategories\s*=|\bFlagStatus\s*=|\bSaveAsFile\s*\(|\bDisplay\s*\(' src/OutlookAiSecretary.OutlookCom; then
  echo "Forbidden Outlook mutation/display/attachment call found" >&2
  exit 1
fi

if grep -RInE '\.(Send|Delete|Move|Reply|ReplyAll|Forward)\s*\(|\bUnRead\s*=|\bCategories\s*=|\bFlagStatus\s*=|\bSaveAsFile\s*\(' src/OutlookAiSecretary.Windows; then
  echo "Forbidden Outlook mutation/display/attachment call found" >&2
  exit 1
fi

echo "[static] Checking diagnostics privacy language"
grep -RIn "Diagnostics" docs src/OutlookAiSecretary.Windows >/dev/null
grep -RIn "Raw mail body is transient" docs/SECURITY.md >/dev/null

echo "[static] dotnet availability"
if command -v dotnet >/dev/null 2>&1; then
  dotnet --info | sed -n '1,20p'
else
  echo "dotnet not installed; Windows/.NET build is a documented verification gap"
fi

echo "[static] OK"
