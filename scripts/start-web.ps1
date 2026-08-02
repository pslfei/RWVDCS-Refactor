# 启动 RWVDCS Web 管理台
# 用法: .\start-web.ps1 [-Port 8090] [-Mdb <工程.mdb>] [-DataDir <目录>] [-ArenaDir <目录>] [-Start] [-Release] [-NoWindow]
param(
    [int]$Port = 8090,
    [string]$Mdb = "",
    [string]$DataDir = "",
    [string]$ArenaDir = "",
    [switch]$Start,        # 装载后自动开始连续运行（需 -Mdb）
    [switch]$Release,      # 用 Release 构建产物
    [switch]$NoWindow      # 后台运行并把输出重定向到 logs\web-<port>.log
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$cfg = if ($Release) { "Release" } else { "Debug" }
$exe = Join-Path $root "src\Host\RWVDCS.Host\bin\$cfg\net10.0-windows\rwvdcs.exe"

if (-not (Test-Path $exe)) {
    Write-Host "未找到 $exe，先构建..." -ForegroundColor Yellow
    dotnet build (Join-Path $root "src\Host\RWVDCS.Host\RWVDCS.Host.csproj") -c $cfg --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
}

$args = @()
if ($Mdb) { $args += $Mdb }
$args += @("--web", "$Port")
if ($DataDir) { $args += @("--data", $DataDir) }
if ($ArenaDir) { $args += @("--arena", $ArenaDir) }
if ($Start) { $args += "--start" }

Write-Host "启动: rwvdcs.exe $($args -join ' ')" -ForegroundColor Cyan
Write-Host "界面: http://localhost:$Port" -ForegroundColor Green

if ($NoWindow) {
    $logDir = Join-Path $root "logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $p = Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $root -PassThru `
        -RedirectStandardOutput (Join-Path $logDir "web-$Port.log") `
        -RedirectStandardError (Join-Path $logDir "web-$Port.err.log")
    Write-Host "后台 PID: $($p.Id)，日志: logs\web-$Port.log"
} else {
    & $exe @args
}
