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

Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host "[windows] dotnet info"
Invoke-Native { dotnet --info }

Write-Host "[windows] restore"
Invoke-Native { dotnet restore .\MailWhere.sln }

Write-Host "[windows] build release"
Invoke-Native { dotnet build .\MailWhere.sln -c Release --no-restore }

Write-Host "[windows] core tests"
Invoke-Native { dotnet run --project .\tests\MailWhere.Tests\MailWhere.Tests.csproj -c Release --no-build }

Write-Host "[windows] test harness"
Invoke-Native { dotnet run --project .\tests\MailWhere.TestHarness\MailWhere.TestHarness.csproj -c Release --no-build }

Write-Host "[windows] forbidden Outlook mutation scan"
$forbidden = Select-String -Path .\src\MailWhere.OutlookCom\*.cs, .\src\MailWhere.Windows\*.cs -Pattern '\.(Send|Delete|Move|Save|Reply|ReplyAll|Forward)\s*\(|\bUnRead\s*=|\bCategories\s*=|\bFlagStatus\s*=|\bSaveAsFile\s*\(' |
    Where-Object { $_.Line -notmatch 'WindowsRuntimeSettingsStore\.Save\s*\(' }
if ($forbidden) {
    $forbidden | Format-List
    throw "Forbidden Outlook mutation/display/attachment call found."
}

$display = Select-String -Path .\src\MailWhere.OutlookCom\*.cs -Pattern '\bDisplay\s*\('
$unexpectedDisplay = $display | Where-Object { $_.Path -notlike '*OutlookComMailOpener.cs' }
if ($unexpectedDisplay) {
    $unexpectedDisplay | Format-List
    throw "Unexpected Outlook Display call found outside audited read-only opener."
}

Write-Host "[windows] OK"
