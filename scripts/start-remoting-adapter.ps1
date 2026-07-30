# 启动 Remoting 兼容适配器（老 HMI/IOMAP/Alarm 客户端过渡接入）
# 默认使用本机固定二进制管道承载订阅/读写；REST 仅保留管理面和显式应急回退。
# 用法: .\start-remoting-adapter.ps1 [-Port 8000] [-Api http://localhost:8090] [-Transport pipe|rest] [-Release]
param(
    [int]$Port = 8000,
    [string]$Api = "http://localhost:8090",
    [ValidateSet("pipe", "rest")]
    [string]$Transport = "pipe",
    [string]$RequestPipe = "RWVDCS.default.Realtime.Request.v1",
    [string]$EventPipe = "RWVDCS.default.Realtime.Events.v1",
    [int]$RequestTimeoutMs = 3000,
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
if ($Transport -eq "pipe") {
    Write-Host "实时通道: 本机二进制管道 ($RequestPipe / $EventPipe)" -ForegroundColor Cyan
} else {
    Write-Host "实时通道: REST 应急模式（存在 JSON/轮询开销）" -ForegroundColor Yellow
}
& $exe --port $Port --api $Api --transport $Transport `
    --request-pipe $RequestPipe --event-pipe $EventPipe `
    --request-timeout-ms $RequestTimeoutMs --poll $PollMs
