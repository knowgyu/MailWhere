[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [switch]$IncludeSettings
)

$ErrorActionPreference = "Stop"

$root = Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "MailWhere"
$database = Join-Path $root "followups.sqlite"
$targets = @(
    $database,
    "$database-wal",
    "$database-shm"
)

if ($IncludeSettings) {
    $targets += Join-Path $root "runtime-settings.json"
}

Write-Host "[mailwhere] Local data folder: $root"
foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target)) {
        Write-Host "[mailwhere] skip missing $target"
        continue
    }

    if ($PSCmdlet.ShouldProcess($target, "Delete MailWhere local data file")) {
        Remove-Item -LiteralPath $target -Force
        Write-Host "[mailwhere] deleted $target"
    }
}

Write-Host "[mailwhere] OK. Restart MailWhere or run a fresh scan."
