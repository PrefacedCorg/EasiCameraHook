# ASCII-only. Uses $PSScriptRoot to avoid Chinese-path encoding issues.
$build = Join-Path $PSScriptRoot "build"
$main = "C:\Program Files (x86)\Seewo\EasiCamera\EasiCamera_2.1.0.4392\Main"
Add-Type -Path (Join-Path $build "Mono.Cecil.dll")
$out = Join-Path $build "verify_real.log"

function Log($s) { $s | Out-File $out -Append }

"===== REAL DLL VERIFICATION =====" | Out-File $out

Log "[1] api:"
$api = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $main "EasiCamera.Api.dll"))
$t = $null; foreach($m in $api.MainModule.Types){ if($m.FullName -eq 'EasiCamera.CameraExtension'){$t=$m;break} }
$mm = $null; foreach($x in $t.Methods){ if($x.Name -eq 'IsSeewoCamera' -and $x.Parameters.Count -eq 1 -and $x.Parameters[0].ParameterType.Name -eq 'DsDevice'){$mm=$x;break} }
Log ("  IsSeewoCamera(DsDevice) instrs=" + $mm.Body.Instructions.Count + ": " + $mm.Body.Instructions[0].OpCode.Name + ", " + $mm.Body.Instructions[1].OpCode.Name)
Log ("  api bak exists: " + (Test-Path (Join-Path $main "EasiCamera.Api.dll.bak")))
$api.Dispose()

Log "[2] uiconv:"
$ui = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $main "EasiCamera.UI.dll"))
$t1 = $null; foreach($m in $ui.MainModule.Types){ if($m.FullName -eq 'EasiCamera.UI.SeewoCameraButtonVisibilityConverter'){$t1=$m;break} }
$c = $null; foreach($x in $t1.Methods){ if($x.Name -eq 'Convert'){$c=$x;break} }
Log ("  Convert[0..1]: " + $c.Body.Instructions[0].OpCode.Name + " / " + $c.Body.Instructions[1].OpCode.Name)
Log ("  Convert[1] operand name: " + $c.Body.Instructions[1].Operand.Name)
Log ("  ui bak exists: " + (Test-Path (Join-Path $main "EasiCamera.UI.dll.bak")))
$ui.Dispose()

Log "[3] bizai:"
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
Log ("  bizai setter preceded by ldc.i4.1: " + $found)
Log ("  biz bak exists: " + (Test-Path (Join-Path $main "EasiCamera.Business.dll.bak")))
$biz.Dispose()

Get-Content $out -Raw
