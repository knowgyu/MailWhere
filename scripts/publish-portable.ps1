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

$artifactRoot = Join-Path $repoRoot $OutputRoot
$publishRoot = Join-Path $artifactRoot "publish"
$appName = "MailWhere-$RuntimeIdentifier"
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

    Write-Host "[portable] test harness"
    Invoke-Native { dotnet run --project .\tests\MailWhere.TestHarness\MailWhere.TestHarness.csproj -c $Configuration --no-build }
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

Write-Host "[portable] copy operator docs"
Copy-Item .\README.md (Join-Path $publishDir "README.md") -Force
Copy-Item .\docs (Join-Path $publishDir "docs") -Recurse -Force
Copy-Item .\docs\START_HERE.ko.txt (Join-Path $publishDir "START_HERE_시작하기.txt") -Force
Copy-Item .\assets (Join-Path $publishDir "assets") -Recurse -Force
Copy-Item .\src\MailWhere.Windows\appsettings.sample.json (Join-Path $publishDir "appsettings.sample.json") -Force

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
    package = "$appName-portable.zip"
    configuration = $Configuration
    runtimeIdentifier = $RuntimeIdentifier
    commit = $commit
    builtAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    installMode = "portable-self-contained"
    safetyDefaults = @(
        "Phase 0/1 Outlook access is read-only",
        "External LLM providers are disabled by default",
        "Managed automation requires diagnostics and smoke-test approval"
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $publishDir "BUILD-MANIFEST.json") -Encoding UTF8

Write-Host "[portable] create zip $zipPath"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Host "[portable] OK: $zipPath"
