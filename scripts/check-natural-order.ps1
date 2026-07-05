# Check whether natural (no ORDER BY) row order differs from ID order per table.
# NOTE: keep this file ASCII-only (PowerShell 5.1 reads BOM-less files as ANSI).
param(
    [Parameter(Mandatory = $true)][string]$Mdb
)
$tables = @('Prj_Controller', 'Cfg_VarSystem', 'Cld_FCBlock', 'Cld_FCInput', 'Cld_FCOutput', 'Cld_FCParameter', 'Meta_FCMaster', 'Meta_FCDetail')
$conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$Mdb;Mode=Read;")
$conn.Open()
foreach ($t in $tables) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT * FROM [$t]"
    $r = $cmd.ExecuteReader()
    $prev = [long]::MinValue
    $rows = 0; $descents = 0; $firstDescentRow = -1; $firstDescentPrev = 0; $firstDescentCur = 0
    while ($r.Read()) {
        $rows++
        $id = [long]$r["ID"]
        if ($id -lt $prev) {
            $descents++
            if ($firstDescentRow -lt 0) { $firstDescentRow = $rows; $firstDescentPrev = $prev; $firstDescentCur = $id }
        }
        $prev = $id
    }
    $r.Close()
    if ($descents -eq 0) {
        Write-Host ("{0}: rows={1} natural order == ID order" -f $t, $rows)
    } else {
        Write-Host ("{0}: rows={1} DESCENTS={2} first at row {3} (prevID={4} -> curID={5})" -f $t, $rows, $descents, $firstDescentRow, $firstDescentPrev, $firstDescentCur)
    }
}
$conn.Close()
