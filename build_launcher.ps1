# build_launcher.ps1 - ASCII only
$build = Join-Path $PSScriptRoot "build"
$lib = Join-Path $PSScriptRoot "lib"
$src = Join-Path $PSScriptRoot "src\Launcher"
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Force -Path $build | Out-Null
Copy-Item (Join-Path $lib "Mono.Cecil.dll") (Join-Path $build "Mono.Cecil.dll") -Force

Set-Location $build
Write-Host "[*] cwd: $(Get-Location)"

$argList = @(
    "/nologo", "/target:exe", "/platform:x86", "/optimize+", "/codepage:65001",
    "/reference:Mono.Cecil.dll",
    "/resource:Mono.Cecil.dll,Mono.Cecil.dll",
    "/win32manifest:`"$src\app.manifest`"",
    "/out:EasiCameraLauncher.exe",
    "`"$src\EasiCameraLauncher.cs`""
)
Write-Host "[*] csc args: $argList"
& $csc $argList 2>&1 | Out-String | Write-Host
Write-Host "csc exit=$LASTEXITCODE"

if ($LASTEXITCODE -eq 0) {
    $f = Get-Item (Join-Path $build "EasiCameraLauncher.exe")
    Write-Host ("[+] EasiCameraLauncher.exe: " + $f.Length + " bytes")
}
