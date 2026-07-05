param(
    # Project mdb the wrk was saved against (must match, same as legacy behavior)
    [Parameter(Mandatory = $true)][string]$Mdb,
    # Legacy .wrk file to migrate
    [Parameter(Mandatory = $true)][string]$Wrk,
    # Staged legacy run dir containing LegacyRunner.exe + old DLLs (see stage-legacy.ps1)
    [Parameter(Mandatory = $true)][string]$LegacyDir,
    # Repo root of RWVDCS.Next (used to invoke the new Host via dotnet run)
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    # Output directory for the new-format snapshot
    [Parameter(Mandatory = $true)][string]$OutDir,
    # Optional: keep the intermediate bridge tsv (default: temp file, deleted on success)
    [string]$BridgeFile = '',
    # Optional: run a c0 point-level parity check between old and new after migration
    [switch]$Verify
)

# Migrate a legacy .wrk snapshot into the new RWVDCS.Next snapshot format:
#   1) LegacyRunner (x86, in-proc old DCS) loads mdb + wrk, exports a name-addressed
#      bridge tsv with every point sub-field and block state field
#   2) New Host imports the bridge and saves the new-format snapshot (manifest + arenas)
#   3) optional -Verify: dump both sides at c0 and compare with ParityCompare
# NOTE: keep this file ASCII-only (PowerShell 5.1 reads BOM-less files as ANSI).
$ErrorActionPreference = 'Stop'

$runner = Join-Path $LegacyDir 'LegacyRunner.exe'
if (-not (Test-Path $runner)) { throw "LegacyRunner.exe not found in $LegacyDir (run stage-legacy.ps1 first)" }
if (-not (Test-Path $Mdb)) { throw "mdb not found: $Mdb" }
if (-not (Test-Path $Wrk)) { throw "wrk not found: $Wrk" }

$keepBridge = $BridgeFile -ne ''
if (-not $keepBridge) { $BridgeFile = Join-Path ([IO.Path]::GetTempPath()) ("wrk-bridge-" + [Guid]::NewGuid().ToString('N') + ".tsv") }
$oldDump = Join-Path ([IO.Path]::GetTempPath()) ("wrk-old-" + [Guid]::NewGuid().ToString('N'))
$newDump = Join-Path ([IO.Path]::GetTempPath()) ("wrk-new-" + [Guid]::NewGuid().ToString('N') + ".tsv")

# ---- 1) legacy: load wrk, export bridge (and c0 dump when verifying)
$legacyArgs = @($Mdb, '--load-wrk', $Wrk, '--export-state', $BridgeFile, '--quiet')
if ($Verify) { $legacyArgs += @('--dump', $oldDump) }
Write-Host "[1/3] legacy export: $runner $($legacyArgs -join ' ')"
Push-Location $LegacyDir
try { & $runner @legacyArgs | Write-Host } finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "LegacyRunner failed: $LASTEXITCODE" }

# ---- 2) new host: import bridge, save new-format snapshot (and c0 dump when verifying)
$hostProj = Join-Path $RepoRoot 'src\Host\RWVDCS.Host'
$hostArgs = @('run', '--project', $hostProj, '-c', 'Release', '--', $Mdb, '--import-legacy', $BridgeFile, '--save', $OutDir)
if ($Verify) { $hostArgs += @('--dump', $newDump) }
Write-Host "[2/3] new import: dotnet $($hostArgs -join ' ')"
& dotnet @hostArgs | Write-Host
if ($LASTEXITCODE -ne 0) { throw "Host import failed: $LASTEXITCODE" }

# ---- 3) optional parity check at c0
if ($Verify) {
    $cmpProj = Join-Path $RepoRoot 'src\Tools\RWVDCS.Tools.ParityCompare'
    Write-Host "[3/3] verify c0 parity"
    & dotnet run --project $cmpProj -c Release -- "$oldDump.c0.tsv" $newDump | Write-Host
    if ($LASTEXITCODE -ne 0) { throw "c0 parity check FAILED - see report above" }
    Remove-Item "$oldDump.c0.tsv", $newDump -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "[3/3] verify skipped (pass -Verify to enable)"
}

if (-not $keepBridge) { Remove-Item $BridgeFile -Force -ErrorAction SilentlyContinue }
Write-Host "done -> $OutDir"
