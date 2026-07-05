param(
    [Parameter(Mandatory = $true)][string]$LegacyBin,
    [Parameter(Mandatory = $true)][string]$StageDir,
    [Parameter(Mandatory = $true)][string]$RunnerBin
)

# Stage the legacy parity run directory:
#   1) mirror the old Simulator output dir (Plug\, NHibernate/JetDriver deps) - read-only copy
#   2) overlay LegacyRunner.exe / .config
# NOTE: keep this file ASCII-only (PowerShell 5.1 reads BOM-less files as ANSI).
$ErrorActionPreference = 'Stop'

robocopy $LegacyBin $StageDir /E /XO /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed: $LASTEXITCODE" }

Copy-Item (Join-Path $RunnerBin 'LegacyRunner.exe') $StageDir -Force
Copy-Item (Join-Path $RunnerBin 'LegacyRunner.exe.config') $StageDir -Force
Write-Host "staged -> $StageDir"
