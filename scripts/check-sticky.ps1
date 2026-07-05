# Verify legacy sticky-default hypothesis: emulate InitPointByDatabase over natural SELECT order.
# NOTE: keep this file ASCII-only (PowerShell 5.1 reads BOM-less files as ANSI).
param(
    [Parameter(Mandatory = $true)][string]$Mdb,
    [Parameter(Mandatory = $true)][int]$ControllerId,
    [Parameter(Mandatory = $true)][string[]]$CheckNames
)
$conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$Mdb;Mode=Read;")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Name, DataType, DefaultValue, MaximunScale, MinimumScale FROM Cfg_VarSystem WHERE Prj_Controller_ID=$ControllerId"
$r = $cmd.ExecuteReader()
$fDef = 0.0; $bDef = $false; $maxV = 0.0; $minV = 0.0
$want = @{}
foreach ($n in $CheckNames) { $want[$n] = $true }
$rowIdx = 0
while ($r.Read()) {
    $rowIdx++
    $name = [string]$r["Name"]
    $dt = [string]$r["DataType"]
    $dv = if ($r["DefaultValue"] -is [DBNull]) { "" } else { [string]$r["DefaultValue"] }
    if ($dv -ne "") {
        if ($dt -eq "LA") { $fDef = [float]::Parse($dv, [Globalization.CultureInfo]::InvariantCulture) }
        elseif ($dt -eq "LD") { $bDef = ($dv -eq "1") }
    }
    $mx = if ($r["MaximunScale"] -is [DBNull]) { "" } else { [string]$r["MaximunScale"] }
    if ($mx -ne "") { $maxV = [float]::Parse($mx, [Globalization.CultureInfo]::InvariantCulture) }
    $mn = if ($r["MinimumScale"] -is [DBNull]) { "" } else { [string]$r["MinimumScale"] }
    if ($mn -ne "") { $minV = [float]::Parse($mn, [Globalization.CultureInfo]::InvariantCulture) }
    if ($want.ContainsKey($name)) {
        Write-Host ("row={0} name={1} type={2} rawDefault='{3}' stickyDefault={4} stickyMax={5} stickyMin={6}" -f $rowIdx, $name, $dt, $dv, $fDef, $maxV, $minV)
    }
}
$r.Close(); $conn.Close()
