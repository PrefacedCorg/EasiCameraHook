# verify embedded resource + new converter patches on UI.dll copy
$build = Join-Path $PSScriptRoot "build"
$main = "C:\Program Files (x86)\Seewo\EasiCamera\EasiCamera_2.1.0.4392\Main"

Write-Host "===== 1. embedded resources in launcher exe =====" -ForegroundColor Cyan
$exe = Join-Path $build "EasiCameraLauncher.exe"
$asm = [System.Reflection.Assembly]::LoadFile($exe)
$names = $asm.GetManifestResourceNames()
"resource count: " + $names.Count
foreach ($n in $names) { "  " + $n }
$asm.Dispose()

Write-Host "`n===== 2. patch 3 new converters on UI.dll copy =====" -ForegroundColor Cyan
Add-Type -Path (Join-Path $build "Mono.Cecil.dll")
$test = Join-Path $build "test_conv"
Remove-Item $test -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $test | Out-Null
Copy-Item (Join-Path $main "*.dll") $test -Force
$uiDll = Join-Path $test "EasiCamera.UI.dll"

$u = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($uiDll)
$targets = @(
    "EasiCamera.UI.SeewoCameraDenoiseEnabledToVisibilityConverter",
    "EasiCamera.UI.SeewoCameraFrameRateSettingEnabledToVisibilityConverter",
    "EasiCamera.UI.Converters.CurrentCameraControllerCanCalibrateTrapezoidConverter"
)
foreach ($fn in $targets) {
    $t = $null
    foreach ($m in $u.MainModule.Types) { if ($m.FullName -eq $fn) { $t = $m; break } }
    if (-not $t) { "  [!] not found: $fn"; continue }
    $c = $null; foreach ($x in $t.Methods) { if ($x.Name -eq 'Convert') { $c = $x; break } }
    # find visibility ref
    $vis = $null
    foreach ($i in $c.Body.Instructions) {
        if ($i.OpCode.Name -eq 'box') {
            $tr = $i.Operand
            if ($tr.FullName -eq 'System.Windows.Visibility') { $vis = $tr; break }
        }
    }
    # insert ldc.i4.0; box; ret at start
    $il = $c.Body.GetILProcessor()
    $first = $c.Body.Instructions[0]
    $il.InsertBefore($first, ($il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_0)))
    $il.InsertBefore($first, ($il.Create([Mono.Cecil.Cil.OpCodes]::Box, $vis)))
    $il.InsertBefore($first, ($il.Create([Mono.Cecil.Cil.OpCodes]::Ret)))
    "  patched: $fn"
}
$tmp = $uiDll + ".tmp"
$u.Write($tmp)
$u.Dispose()
Copy-Item $tmp $uiDll -Force
Remove-Item $tmp -Force

Write-Host "`n===== 3. verify IL of 3 patched converters =====" -ForegroundColor Cyan
$u2 = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($uiDll)
foreach ($fn in $targets) {
    $t = $null
    foreach ($m in $u2.MainModule.Types) { if ($m.FullName -eq $fn) { $t = $m; break } }
    $c = $null; foreach ($x in $t.Methods) { if ($x.Name -eq 'Convert') { $c = $x; break } }
    $label = ($fn -split '\.')[-1]
    "[$label] first 4 instrs:"
    $cnt = [Math]::Min(4, $c.Body.Instructions.Count)
    for ($k=0; $k -lt $cnt; $k++) {
        $ins = $c.Body.Instructions[$k]
        "    " + $ins.OpCode.Name + " " + $ins.Operand
    }
}
$u2.Dispose()
