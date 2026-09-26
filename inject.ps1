# EasiCamera 自动注入脚本
# 用法：
#   .\inject.ps1              自动查找 EasiCamera.exe 并注入
#   .\inject.ps1 -Pid 1234    指定 PID
#   .\inject.ps1 -Wait        启动 EasiCamera 后等待 3 秒再注入
param(
    [int]$Pid,
    [switch]$Wait
)

$ErrorActionPreference = "Stop"
$build = Join-Path $PSScriptRoot "build"
$injector = Join-Path $build "Injector.exe"
$bootstrap = Join-Path $build "Bootstrap.dll"
$patchDll = Join-Path $build "EasiCameraPatch.dll"
$harmonyDll = Join-Path $build "0Harmony.dll"
$bLog = Join-Path $build "bootstrap.log"
$pLog = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "EasiCameraPatch.log"

foreach ($f in @($injector, $bootstrap, $patchDll, $harmonyDll)) {
    if (-not (Test-Path $f)) {
        Write-Host "[!] 缺少文件: $f" -ForegroundColor Red
        Write-Host "    请先运行 build.ps1 编译" -ForegroundColor Yellow
        exit 1
    }
}

# 清理旧日志
if (Test-Path $bLog) { Remove-Item $bLog -Force }

$targetPid = $Pid
if (-not $targetPid) {
    $proc = Get-Process -Name "EasiCamera" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) {
        Write-Host "[!] EasiCamera.exe 未运行" -ForegroundColor Red
        $ans = Read-Host "是否现在启动 EasiCamera？(y/N)"
        if ($ans -eq 'y' -or $ans -eq 'Y') {
            $exe = "C:\Program Files (x86)\Seewo\EasiCamera\EasiCamera_2.1.0.4392\Main\EasiCamera.exe"
            if (Test-Path $exe) {
                Start-Process $exe
                Write-Host "[*] 等待 EasiCamera 启动..." -ForegroundColor Cyan
                Start-Sleep -Seconds 4
                $proc = Get-Process -Name "EasiCamera" -ErrorAction SilentlyContinue | Select-Object -First 1
            } else {
                Write-Host "[!] 未找到 EasiCamera.exe: $exe" -ForegroundColor Red
                exit 2
            }
        } else {
            exit 2
        }
    }
    $targetPid = $proc.Id
}

Write-Host "[*] 目标进程 PID: $targetPid" -ForegroundColor Cyan
if ($Wait) {
    Write-Host "[*] 等待 3 秒确保 CLR 加载完成..." -ForegroundColor Cyan
    Start-Sleep -Seconds 3
}

Write-Host "[*] 执行注入..." -ForegroundColor Cyan
& $injector $targetPid $bootstrap
$code = $LASTEXITCODE
if ($code -ne 0) {
    Write-Host "[!] 注入失败 (exit=$code)" -ForegroundColor Red
    exit $code
}

# 等待 patch 完成
Write-Host "[*] 等待补丁生效..." -ForegroundColor Cyan
Start-Sleep -Seconds 2

# 检查日志
if (Test-Path $bLog) {
    Write-Host "`n--- bootstrap.log ---" -ForegroundColor DarkGray
    Get-Content $bLog -Encoding Unicode | ForEach-Object { Write-Host $_ }
}
if (Test-Path $pLog) {
    Write-Host "`n--- EasiCameraPatch.log ---" -ForegroundColor DarkGray
    Get-Content $pLog | Select-Object -Last 5 | ForEach-Object { Write-Host $_ }
}

Write-Host "`n[+] 完成。现在在 EasiCamera 中重新触发摄像头枚举（拔插 USB 相机或切换设备）即可看到非希沃相机。" -ForegroundColor Green
