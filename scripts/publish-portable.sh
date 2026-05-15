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

  echo "[portable] test harness"
  "$dotnet_cmd" run --project ./tests/MailWhere.TestHarness/MailWhere.TestHarness.csproj -c "$configuration" --no-build
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

echo "[portable] copy operator docs"
cp ./README.md "$publish_dir/README.md"
cp -R ./docs "$publish_dir/docs"
cp ./docs/START_HERE.ko.txt "$publish_dir/START_HERE_시작하기.txt"
cp -R ./assets "$publish_dir/assets"
cp ./src/MailWhere.Windows/appsettings.sample.json "$publish_dir/appsettings.sample.json"
cp ./src/MailWhere.Windows/MailWhere.defaults.sample.json "$publish_dir/MailWhere.defaults.sample.json"

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
    "safetyDefaults": [
        "Phase 0/1 Outlook access is read-only",
        "External LLM providers are disabled by default",
        "Managed automation requires diagnostics and smoke-test approval",
    ],
}
with open(manifest_path, "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY

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
