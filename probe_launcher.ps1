# ASCII only
$exe = "C:\Program Files (x86)\Seewo\EasiCamera\sweclauncher\sweclauncher.exe"

Write-Host "===== 1. shortcuts =====" -ForegroundColor Cyan
$locs = @("$env:USERPROFILE\Desktop","$env:PUBLIC\Desktop","$env:APPDATA\Microsoft\Windows\Start Menu\Programs","$env:ProgramData\Microsoft\Windows\Start Menu\Programs")
foreach ($l in $locs) {
    Get-ChildItem $l -Filter "*.lnk" -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -match "EasiCamera|seewo" } | ForEach-Object {
        $sh = New-Object -ComObject WScript.Shell
        $lnk = $sh.CreateShortcut($_.FullName)
        "  $($_.FullName)"
        "    Target: $($lnk.TargetPath)"
        "    Args: $($lnk.Arguments)"
    }
}

Write-Host "`n===== 2. sweclauncher imported dlls =====" -ForegroundColor Cyan
$bytes = [System.IO.File]::ReadAllBytes($exe)
$txt = [System.Text.Encoding]::ASCII.GetString($bytes)
$dlls = [regex]::Matches($txt, '[A-Za-z0-9_\-]+\.dll') | ForEach-Object { $_.Value } | Sort-Object -Unique
$dlls | ForEach-Object { "  $_" }

Write-Host "`n===== 3. EasiCamera parent process =====" -ForegroundColor Cyan
$p = Get-Process -Name "EasiCamera" -ErrorAction SilentlyContinue
if ($p) {
    "EasiCamera PID $($p.Id)"
    try {
        $pi = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)"
        "  ParentPID: $($pi.ParentProcessId)"
        "  CmdLine: $($pi.CommandLine)"
        $pp = Get-CimInstance Win32_Process -Filter "ProcessId=$($pi.ParentProcessId)" -ErrorAction SilentlyContinue
        if ($pp) { "  Parent: $($pp.Name) - $($pp.CommandLine)" }
    } catch {}
} else { "EasiCamera not running" }

Write-Host "`n===== 4. guardian/launcher running? =====" -ForegroundColor Cyan
Get-Process -Name "sweclauncher","EasiCameraGuardian" -ErrorAction SilentlyContinue | ForEach-Object {
    "$($_.Id) $($_.Name) $($_.Path)"
}

Write-Host "`n===== 5. meaningful strings in sweclauncher (seewo/easi/update/start/launch/process) =====" -ForegroundColor Cyan
$kw = "seewo|easi|update|launch|start|process|\.exe|http|config|guardian|main|version|sleep|wait"
$ms = [regex]::Matches($txt, "(?i)($kw)[a-z0-9_\.\/\-]{2,40}") | ForEach-Object { $_.Value } | Sort-Object -Unique
$ms | Select-Object -First 40 | ForEach-Object { "  $_" }
