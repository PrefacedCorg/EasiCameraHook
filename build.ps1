# EasiCamera 补丁一键编译脚本
# 产物输出到 build\ 目录：
#   EasiCameraPatch.dll  - C# 补丁（Harmony patch）
#   0Harmony.dll         - Harmony 运行库（已随包准备）
#   Bootstrap.dll        - C++ 引导 DLL（被注入目标进程）
#   Injector.exe         - C++ 注入器
#param()

$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$src  = Join-Path $root "src"
$lib  = Join-Path $root "lib"
$build = Join-Path $root "build"
New-Item -ItemType Directory -Force -Path $build | Out-Null

# ====== 1. 刷新 PATH 找到 MinGW ======
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
$clang = Get-Command i686-w64-mingw32-clang++ -ErrorAction SilentlyContinue
if (-not $clang) {
    Write-Host "[!] i686-w64-mingw32-clang++ 未找到，请确认 LLVM-MinGW 已安装" -ForegroundColor Red
    exit 1
}
Write-Host "[+] C++ 编译器: $($clang.Source)" -ForegroundColor Green

# ====== 2. 编译 C# 补丁 DLL ======
$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    Write-Host "[!] csc.exe 未找到: $csc" -ForegroundColor Red
    exit 2
}
$harmonyDll = Join-Path $lib "0Harmony.dll"
if (-not (Test-Path $harmonyDll)) {
    Write-Host "[!] 0Harmony.dll 未找到，请先运行准备步骤" -ForegroundColor Red
    exit 3
}

Write-Host "`n[1/3] 编译 EasiCameraPatch.dll ..." -ForegroundColor Cyan
& $csc /nologo /target:library /platform:x86 /optimize+ `
    /reference:"$harmonyDll" `
    /out:"$(Join-Path $build 'EasiCameraPatch.dll')" `
    "$(Join-Path $src 'EasiCameraPatch\PatchEntry.cs')"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[!] C# 编译失败" -ForegroundColor Red
    exit 4
}
# 拷贝 Harmony 依赖到 build 目录（CLR 加载补丁时需要同目录找到 0Harmony.dll）
Copy-Item $harmonyDll -Destination $build -Force
Write-Host "[+] EasiCameraPatch.dll OK" -ForegroundColor Green

# ====== 3. 编译 Bootstrap.dll ======
Write-Host "`n[2/3] 编译 Bootstrap.dll ..." -ForegroundColor Cyan
& $clang -shared -static -O2 `
    -o "$(Join-Path $build 'Bootstrap.dll')" `
    "$(Join-Path $src 'Bootstrap\Bootstrap.cpp')" `
    -lkernel32 -lole32 -loleaut32
if ($LASTEXITCODE -ne 0) {
    Write-Host "[!] Bootstrap 编译失败" -ForegroundColor Red
    exit 5
}
Write-Host "[+] Bootstrap.dll OK" -ForegroundColor Green

# ====== 4. 编译 Injector.exe ======
Write-Host "`n[3/3] 编译 Injector.exe ..." -ForegroundColor Cyan
& $clang -static -O2 -mconsole -municode `
    -o "$(Join-Path $build 'Injector.exe')" `
    "$(Join-Path $src 'Injector\Injector.cpp')" `
    -lkernel32 -lpsapi
if ($LASTEXITCODE -ne 0) {
    Write-Host "[!] Injector 编译失败" -ForegroundColor Red
    exit 6
}
Write-Host "[+] Injector.exe OK" -ForegroundColor Green

# ====== 5. 产物清单 ======
Write-Host "`n========== 编译完成 ==========" -ForegroundColor Yellow
Get-ChildItem $build -Filter "*.dll","*.exe" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("  {0,-25} {1,10} bytes" -f $_.Name, $_.Length)
}
Write-Host "`n使用方法：在 EasiCamera.exe 运行时，执行 build\Injector.exe" -ForegroundColor Yellow
Write-Host "或运行 inject.ps1 自动查找进程并注入" -ForegroundColor Yellow
