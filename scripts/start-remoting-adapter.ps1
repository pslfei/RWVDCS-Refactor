# 启动 Remoting 兼容适配器（老 HMI/IOMAP/Alarm 客户端过渡接入）
# 用法: .\start-remoting-adapter.ps1 [-Port 8000] [-Api http://localhost:8090] [-PollMs 200] [-Release]
param(
    [int]$Port = 8000,
    [string]$Api = "http://localhost:8090",
    [int]$PollMs = 200,
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$cfg = if ($Release) { "Release" } else { "Debug" }
$exe = Join-Path $root "src\Compat\RWVDCS.RemotingAdapter\bin\$cfg\net48\rwvdcs-remoting-adapter.exe"

if (-not (Test-Path $exe)) {
    Write-Host "未找到 $exe，先构建..." -ForegroundColor Yellow
    dotnet build (Join-Path $root "src\Compat\RWVDCS.RemotingAdapter\RWVDCS.RemotingAdapter.csproj") -c $cfg --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
}

# 先确认新系统 API 可达
try {
    $null = Invoke-RestMethod "$Api/api/status" -TimeoutSec 5 -NoProxy
    Write-Host "新系统 API 可达: $Api" -ForegroundColor Green
} catch {
    Write-Host "警告: $Api 不可达，请先用 start-web.ps1 启动主宿主" -ForegroundColor Yellow
}

Write-Host "老客户端连接地址: tcp://<本机>:$Port/Communication" -ForegroundColor Cyan
& $exe --port $Port --api $Api --poll $PollMs
