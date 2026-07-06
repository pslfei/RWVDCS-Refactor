# 注册/卸载 RWVDCS Web 管理台的开机自启（任务计划程序，SYSTEM 账户）
# 用法（管理员 PowerShell）:
#   .\install-service.ps1 -Port 8090 -DataDir D:\rwvdcs\data [-Mdb <工程.mdb>] [-AutoStart] [-ExePath <rwvdcs.exe>]
#   .\install-service.ps1 -Uninstall
param(
    [int]$Port = 8090,
    [string]$DataDir = "",
    [string]$Mdb = "",
    [string]$ExePath = "",
    [switch]$AutoStart,    # 装载后自动开始连续运行（需 -Mdb）
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$taskName = "RWVDCS-Web"

if ($Uninstall) {
    schtasks /End /TN $taskName 2>$null | Out-Null
    schtasks /Delete /TN $taskName /F
    Write-Host "已卸载计划任务 $taskName" -ForegroundColor Green
    return
}

if (-not $ExePath) {
    $root = Split-Path $PSScriptRoot -Parent
    $ExePath = Join-Path $root "src\Host\RWVDCS.Host\bin\Release\net10.0-windows\rwvdcs.exe"
}
if (-not (Test-Path $ExePath)) { throw "找不到 $ExePath（先 dotnet build -c Release 或用 -ExePath 指定）" }

$cmdArgs = @()
if ($Mdb) { $cmdArgs += "`"$Mdb`"" }
$cmdArgs += @("--web", "$Port")
if ($DataDir) { $cmdArgs += @("--data", "`"$DataDir`"") }
if ($AutoStart) { $cmdArgs += "--start" }
$tr = "`"$ExePath`" $($cmdArgs -join ' ')"

# 开机触发 + SYSTEM 账户 + 失败每分钟重试
schtasks /Create /TN $taskName /TR $tr /SC ONSTART /RU SYSTEM /RL HIGHEST /F
if ($LASTEXITCODE -ne 0) { throw "注册失败（需要管理员权限）" }

Write-Host "已注册计划任务 $taskName（开机自启）" -ForegroundColor Green
Write-Host "  命令: $tr"
Write-Host "  立即启动: schtasks /Run /TN $taskName"
Write-Host "  查看状态: schtasks /Query /TN $taskName /V /FO LIST"
