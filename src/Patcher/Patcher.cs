// EasiCamera 品牌/AI 限制解除 —— IL 补丁器（多目标）
// 用 Mono.Cecil 对三个 DLL 做外科手术式 IL 改写：
//   api    : EasiCamera.Api.dll
//            CameraExtension.IsSeewoCamera(DsDevice) -> 恒返回 true（设备枚举放行）
//   uiconv : EasiCamera.UI.dll
//            SeewoCameraButtonVisibilityConverter.Convert 开头插入
//            "if (!ToReverseResult) return Visibility.Visible;"
//            （AI 增强按钮恒显示；设置菜单 ToReverseResult=true 走原逻辑不受影响）
//   bizai  : EasiCamera.Business.dll
//            IntelligentIdentificationViewModel.UpdateSimplifiedIntelligentIdentificationStatus
//            把 setter 调用前的 ldloc.0(SC13 判断结果) 换成 ldc.i4.1(恒 true)
//            （保留 m__0001 守卫，仅放开 SC13 限制）
//
// 用法：
//   Patcher.exe api    <EasiCamera.Api.dll>
//   Patcher.exe uiconv <EasiCamera.UI.dll>
//   Patcher.exe bizai  <EasiCamera.Business.dll>
//   Patcher.exe restore <任意已打补丁的 dll>
//
// 特性：每个目标自动备份 .bak（仅首次）、幂等（已打过跳过）、写临时文件再替换。
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Patcher
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 2;
        }
        string mode = args[0].ToLowerInvariant();
        if (mode == "restore")
        {
            if (args.Length < 2) { PrintUsage(); return 2; }
            return Restore(args[1]);
        }
        if (args.Length < 2) { PrintUsage(); return 2; }
        string dll = args[1];
        if (!File.Exists(dll)) { Console.Error.WriteLine("找不到文件: " + dll); return 3; }

        switch (mode)
        {
            case "api":    return PatchApi(dll);
            case "uiconv": return PatchUiConverter(dll);
            case "bizai":  return PatchBizAi(dll);
            default: PrintUsage(); return 2;
        }
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine("用法: Patcher <api|uiconv|bizai|restore> <dll路径>");
    }

    // ===== 通用：备份 + 读取 =====
    static AssemblyDefinition ReadAssembly(string dll, out string bak)
    {
        bak = dll + ".bak";
        if (!File.Exists(bak)) { File.Copy(dll, bak); Console.WriteLine("[+] 已备份 -> " + bak); }
        else Console.WriteLine("[*] 备份已存在: " + bak);

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
        var rp = new ReaderParameters { AssemblyResolver = resolver };
        return AssemblyDefinition.ReadAssembly(dll, rp);
    }

    // ===== 通用：写回（临时文件 + 替换，避免 Cecil 读句柄占用） =====
    static int WriteBack(AssemblyDefinition asm, string dll)
    {
        string tmp = dll + ".tmp";
        try { asm.Write(tmp); asm.Dispose(); }
        catch (Exception e)
        {
            Console.Error.WriteLine("写补丁文件失败: " + e.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return 8;
        }
        try { File.Copy(tmp, dll, true); File.Delete(tmp); }
        catch (Exception e)
        {
            Console.Error.WriteLine("替换原文件失败（请先关闭 EasiCamera）: " + e.Message);
            return 8;
        }
        return 0;
    }

    static int Verify(string dll)
    {
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
            var rp = new ReaderParameters { AssemblyResolver = resolver };
            var check = AssemblyDefinition.ReadAssembly(dll, rp);
            check.Dispose();
            Console.WriteLine("[+] 校验通过：程序集可正常重载。");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[!] 校验失败（请用 restore 恢复）: " + e.Message);
            return 9;
        }
    }

    static int Restore(string dll)
    {
        string bak = dll + ".bak";
        if (!File.Exists(bak)) { Console.Error.WriteLine("找不到备份: " + bak); return 4; }
        File.Copy(bak, dll, true);
        Console.WriteLine("[+] 已从备份恢复: " + bak);
        return 0;
    }

    // ===== 目标 1：api —— IsSeewoCamera(DsDevice) -> ldc.i4.1; ret =====
    static int PatchApi(string dll)
    {
        Console.WriteLine("=== [api] IsSeewoCamera(DsDevice) -> true ===");
        string bak;
        AssemblyDefinition asm;
        try { asm = ReadAssembly(dll, out bak); }
        catch (Exception e) { Console.Error.WriteLine("读取失败（请先关闭 EasiCamera）: " + e.Message); return 5; }

        TypeDefinition camExt = null;
        foreach (var mod in asm.Modules) { camExt = mod.GetType("EasiCamera.CameraExtension"); if (camExt != null) break; }
        if (camExt == null) { Console.Error.WriteLine("未找到 EasiCamera.CameraExtension"); return 6; }

        MethodDefinition target = FindIsSeewoCameraDsDevice(camExt);
        if (target == null) { Console.Error.WriteLine("未找到 IsSeewoCamera(DsDevice)"); return 7; }
        Console.WriteLine("[*] 目标: " + target.FullName);

        var instr = target.Body.Instructions;
        if (instr.Count == 2 && instr[0].OpCode == OpCodes.Ldc_I4_1 && instr[1].OpCode == OpCodes.Ret)
        {
            Console.WriteLine("[*] 已打过补丁，跳过。");
            return 0;
        }
        target.Body.Instructions.Clear();
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.InitLocals = false;
        var il = target.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        int r = WriteBack(asm, dll);
        if (r != 0) return r;
        Console.WriteLine("[+] 已打补丁: IsSeewoCamera(DsDevice) -> true");
        return Verify(dll);
    }

    static MethodDefinition FindIsSeewoCameraDsDevice(TypeDefinition t)
    {
        foreach (var m in t.Methods)
        {
            if (m.Name != "IsSeewoCamera") continue;
            if (m.ReturnType.MetadataType != MetadataType.Boolean) continue;
            if (m.Parameters.Count != 1) continue;
            if (m.Parameters[0].ParameterType.Name != "DsDevice") continue;
            return m;
        }
        return null;
    }

    // ===== 目标 2：uiconv —— Convert 开头插入 ToReverseResult 守卫 =====
    static int PatchUiConverter(string dll)
    {
        Console.WriteLine("=== [uiconv] SeewoCameraButtonVisibilityConverter.Convert -> AI 按钮恒显示 ===");
        string bak;
        AssemblyDefinition asm;
        try { asm = ReadAssembly(dll, out bak); }
        catch (Exception e) { Console.Error.WriteLine("读取失败（请先关闭 EasiCamera）: " + e.Message); return 5; }

        TypeDefinition convType = null;
        foreach (var mod in asm.Modules)
            foreach (var t in mod.Types)
                if (t.FullName == "EasiCamera.UI.SeewoCameraButtonVisibilityConverter") { convType = t; break; }
        if (convType == null) { Console.Error.WriteLine("未找到 SeewoCameraButtonVisibilityConverter"); return 6; }

        MethodDefinition convert = null;
        foreach (var m in convType.Methods) if (m.Name == "Convert") { convert = m; break; }
        if (convert == null) { Console.Error.WriteLine("未找到 Convert 方法"); return 7; }
        Console.WriteLine("[*] 目标: " + convert.FullName);

        // 幂等：检查开头是否已是 ldarg.0 + call get_ToReverseResult
        var ins = convert.Body.Instructions;
        if (ins.Count >= 2 && ins[0].OpCode == OpCodes.Ldarg_0
            && ins[1].OpCode == OpCodes.Call)
        {
            MethodReference tmpMr = ins[1].Operand as MethodReference;
            if (tmpMr != null && tmpMr.Name == "get_ToReverseResult")
            {
                Console.WriteLine("[*] 已打过补丁，跳过。");
                return 0;
            }
        }

        // 找 get_ToReverseResult 方法引用
        MethodReference getToRev = null;
        foreach (var m in convType.Methods) if (m.Name == "get_ToReverseResult") { getToRev = m; break; }
        if (getToRev == null) { Console.Error.WriteLine("未找到 get_ToReverseResult"); return 7; }

        // 找 System.Windows.Visibility 类型引用（从已有 box 指令拿）
        TypeReference visType = null;
        foreach (var i in ins)
        {
            if (i.OpCode == OpCodes.Box)
            {
                TypeReference tr = i.Operand as TypeReference;
                if (tr != null && tr.FullName == "System.Windows.Visibility") { visType = tr; break; }
            }
        }
        if (visType == null) { Console.Error.WriteLine("未找到 Visibility 类型引用"); return 7; }

        // 在方法开头插入：
        //   ldarg.0
        //   call  get_ToReverseResult
        //   brtrue ORIGINAL_FIRST      // 设置菜单(ToReverseResult=true)走原逻辑
        //   ldc.i4.0                    // Visibility.Visible = 0
        //   box   System.Windows.Visibility
        //   ret
        var il = convert.Body.GetILProcessor();
        var first = ins[0];
        var iLdarg0 = il.Create(OpCodes.Ldarg_0);
        var iCallGet = il.Create(OpCodes.Call, getToRev);
        var iBrTrue = il.Create(OpCodes.Brtrue, first);   // 长分支，避免偏移问题
        var iLdVis = il.Create(OpCodes.Ldc_I4_0);
        var iBox = il.Create(OpCodes.Box, visType);
        var iRet = il.Create(OpCodes.Ret);

        il.InsertBefore(first, iLdarg0);
        il.InsertBefore(first, iCallGet);
        il.InsertBefore(first, iBrTrue);
        il.InsertBefore(first, iLdVis);
        il.InsertBefore(first, iBox);
        il.InsertBefore(first, iRet);

        int r = WriteBack(asm, dll);
        if (r != 0) return r;
        Console.WriteLine("[+] 已打补丁: Convert -> AI 按钮(ToReverseResult=false)恒返回 Visible");
        return Verify(dll);
    }

    // ===== 目标 3：bizai —— 简化识别放开 SC13 限制 =====
    static int PatchBizAi(string dll)
    {
        Console.WriteLine("=== [bizai] UpdateSimplifiedIntelligentIdentificationStatus -> 放开 SC13 限制 ===");
        string bak;
        AssemblyDefinition asm;
        try { asm = ReadAssembly(dll, out bak); }
        catch (Exception e) { Console.Error.WriteLine("读取失败（请先关闭 EasiCamera）: " + e.Message); return 5; }

        TypeDefinition vmType = null;
        foreach (var mod in asm.Modules)
            foreach (var t in mod.Types)
                if (t.Name == "IntelligentIdentificationViewModel") { vmType = t; break; }
        if (vmType == null) { Console.Error.WriteLine("未找到 IntelligentIdentificationViewModel"); return 6; }

        MethodDefinition upd = null;
        foreach (var m in vmType.Methods)
            if (m.Name == "UpdateSimplifiedIntelligentIdentificationStatus") { upd = m; break; }
        if (upd == null) { Console.Error.WriteLine("未找到 UpdateSimplifiedIntelligentIdentificationStatus"); return 7; }
        Console.WriteLine("[*] 目标: " + upd.FullName);

        // 策略：找本类型上 void(bool) 的 setter 调用，把其前一条 ldloc.0 换成 ldc.i4.1
        // 这样 else 分支恒为 true（保留 m__0001 守卫）
        var instrs = upd.Body.Instructions;
        int patchedCount = 0;
        for (int i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode != OpCodes.Call) continue;
            var mr = instrs[i].Operand as MethodReference;
            if (mr == null) continue;
            if (mr.DeclaringType.FullName != vmType.FullName) continue;
            if (mr.Parameters.Count != 1) continue;
            if (mr.Parameters[0].ParameterType.MetadataType != MetadataType.Boolean) continue;
            if (mr.ReturnType.MetadataType != MetadataType.Void) continue;
            // 这是本类型的 void(bool) setter
            var prev = instrs[i - 1];
            if (prev.OpCode == OpCodes.Ldloc_0)
            {
                // 幂等：若前一条已是 ldc.i4.1 则跳过
                Console.WriteLine("[*] 找到 setter 调用，前驱 ldloc.0 -> ldc.i4.1");
                var il = upd.Body.GetILProcessor();
                var newIns = il.Create(OpCodes.Ldc_I4_1);
                il.InsertBefore(prev, newIns);
                il.Remove(prev);
                patchedCount++;
            }
            else if (prev.OpCode == OpCodes.Ldc_I4_1)
            {
                Console.WriteLine("[*] setter 前驱已是 ldc.i4.1，已打过补丁。");
                patchedCount++;
            }
            else if (prev.OpCode == OpCodes.Ldc_I4_0)
            {
                // m__0001=true 分支设 false，保留不动
            }
        }
        if (patchedCount == 0)
        {
            Console.Error.WriteLine("[!] 未找到可改写的 setter 调用模式");
            return 7;
        }

        int r = WriteBack(asm, dll);
        if (r != 0) return r;
        Console.WriteLine("[+] 已打补丁: 简化智能识别不再限定 SC13");
        return Verify(dll);
    }
}
