# RWVDCS 一键诊断：进程 / API 状态 / DPU 周期 / 日志尾部 / 数据目录 / 环境自检
# 用法: .\diagnose.ps1 [-Url http://localhost:8090] [-DataDir <目录>]
param(
    [string]$Url = "http://localhost:8090",
    [string]$DataDir = ""
)

$ErrorActionPreference = "Continue"
function Section($t) { Write-Host "`n===== $t =====" -ForegroundColor Cyan }

Section "进程"
$procs = Get-Process | Where-Object { $_.ProcessName -like "*rwvdcs*" }
if ($procs) {
    $procs | Select-Object Id, ProcessName,
        @{n = "工作集MB"; e = { [math]::Round($_.WorkingSet64 / 1MB, 1) } },
        @{n = "CPU秒"; e = { [math]::Round($_.CPU, 1) } },
        StartTime | Format-Table -AutoSize
} else { Write-Host "无 rwvdcs 相关进程" -ForegroundColor Yellow }

Section "API 状态（$Url）"
try {
    $s = Invoke-RestMethod "$Url/api/status" -TimeoutSec 5 -NoProxy
    if ($s.project) {
        Write-Host ("工程: {0}  v{1}  指纹 {2}" -f $s.project.mdbPath, $s.project.version, $s.project.fingerprint)
        Write-Host ("规模: {0} DPU / {1} 点 / {2} 块" -f $s.project.dpuCount, $s.project.pointCount, $s.project.commandCount)
    } else { Write-Host "未装载工程" -ForegroundColor Yellow }
    Write-Host ("运行态: {0}" -f $s.run.state)
    Write-Host ("监控: 堆 {0:F0} MB / 工作集 {1:F0} MB / GC暂停 {2}% / 线程 {3}" -f `
        $s.monitor.heapMb, $s.monitor.workingSetMb, $s.monitor.gcPausePct, $s.monitor.threads)
    if ($s.pendingDownload) { Write-Host ("待提交下装计划: {0}" -f $s.pendingDownload.planId) -ForegroundColor Yellow }

    Section "DPU 周期统计（P99 最高 5 个）"
    $dpus = Invoke-RestMethod "$Url/api/runtime/dpus" -TimeoutSec 5 -NoProxy
    $dpus | Where-Object { $_.stats } |
        Sort-Object { $_.stats.p99Ms } -Descending | Select-Object -First 5 |
        ForEach-Object { Write-Host ("{0,-10} 周期{1}s 当前{2:F2}ms P99 {3:F2}ms 超限{4} 扫描{5}" -f `
            $_.name, $_.cycleSeconds, $_.stats.curMs, $_.stats.p99Ms, $_.stats.overruns, $_.stats.count) }

    Section "最近 20 条日志"
    (Invoke-RestMethod "$Url/api/logs?max=20" -TimeoutSec 5 -NoProxy) |
        ForEach-Object { Write-Host ("{0} [{1}] [{2}] {3}" -f $_.time, $_.level, $_.source, $_.message) }
} catch {
    Write-Host "API 不可达: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "端口监听检查:"; netstat -ano | Select-String ":$(([uri]$Url).Port)\s" | Select-Object -First 5
}

Section "数据目录"
if (-not $DataDir) { $DataDir = Join-Path (Split-Path $PSScriptRoot -Parent) "rwvdcs-data" }
if (Test-Path $DataDir) {
    Get-ChildItem $DataDir -Directory | ForEach-Object {
        $mb = (Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object Length -Sum).Sum / 1MB
        Write-Host ("{0,-12} {1,10:F1} MB" -f $_.Name, $mb)
    }
} else { Write-Host "$DataDir 不存在" -ForegroundColor Yellow }

Section "环境自检"
# ACE OLEDB x64 驱动
$ace = Get-ItemProperty "HKLM:\SOFTWARE\Classes\Microsoft.ACE.OLEDB.12.0" -ErrorAction SilentlyContinue
Write-Host ("ACE OLEDB 12.0 驱动: " + $(if ($ace) { "已安装" } else { "未检测到（装载 mdb 会失败）" })) `
    -ForegroundColor $(if ($ace) { "Green" } else { "Red" })
# .NET 10 运行时
$net10 = (dotnet --list-runtimes 2>$null | Select-String "Microsoft.NETCore.App 10\.")
Write-Host (".NET 10 运行时: " + $(if ($net10) { "已安装" } else { "未检测到" })) `
    -ForegroundColor $(if ($net10) { "Green" } else { "Red" })
