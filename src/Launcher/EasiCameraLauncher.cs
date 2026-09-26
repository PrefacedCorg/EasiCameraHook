// EasiCamera 品牌+AI 限制解除 —— 单文件 Launcher
// 编译为单 exe（Mono.Cecil 嵌入为资源），用户双击即可：
//   1. UAC 提权（manifest 要求 requireAdministrator）
//   2. 杀 EasiCamera / Guardian 进程（释放 DLL 文件锁）
//   3. 对 3 个 DLL 共 6 处打 IL 补丁（幂等，自动备份 .bak）
//   4. 启动 EasiScreenPerception 服务并等待端口就绪
//   5. 启动 EasiCamera
//
// 用法：
//   EasiCameraLauncher.exe            打补丁 + 启动
//   EasiCameraLauncher.exe --restore  从 .bak 恢复 3 个 DLL
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal class Launcher
{
    // ===== 路径（带版本目录，启动时动态查找） =====
    static string SeewoBase = @"C:\Program Files (x86)\Seewo";
    static string MainDir;       // EasiCamera 主程序目录
    static string EspDir;        // EasiScreenPerception 父目录

    [STAThread]
    static int Main(string[] args)
    {
        // 嵌入资源加载 Mono.Cecil
        AppDomain.CurrentDomain.AssemblyResolve += LoadEmbedded;

        // 输出用 UTF8，避免控制台中文乱码
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        bool restore = args.Length > 0 && args[0] == "--restore";

        if (!IsAdmin())
        {
            // manifest 应已保证管理员；若没有，尝试 runas 重启
            Console.WriteLine("[*] Admin required, relaunching with UAC...");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Assembly.GetCallingAssembly().Location;
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                if (restore) psi.Arguments = "--restore";
                Process.Start(psi);
            }
            catch { }
            return 0;
        }

        // 定位路径
        if (!LocatePaths())
        {
            Console.WriteLine("[!] EasiCamera install not found under " + SeewoBase);
            Console.ReadLine();
            return 1;
        }

        Console.WriteLine("=== EasiCamera brand+AI restriction removal ===");
        Console.WriteLine("Main:   " + MainDir);
        if (EspDir != null) Console.WriteLine("Svc:    " + EspDir);
        Console.WriteLine();

        // 1. 杀进程
        KillProcesses();

        if (restore)
        {
            RestoreAll();
            Console.WriteLine();
            Console.WriteLine("[+] All restored. Press Enter to close.");
            Console.ReadLine();
            return 0;
        }

        // 2. 打补丁
        Console.WriteLine("[*] Patching...");
        PatchApi(Path.Combine(MainDir, "EasiCamera.Api.dll"));
        PatchUi(Path.Combine(MainDir, "EasiCamera.UI.dll"));
        PatchBiz(Path.Combine(MainDir, "EasiCamera.Business.dll"));
        Console.WriteLine();

        // 3. 启动服务
        StartEspService();
        Console.WriteLine();

        // 4. 启动 EasiCamera
        Console.WriteLine("[*] Starting EasiCamera...");
        StartProcess(Path.Combine(MainDir, "EasiCamera.exe"));

        Console.WriteLine();
        Console.WriteLine("[+] Done. Non-Seewo cameras + AI buttons should work now.");
        Console.WriteLine("[*] To revert: run this exe with --restore");
        Console.WriteLine();
        Console.WriteLine("Press Enter to close this window (EasiCamera will keep running)...");
        Console.ReadLine();
        return 0;
    }

    // ===== 嵌入资源加载 Cecil =====
    static Assembly LoadEmbedded(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (name == "Mono.Cecil")
        {
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll");
            if (s == null) return null;
            byte[] b = new byte[s.Length];
            s.Read(b, 0, b.Length);
            return Assembly.Load(b);
        }
        return null;
    }

    // ===== 路径定位 =====
    static bool LocatePaths()
    {
        // EasiCamera 主目录：Seewo\EasiCamera\EasiCamera_x.x.x\Main
        string camRoot = Path.Combine(SeewoBase, "EasiCamera");
        if (!Directory.Exists(camRoot)) return false;
        MainDir = FindVersionedDir(camRoot, "EasiCamera_", "Main");
        if (MainDir == null) return false;

        // EasiScreenPerception 服务目录：Seewo\EasiScreenPerception\EasiScreenPerception_x.x.x
        string espRoot = Path.Combine(SeewoBase, "EasiScreenPerception");
        if (Directory.Exists(espRoot))
        {
            EspDir = FindVersionedDir(espRoot, "EasiScreenPerception_", null);
        }
        return true;
    }

    static string FindVersionedDir(string root, string prefix, string sub)
    {
        string[] dirs = Directory.GetDirectories(root, prefix + "*");
        if (dirs.Length == 0) return null;
        // 取版本号最大的（按目录名排序，通常版本号大的在后面）
        Array.Sort(dirs);
        string chosen = dirs[dirs.Length - 1];
        if (sub != null) chosen = Path.Combine(chosen, sub);
        if (!Directory.Exists(chosen)) return null;
        return chosen;
    }

    // ===== 提权检查 =====
    static bool IsAdmin()
    {
        try
        {
            using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch { return false; }
    }

    // ===== 杀进程 =====
    static void KillProcesses()
    {
        Console.WriteLine("[*] Stopping EasiCamera processes...");
        string[] names = { "EasiCamera", "EasiCameraGuardian" };
        foreach (string name in names)
        {
            Process[] ps = Process.GetProcessesByName(name);
            foreach (Process p in ps)
            {
                try
                {
                    p.Kill();
                    Console.WriteLine("  killed " + name + " (PID " + p.Id + ")");
                }
                catch { }
            }
        }
        System.Threading.Thread.Sleep(2000);
    }

    static void StartProcess(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe) });
        }
        catch (Exception e)
        {
            Console.WriteLine("[!] Failed to start " + Path.GetFileName(exe) + ": " + e.Message);
        }
    }

    // ===== 启动 EasiScreenPerception 服务并等待端口就绪 =====
    static void StartEspService()
    {
        Console.WriteLine("[*] Starting EasiScreenPerception service...");
        if (EspDir == null)
        {
            Console.WriteLine("  [!] Service install not found, skip (切题讲评 will not work)");
            return;
        }
        string espExe = Path.Combine(EspDir, "EasiScreenPerception.exe");
        if (!File.Exists(espExe))
        {
            Console.WriteLine("  [!] EasiScreenPerception.exe not found, skip");
            return;
        }
        // 已运行就不重复启动
        if (Process.GetProcessesByName("EasiScreenPerception").Length > 0)
        {
            Console.WriteLine("  [*] Service already running");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(espExe) { WorkingDirectory = EspDir });
        }
        catch (Exception e)
        {
            Console.WriteLine("  [!] Failed to start service: " + e.Message);
            return;
        }

        // 轮询注册表端口（最多 20 秒）
        Console.Write("  [*] Waiting for service ready");
        string regPath = @"Software\Seewo\EasiScreenPerception";
        for (int i = 0; i < 40; i++)
        {
            System.Threading.Thread.Sleep(500);
            Console.Write(".");
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath))
                {
                    if (k != null)
                    {
                        object v = k.GetValue("WebSocketPort");
                        if (v != null)
                        {
                            int port = Convert.ToInt32(v);
                            if (port > 0)
                            {
                                Console.WriteLine();
                                Console.WriteLine("  [+] Service ready, WebSocket port = " + port);
                                return;
                            }
                        }
                    }
                }
            }
            catch { }
        }
        Console.WriteLine();
        Console.WriteLine("  [!] Service not ready in 20s (切题讲评 may fail)");
    }

    // ===== Patch 通用工具 =====
    static AssemblyDefinition ReadAsm(string dll, out string bak)
    {
        bak = dll + ".bak";
        if (!File.Exists(bak)) File.Copy(dll, bak);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dll));
        return AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
    }

    static int WriteBack(AssemblyDefinition asm, string dll)
    {
        string tmp = dll + ".tmp";
        try
        {
            asm.Write(tmp);
            asm.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine("  [!] Write failed: " + e.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return 8;
        }
        try
        {
            File.Copy(tmp, dll, true);
            File.Delete(tmp);
        }
        catch (Exception e)
        {
            Console.WriteLine("  [!] Replace failed (file locked?): " + e.Message);
            return 8;
        }
        return 0;
    }

    static TypeDefinition FindType(AssemblyDefinition asm, string fullName)
    {
        foreach (var mod in asm.Modules)
        {
            TypeDefinition t = mod.GetType(fullName);
            if (t != null) return t;
        }
        foreach (var mod in asm.Modules)
            foreach (var t in mod.Types)
                if (t.FullName == fullName) return t;
        return null;
    }

    // 找 Visibility 类型引用（从已有 box 指令拿）
    static TypeReference FindVisibilityRef(MethodDefinition md)
    {
        foreach (var i in md.Body.Instructions)
        {
            if (i.OpCode == OpCodes.Box)
            {
                TypeReference tr = i.Operand as TypeReference;
                if (tr != null && tr.FullName == "System.Windows.Visibility") return tr;
            }
        }
        return null;
    }

    // 在 Convert 开头插入 ldc.i4.0(Visible); box; ret —— 适用于简单 Converter
    static bool PatchConvertToAlwaysVisible(TypeDefinition convType, string label)
    {
        MethodDefinition convert = null;
        foreach (var m in convType.Methods) if (m.Name == "Convert") { convert = m; break; }
        if (convert == null) { Console.WriteLine("  [!] " + label + ": Convert not found"); return false; }

        var ins = convert.Body.Instructions;
        // 幂等
        if (ins.Count >= 3 && ins[0].OpCode == OpCodes.Ldc_I4_0
            && ins[1].OpCode == OpCodes.Box && ins[2].OpCode == OpCodes.Ret)
        {
            Console.WriteLine("  [*] " + label + ": already patched");
            return true;
        }

        TypeReference visType = FindVisibilityRef(convert);
        if (visType == null) { Console.WriteLine("  [!] " + label + ": Visibility ref not found"); return false; }

        var il = convert.Body.GetILProcessor();
        var first = ins[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Box, visType));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        Console.WriteLine("  [+] " + label + " -> always Visible");
        return true;
    }

    // ===== Patch 1: api - IsSeewoCamera(DsDevice) -> ldc.i4.1; ret =====
    static void PatchApi(string dll)
    {
        Console.WriteLine("[1/3] EasiCamera.Api.dll (device enumeration)");
        string bak; AssemblyDefinition asm;
        try { asm = ReadAsm(dll, out bak); }
        catch (Exception e) { Console.WriteLine("  [!] " + e.Message); return; }

        TypeDefinition t = FindType(asm, "EasiCamera.CameraExtension");
        if (t == null) { Console.WriteLine("  [!] CameraExtension not found"); asm.Dispose(); return; }

        MethodDefinition target = null;
        foreach (var m in t.Methods)
        {
            if (m.Name != "IsSeewoCamera") continue;
            if (m.ReturnType.MetadataType != MetadataType.Boolean) continue;
            if (m.Parameters.Count != 1) continue;
            if (m.Parameters[0].ParameterType.Name != "DsDevice") continue;
            target = m; break;
        }
        if (target == null) { Console.WriteLine("  [!] IsSeewoCamera(DsDevice) not found"); asm.Dispose(); return; }

        var ins = target.Body.Instructions;
        if (ins.Count == 2 && ins[0].OpCode == OpCodes.Ldc_I4_1 && ins[1].OpCode == OpCodes.Ret)
        {
            Console.WriteLine("  [*] already patched");
            asm.Dispose();
            return;
        }
        target.Body.Instructions.Clear();
        target.Body.ExceptionHandlers.Clear();
        target.Body.Variables.Clear();
        target.Body.InitLocals = false;
        var il = target.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        if (WriteBack(asm, dll) == 0) Console.WriteLine("  [+] OK");
    }

    // ===== Patch 2: ui - 4 个 Converter =====
    static void PatchUi(string dll)
    {
        Console.WriteLine("[2/3] EasiCamera.UI.dll (4 converters)");
        string bak; AssemblyDefinition asm;
        try { asm = ReadAsm(dll, out bak); }
        catch (Exception e) { Console.WriteLine("  [!] " + e.Message); return; }

        // 2a. SeewoCameraButtonVisibilityConverter —— 带 ToReverseResult 守卫（保护设置菜单）
        TypeDefinition t1 = FindType(asm, "EasiCamera.UI.SeewoCameraButtonVisibilityConverter");
        if (t1 != null) PatchSeewoButtonConv(t1);
        else Console.WriteLine("  [!] SeewoCameraButtonVisibilityConverter not found");

        // 2b-2d. 三个简单 Converter
        PatchSimpleConv(asm, "EasiCamera.UI.SeewoCameraDenoiseEnabledToVisibilityConverter", "denoise");
        PatchSimpleConv(asm, "EasiCamera.UI.SeewoCameraFrameRateSettingEnabledToVisibilityConverter", "frame rate");
        PatchSimpleConv(asm, "EasiCamera.UI.Converters.CurrentCameraControllerCanCalibrateTrapezoidConverter", "trapezoid calibrate");

        if (WriteBack(asm, dll) == 0) Console.WriteLine("  [+] OK");
    }

    static void PatchSeewoButtonConv(TypeDefinition t)
    {
        MethodDefinition convert = null;
        foreach (var m in t.Methods) if (m.Name == "Convert") { convert = m; break; }
        if (convert == null) { Console.WriteLine("  [!] SeewoButtonConv Convert not found"); return; }
        var ins = convert.Body.Instructions;
        // 幂等
        if (ins.Count >= 2 && ins[0].OpCode == OpCodes.Ldarg_0 && ins[1].OpCode == OpCodes.Call)
        {
            MethodReference mr = ins[1].Operand as MethodReference;
            if (mr != null && mr.Name == "get_ToReverseResult")
            {
                Console.WriteLine("  [*] SeewoButtonConv already patched");
                return;
            }
        }
        MethodReference getToRev = null;
        foreach (var m in t.Methods) if (m.Name == "get_ToReverseResult") { getToRev = m; break; }
        if (getToRev == null) { Console.WriteLine("  [!] get_ToReverseResult not found"); return; }
        TypeReference visType = FindVisibilityRef(convert);
        if (visType == null) { Console.WriteLine("  [!] Visibility ref not found"); return; }

        var il = convert.Body.GetILProcessor();
        var first = ins[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, getToRev));
        il.InsertBefore(first, il.Create(OpCodes.Brtrue, first));
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(first, il.Create(OpCodes.Box, visType));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        Console.WriteLine("  [+] SeewoButtonConv -> AI buttons always Visible");
    }

    static void PatchSimpleConv(AssemblyDefinition asm, string fullName, string label)
    {
        TypeDefinition t = FindType(asm, fullName);
        if (t == null) { Console.WriteLine("  [!] " + label + " converter not found"); return; }
        PatchConvertToAlwaysVisible(t, label);
    }

    // ===== Patch 3: biz - 切题讲评放开 SC13 限制 =====
    static void PatchBiz(string dll)
    {
        Console.WriteLine("[3/3] EasiCamera.Business.dll (simplified recognition)");
        string bak; AssemblyDefinition asm;
        try { asm = ReadAsm(dll, out bak); }
        catch (Exception e) { Console.WriteLine("  [!] " + e.Message); return; }

        TypeDefinition t = null;
        foreach (var mod in asm.Modules)
            foreach (var tt in mod.Types)
                if (tt.Name == "IntelligentIdentificationViewModel") { t = tt; break; }
        if (t == null) { Console.WriteLine("  [!] IntelligentIdentificationViewModel not found"); asm.Dispose(); return; }

        MethodDefinition upd = null;
        foreach (var m in t.Methods) if (m.Name == "UpdateSimplifiedIntelligentIdentificationStatus") { upd = m; break; }
        if (upd == null) { Console.WriteLine("  [!] method not found"); asm.Dispose(); return; }

        var instrs = upd.Body.Instructions;
        int patched = 0;
        for (int i = 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode != OpCodes.Call) continue;
            MethodReference mr = instrs[i].Operand as MethodReference;
            if (mr == null) continue;
            if (mr.DeclaringType.FullName != t.FullName) continue;
            if (mr.Parameters.Count != 1) continue;
            if (mr.Parameters[0].ParameterType.MetadataType != MetadataType.Boolean) continue;
            if (mr.ReturnType.MetadataType != MetadataType.Void) continue;
            var prev = instrs[i - 1];
            if (prev.OpCode == OpCodes.Ldloc_0)
            {
                var il = upd.Body.GetILProcessor();
                il.InsertBefore(prev, il.Create(OpCodes.Ldc_I4_1));
                il.Remove(prev);
                patched++;
                Console.WriteLine("  [+] ldloc.0 -> ldc.i4.1");
            }
            else if (prev.OpCode == OpCodes.Ldc_I4_1)
            {
                Console.WriteLine("  [*] already patched");
                patched++;
            }
        }
        if (patched == 0) { Console.WriteLine("  [!] no setter pattern found"); asm.Dispose(); return; }
        if (WriteBack(asm, dll) == 0) Console.WriteLine("  [+] OK");
    }

    // ===== 恢复 =====
    static void RestoreAll()
    {
        Console.WriteLine("[*] Restoring from backups...");
        string[] dlls = {
            Path.Combine(MainDir, "EasiCamera.Api.dll"),
            Path.Combine(MainDir, "EasiCamera.UI.dll"),
            Path.Combine(MainDir, "EasiCamera.Business.dll")
        };
        foreach (string dll in dlls)
        {
            string bak = dll + ".bak";
            if (File.Exists(bak))
            {
                File.Copy(bak, dll, true);
                Console.WriteLine("  restored " + Path.GetFileName(dll));
            }
            else
            {
                Console.WriteLine("  no backup: " + Path.GetFileName(dll));
            }
        }
    }
}
