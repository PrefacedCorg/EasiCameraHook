# verify all 6 patches on real DLLs
$build = Join-Path $PSScriptRoot "build"
$main = "C:\Program Files (x86)\Seewo\EasiCamera\EasiCamera_2.1.0.4392\Main"
Add-Type -Path (Join-Path $build "Mono.Cecil.dll")
$out = Join-Path $build "verify6.log"
"" | Out-File $out
function Log($s) { $s | Out-File $out -Append; Write-Host $s }

Log "===== 6-point patch verification ====="

# 1. api: IsSeewoCamera(DsDevice)
$api = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $main "EasiCamera.Api.dll"))
$t = $null; foreach($m in $api.MainModule.Types){ if($m.FullName -eq 'EasiCamera.CameraExtension'){$t=$m;break} }
$mm = $null; foreach($x in $t.Methods){ if($x.Name -eq 'IsSeewoCamera' -and $x.Parameters.Count -eq 1 -and $x.Parameters[0].ParameterType.Name -eq 'DsDevice'){$mm=$x;break} }
$ok1 = ($mm.Body.Instructions.Count -eq 2 -and $mm.Body.Instructions[0].OpCode.Name -eq 'ldc.i4.1' -and $mm.Body.Instructions[1].OpCode.Name -eq 'ret')
Log ("[1] api IsSeewoCamera(DsDevice) -> " + $(if($ok1){'OK ldc.i4.1;ret'}else{'FAIL'}))
$api.Dispose()

# 2-5. UI converters
$ui = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $main "EasiCamera.UI.dll"))
$convTargets = @(
    @{name='EasiCamera.UI.SeewoCameraButtonVisibilityConverter'; label='[2] SeewoButtonConv (AI button)'; mode='toreverse'},
    @{name='EasiCamera.UI.SeewoCameraDenoiseEnabledToVisibilityConverter'; label='[3] denoise'; mode='simple'},
    @{name='EasiCamera.UI.SeewoCameraFrameRateSettingEnabledToVisibilityConverter'; label='[4] frame rate'; mode='simple'},
    @{name='EasiCamera.UI.Converters.CurrentCameraControllerCanCalibrateTrapezoidConverter'; label='[5] trapezoid'; mode='simple'}
)
foreach ($ct in $convTargets) {
    $t = $null; foreach($m in $ui.MainModule.Types){ if($m.FullName -eq $ct.name){$t=$m;break} }
    $c = $null; foreach($x in $t.Methods){ if($x.Name -eq 'Convert'){$c=$x;break} }
    $ins = $c.Body.Instructions
    if ($ct.mode -eq 'toreverse') {
        $ok = ($ins.Count -ge 2 -and $ins[0].OpCode.Name -eq 'ldarg.0' -and $ins[1].OpCode.Name -eq 'call' -and $ins[1].Operand.Name -eq 'get_ToReverseResult')
        Log ($ct.label + ' -> ' + $(if($ok){'OK ldarg.0;call get_ToReverseResult;...'}else{'FAIL'}))
    } else {
        $ok = ($ins.Count -ge 3 -and $ins[0].OpCode.Name -eq 'ldc.i4.0' -and $ins[1].OpCode.Name -eq 'box' -and $ins[2].OpCode.Name -eq 'ret')
        Log ($ct.label + ' -> ' + $(if($ok){'OK ldc.i4.0;box;ret'}else{'FAIL'}))
    }
}
$ui.Dispose()

# 6. bizai
$biz = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $main "EasiCamera.Business.dll"))
$t2 = $null; foreach($m in $biz.MainModule.Types){ if($m.Name -eq 'IntelligentIdentificationViewModel'){$t2=$m;break} }
$u = $null; foreach($x in $t2.Methods){ if($x.Name -eq 'UpdateSimplifiedIntelligentIdentificationStatus'){$u=$x;break} }
$found = $false
for($k=1;$k -lt $u.Body.Instructions.Count;$k++){
    if($u.Body.Instructions[$k].OpCode.Name -eq 'call'){
        $p = $u.Body.Instructions[$k-1]
        if($p.OpCode.Name -eq 'ldc.i4.1'){ $found=$true; break }
    }
}
Log ('[6] bizai simplified recognition -> ' + $(if($found){'OK setter preceded by ldc.i4.1'}else{'FAIL'}))
$biz.Dispose()

# backups
Log ''
Log 'backups:'
foreach ($d in @('EasiCamera.Api.dll','EasiCamera.UI.dll','EasiCamera.Business.dll')) {
    Log ('  ' + $d + '.bak : ' + (Test-Path (Join-Path $main ($d + '.bak'))))
}
