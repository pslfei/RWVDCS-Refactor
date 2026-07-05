param(
    [Parameter(Mandatory = $true)][string]$Dir
)

# Remove [StructLayout(...)] lines from ported block sources.
# The new system stores block state via BlockStateSchema (reflection layout),
# not CLR sequential layout; partial class + StructLayout triggers
# TypeLoadException ("format is invalid") on .NET 10.
# NOTE: keep this file ASCII-only (PowerShell 5.1 reads BOM-less files as ANSI).
$files = Get-ChildItem -Path $Dir -Filter *.cs -Recurse
$stripped = 0
foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName)
    $kept = @($lines | Where-Object { $_ -notmatch '^\s*\[\s*StructLayout\s*\(' })
    if ($kept.Count -ne $lines.Count) {
        [System.IO.File]::WriteAllLines($f.FullName, $kept, (New-Object System.Text.UTF8Encoding($true)))
        $stripped++
    }
}
Write-Host "stripped StructLayout lines in $stripped of $($files.Count) files"
