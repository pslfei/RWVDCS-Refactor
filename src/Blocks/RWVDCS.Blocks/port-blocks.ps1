# 把老系统 Function\RW 下的 106 个功能块（212 个文件）移植到本项目 RW\ 目录。
# 转换规则（尽量保持源码原样，便于与老系统 diff 对账）：
#   1. GBK → UTF-8 (BOM)
#   2. namespace FunctionCode → namespace RWVDCS.Blocks.RW
#   3. using DCSCommon; → using RWVDCS.Core.Blocks; + using RWVDCS.Core.Types;
#   4. using DCSType;  → 删除（类型已并入 RWVDCS.Core.Types）
# 用法：在本目录执行 .\port-blocks.ps1
param(
    [string]$SourceDir = "D:\项目\睿渥\RWVDCS\Function\RW",
    [string]$TargetDir = "$PSScriptRoot\RW"
)

$ErrorActionPreference = "Stop"
$gbk = [System.Text.Encoding]::GetEncoding(936)
$utf8bom = New-Object System.Text.UTF8Encoding($true)

if (Test-Path $TargetDir) { Remove-Item $TargetDir -Recurse -Force }
New-Item -ItemType Directory -Path $TargetDir | Out-Null

$count = 0
foreach ($f in Get-ChildItem $SourceDir -Filter *.cs) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    # 编码探测：优先严格 UTF-8，失败则按 GBK 读
    $text = $null
    try {
        $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
        $text = $strictUtf8.GetString($bytes)
    } catch {
        $text = $gbk.GetString($bytes)
    }

    $text = $text -replace 'namespace\s+FunctionCode', 'namespace RWVDCS.Blocks.RW'
    $text = $text -replace 'using\s+DCSCommon\s*;', "using RWVDCS.Core.Blocks;`r`nusing RWVDCS.Core.Types;"
    $text = $text -replace 'using\s+DCSType\s*;\r?\n', ''

    $target = Join-Path $TargetDir $f.Name
    [System.IO.File]::WriteAllText($target, $text, $utf8bom)
    $count++
}
Write-Host "已移植 $count 个文件到 $TargetDir"
