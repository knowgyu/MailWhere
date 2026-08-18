#!/usr/bin/env bash
set -euo pipefail

configuration="${CONFIGURATION:-Release}"
runtime_identifier="${RUNTIME_IDENTIFIER:-win-x64}"
output_root="${OUTPUT_ROOT:-artifacts}"
skip_tests="${SKIP_TESTS:-0}"
dotnet_cmd="${DOTNET:-dotnet}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

version="$("$dotnet_cmd" msbuild ./src/MailWhere.Windows/MailWhere.Windows.csproj -nologo -getProperty:Version | tr -d '\r' | xargs)"
if [[ -z "$version" ]]; then
  version="unknown"
fi

if [[ "$version" == v* ]]; then
  version_label="$version"
else
  version_label="v$version"
fi

artifact_root="$repo_root/$output_root"
publish_root="$artifact_root/publish"
app_name="MailWhere-$version_label-$runtime_identifier"
publish_dir="$publish_root/$app_name"
zip_path="$artifact_root/$app_name-portable.zip"

echo "[portable] dotnet info"
"$dotnet_cmd" --info

echo "[portable] restore"
"$dotnet_cmd" restore ./MailWhere.sln

echo "[portable] build $configuration"
"$dotnet_cmd" build ./MailWhere.sln -c "$configuration" --no-restore

if [[ "$skip_tests" != "1" && "$skip_tests" != "true" ]]; then
  echo "[portable] core tests"
  "$dotnet_cmd" run --project ./tests/MailWhere.Tests/MailWhere.Tests.csproj -c "$configuration" --no-build

fi

echo "[portable] clean artifact folders"
rm -rf "$publish_dir" "$zip_path"
mkdir -p "$publish_dir" "$artifact_root"

echo "[portable] publish self-contained folder"
"$dotnet_cmd" publish ./src/MailWhere.Windows/MailWhere.Windows.csproj \
  -c "$configuration" \
  -r "$runtime_identifier" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=false \
  -o "$publish_dir"

echo "[portable] publish CLI provider into portable folder"
"$dotnet_cmd" publish ./src/MailWhere.Cli/MailWhere.Cli.csproj \
  -c "$configuration" \
  -r "$runtime_identifier" \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishReadyToRun=false \
  -o "$publish_dir"

echo "[portable] copy operator docs"
cp ./docs/PORTABLE_README.md "$publish_dir/README.md"
operator_docs=(
  ARCHITECTURE.md
  BASELINE_METRICS.md
  CAPABILITY_PROBES.md
  DEPLOYMENT.md
  FAILURE_MODES.md
  LLM_ENDPOINTS.md
  MANAGED_PC_SMOKE_TEST.md
  PRODUCTION_READINESS.md
  SECURITY.md
  "releases/$version_label.md"
)
for relative_path in "${operator_docs[@]}"; do
  mkdir -p "$publish_dir/docs/$(dirname "$relative_path")"
  cp "./docs/$relative_path" "$publish_dir/docs/$relative_path"
done
cp ./docs/START_HERE.ko.txt "$publish_dir/START_HERE_시작하기.txt"
mkdir -p "$publish_dir/assets"
cp ./assets/app-icon.svg "$publish_dir/assets/app-icon.svg"
cp ./src/MailWhere.Windows/appsettings.sample.json "$publish_dir/appsettings.sample.json"
cp ./src/MailWhere.Windows/MailWhere.defaults.sample.json "$publish_dir/MailWhere.defaults.sample.json"

echo "[portable] copy bundled skill"
mkdir -p "$publish_dir/skills"
cp -R ./skills/mailwhere "$publish_dir/skills/mailwhere"

required_package_files=(
  README.md
  START_HERE_시작하기.txt
  assets/app-icon.svg
  docs/MANAGED_PC_SMOKE_TEST.md
  "docs/releases/$version_label.md"
  skills/mailwhere/SKILL.md
  skills/mailwhere/manifest.json
)
for relative_path in "${required_package_files[@]}"; do
  test -f "$publish_dir/$relative_path" || {
    echo "Portable package missing required file: $relative_path" >&2
    exit 1
  }
done
for relative_path in \
  docs/history \
  docs/PROJECT_CONTEXT.md \
  docs/PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md \
  docs/CODE_REVIEW_0_1.md \
  assets/app-icon.png; do
  test ! -e "$publish_dir/$relative_path" || {
    echo "Portable package contains internal or redundant file: $relative_path" >&2
    exit 1
  }
done

commit="unknown"
if git rev-parse --short HEAD >/tmp/mailwhere-commit.txt 2>/dev/null; then
  commit="$(cat /tmp/mailwhere-commit.txt)"
fi

python3 - "$publish_dir/BUILD-MANIFEST.json" "$version" "$app_name-portable.zip" "$configuration" "$runtime_identifier" "$commit" <<'PY'
import datetime
import json
import sys

manifest_path, version, package, configuration, rid, commit = sys.argv[1:]
manifest = {
    "name": "MailWhere",
    "version": version,
    "package": package,
    "configuration": configuration,
    "runtimeIdentifier": rid,
    "commit": commit,
    "builtAtUtc": datetime.datetime.now(datetime.UTC).isoformat(),
    "installMode": "portable-self-contained",
    "cliExecutable": "MailWhere.Cli.exe",
    "cliContractVersion": "v1",
    "cliCommands": [
        "health --json",
        "manifest --json",
        "export --json [--db PATH] [--archived-limit N]",
        "list-tasks --json [--status open|archived|all] [--due-window today|overdue|7d|30d|none|all] [--limit N] [--db PATH]",
        "list-review-candidates --json [--limit N] [--db PATH]",
        "search-mail --json --query TEXT [--folder inbox|sent|all] [--sender-recipient TEXT] [--conversation ID] [--limit N] [--db PATH]",
    "MailWhere.exe --open-source-token TOKEN",
    ],
    "bundledSkill": {
        "source": "skills/mailwhere",
        "codexTarget": "%USERPROFILE%\\.agents\\skills\\mailwhere",
        "claudeTarget": "%USERPROFILE%\\.claude\\skills\\mailwhere",
        "conflictPolicy": "Yes overwrites without backup; No preserves and opens folder"
    },
    "safetyDefaults": [
        "Phase 0/1 Outlook access is read-only",
        "MailWhere.Cli is read-only and does not load Outlook COM",
        "External LLM providers are disabled by default",
        "Managed automation requires diagnostics and smoke-test approval",
    ],
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY

exe_path="$publish_dir/MailWhere.exe"
if [[ -f "$exe_path" ]]; then
  echo "[portable] touch MailWhere.exe for recent-sort discoverability"
  python3 - "$exe_path" <<'PY'
import os
import sys
import time

exe_path = sys.argv[1]
# Zip timestamps have coarse granularity, and copied docs/assets can share
# the same second. Nudge only the executable so recent-modified sorting
# reliably surfaces it without shipping any helper script.
stamp = time.time() + 10
os.utime(exe_path, (stamp, stamp))
PY
fi

echo "[portable] create zip $zip_path"
python3 - "$publish_dir" "$zip_path" <<'PY'
import pathlib
import sys
import zipfile

root = pathlib.Path(sys.argv[1])
zip_path = pathlib.Path(sys.argv[2])
with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob("*")):
        if path.is_file():
            archive.write(path, path.relative_to(root))
PY

echo "[portable] OK: $zip_path"
