# EasiCamera brand/AI restriction removal - one-shot patcher (3 targets)
# Auto-relaunches itself as Administrator (writing to Program Files needs UAC)
#
# Usage:  .\apply.ps1            apply all patches
#         .\apply.ps1 -Restore   restore all from .bak
param([switch]$Restore)

# ===== self-elevate =====
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[*] Admin required, launching UAC..." -ForegroundColor Yellow
    $script = $MyInvocation.MyCommand.Path
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
    if ($Restore) { $argList += " -Restore" }
    try { Start-Process powershell.exe -Verb RunAs -ArgumentList $argList } catch { Write-Host "[!] elevation cancelled: $_" -ForegroundColor Red }
    exit 0
}

$ErrorActionPreference = "Stop"
$build   = Join-Path $PSScriptRoot "build"
$patcher = Join-Path $build "Patcher.exe"
$cecil   = Join-Path $build "Mono.Cecil.dll"
$main    = "C:\Program Files (x86)\Seewo\EasiCamera\EasiCamera_2.1.0.4392\Main"
$apiDll  = Join-Path $main "EasiCamera.Api.dll"
$uiDll   = Join-Path $main "EasiCamera.UI.dll"
$bizDll  = Join-Path $main "EasiCamera.Business.dll"

foreach ($f in @($patcher, $cecil, $apiDll, $uiDll, $bizDll)) {
    if (-not (Test-Path $f)) { Write-Host "[!] Missing: $f" -ForegroundColor Red; Read-Host "Press Enter to close"; exit 1 }
}

Write-Host "=== EasiCamera brand + AI restriction removal ===" -ForegroundColor Cyan

# Stop EasiCamera processes to release file locks
Write-Host "[*] Stopping EasiCamera processes..." -ForegroundColor Cyan
$procs = Get-Process -Name "EasiCamera","EasiCameraGuardian" -ErrorAction SilentlyContinue
if ($procs) {
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "[+] Stopped: $($procs.Name -join ', ')" -ForegroundColor Green
} else {
    Write-Host "[*] EasiCamera not running" -ForegroundColor DarkGray
}

function Run-Patch([string]$mode, [string]$dll) {
    if ($Restore) {
        Write-Host "`n[*] restore $mode : $(Split-Path $dll -Leaf)" -ForegroundColor Cyan
        & $patcher restore $dll
    } else {
        Write-Host "`n[*] patch $mode : $(Split-Path $dll -Leaf)" -ForegroundColor Cyan
        & $patcher $mode $dll
    }
    if ($LASTEXITCODE -ne 0) { Write-Host "[!] $mode failed (exit=$LASTEXITCODE)" -ForegroundColor Red }
}

Run-Patch api    $apiDll
Run-Patch uiconv $uiDll
Run-Patch bizai  $bizDll

Write-Host ""
if ($Restore) {
    Write-Host "[+] All restored. Brand + AI restrictions are back." -ForegroundColor Green
} else {
    Write-Host "[+] All patches applied!" -ForegroundColor Green
    Write-Host "    - Device enumeration: non-Seewo cameras accepted" -ForegroundColor DarkGray
    Write-Host "    - AI Image Enhance button: always visible" -ForegroundColor DarkGray
    Write-Host "    - Simplified Intelligent Identification: not SC13-only" -ForegroundColor DarkGray
    Write-Host "[*] Start EasiCamera and test with your non-Seewo camera." -ForegroundColor Yellow
    Write-Host "[*] To revert: re-run with -Restore" -ForegroundColor Yellow
}

Read-Host "`nPress Enter to close"
