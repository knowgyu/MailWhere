[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputRoot = "artifacts",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Native command failed with exit code ${exitCode}: $Command"
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$version = "unknown"
try {
    $version = (& dotnet msbuild .\src\MailWhere.Windows\MailWhere.Windows.csproj -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet msbuild -getProperty:Version failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Warning "Could not read project version for artifact name: $($_.Exception.Message)"
}

$versionLabel = if ($version -and $version -ne "unknown" -and $version.StartsWith("v")) { $version } elseif ($version -and $version -ne "unknown") { "v$version" } else { "vunknown" }
$artifactRoot = Join-Path $repoRoot $OutputRoot
$publishRoot = Join-Path $artifactRoot "publish"
$appName = "MailWhere-$versionLabel-$RuntimeIdentifier"
$publishDir = Join-Path $publishRoot $appName
$zipPath = Join-Path $artifactRoot "$appName-portable.zip"

Write-Host "[portable] dotnet info"
Invoke-Native { dotnet --info }

Write-Host "[portable] restore"
Invoke-Native { dotnet restore .\MailWhere.sln }

Write-Host "[portable] build $Configuration"
Invoke-Native { dotnet build .\MailWhere.sln -c $Configuration --no-restore }

if (-not $SkipTests) {
    Write-Host "[portable] core tests"
    Invoke-Native { dotnet run --project .\tests\MailWhere.Tests\MailWhere.Tests.csproj -c $Configuration --no-build }

}

Write-Host "[portable] clean artifact folders"
Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "[portable] publish self-contained folder"
Invoke-Native {
    dotnet publish .\src\MailWhere.Windows\MailWhere.Windows.csproj `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -o $publishDir
}

Write-Host "[portable] publish CLI provider into portable folder"
Invoke-Native {
    dotnet publish .\src\MailWhere.Cli\MailWhere.Cli.csproj `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishReadyToRun=false `
        -o $publishDir
}

Write-Host "[portable] copy operator docs"
Copy-Item .\docs\PORTABLE_README.md (Join-Path $publishDir "README.md") -Force
$operatorDocs = @(
    "ARCHITECTURE.md",
    "BASELINE_METRICS.md",
    "CAPABILITY_PROBES.md",
    "DEPLOYMENT.md",
    "FAILURE_MODES.md",
    "LLM_ENDPOINTS.md",
    "MANAGED_PC_SMOKE_TEST.md",
    "PRODUCTION_READINESS.md",
    "SECURITY.md",
    "releases\$($versionLabel).md"
)
foreach ($relativePath in $operatorDocs) {
    $source = Join-Path ".\docs" $relativePath
    $target = Join-Path (Join-Path $publishDir "docs") $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item $source $target -Force
}
Copy-Item .\docs\START_HERE.ko.txt (Join-Path $publishDir "START_HERE_시작하기.txt") -Force
New-Item -ItemType Directory -Force -Path (Join-Path $publishDir "assets") | Out-Null
Copy-Item .\assets\app-icon.svg (Join-Path $publishDir "assets\app-icon.svg") -Force
Copy-Item .\src\MailWhere.Windows\appsettings.sample.json (Join-Path $publishDir "appsettings.sample.json") -Force
Copy-Item .\src\MailWhere.Windows\MailWhere.defaults.sample.json (Join-Path $publishDir "MailWhere.defaults.sample.json") -Force

foreach ($relativePath in @(
    "README.md",
    "START_HERE_시작하기.txt",
    "assets\app-icon.svg",
    "docs\MANAGED_PC_SMOKE_TEST.md",
    "docs\releases\$($versionLabel).md"
)) {
    if (-not (Test-Path (Join-Path $publishDir $relativePath))) {
        throw "Portable package missing required file: $relativePath"
    }
}
foreach ($relativePath in @(
    "docs\history",
    "docs\PROJECT_CONTEXT.md",
    "docs\PRODUCT_ARCHITECTURE_AND_AGENT_CLI.md",
    "docs\CODE_REVIEW_0_1.md",
    "assets\app-icon.png"
)) {
    if (Test-Path (Join-Path $publishDir $relativePath)) {
        throw "Portable package contains internal or redundant file: $relativePath"
    }
}

$commit = "unknown"
try {
    $commit = (& git rev-parse --short HEAD).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "git rev-parse failed with exit code $LASTEXITCODE"
    }
} catch {
    Write-Warning "Could not read git commit for build manifest: $($_.Exception.Message)"
}

$manifest = [ordered]@{
    name = "MailWhere"
    version = $version
    package = "$appName-portable.zip"
    configuration = $Configuration
    runtimeIdentifier = $RuntimeIdentifier
    commit = $commit
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    installMode = "portable-self-contained"
    cliExecutable = "MailWhere.Cli.exe"
    cliContractVersion = "v1"
    cliCommands = @(
        "health --json",
        "manifest --json",
        "export --json [--db PATH] [--archived-limit N]",
        "list-tasks --json [--status open|archived|all] [--due-window today|overdue|7d|30d|none|all] [--limit N] [--db PATH]",
        "list-review-candidates --json [--limit N] [--db PATH]",
        "search-mail --json --query TEXT [--folder inbox|sent|all] [--sender-recipient TEXT] [--conversation ID] [--limit N] [--db PATH]"
    )
    safetyDefaults = @(
        "Phase 0/1 Outlook access is read-only",
        "MailWhere.Cli is read-only and does not load Outlook COM",
        "External LLM providers are disabled by default",
        "Managed automation requires diagnostics and smoke-test approval"
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $publishDir "BUILD-MANIFEST.json") -Encoding UTF8

$exePath = Join-Path $publishDir "MailWhere.exe"
if (Test-Path $exePath) {
    Write-Host "[portable] touch MailWhere.exe for recent-sort discoverability"
    # Zip timestamps have coarse granularity, and copied docs/assets can share
    # the same second. Nudge only the executable so recent-modified sorting
    # reliably surfaces it without shipping any helper script.
    (Get-Item $exePath).LastWriteTime = (Get-Date).AddSeconds(10)
}

Write-Host "[portable] create zip $zipPath"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Host "[portable] OK: $zipPath"
