// EasiCamera 品牌限制解除补丁
// 通过 Harmony 在运行时把 CameraExtension.IsSeewoCamera(DsDevice) 强制返回 true
//
// 两种触发方式：
//   1. CLR Hosting ExecuteInDefaultAppDomain 调用 Init(IntPtr,int) -> int
//   2. AppDomain.CreateInstanceFromAndUnwrap 创建实例，无参构造函数触发 DoPatch()
//      （需要继承 MarshalByRefObject，否则跨 AppDomain 返回会序列化失败）
using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace EasiCameraPatch
{
    // 继承 MarshalByRefObject，使 CreateInstanceFromAndUnwrap 可跨 AppDomain 返回透明代理
    public class PatchEntry : MarshalByRefObject
    {
        private static bool _patched;
        private static readonly string LogPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "EasiCameraPatch.log");

        // 方式 1：ICLRRuntimeHost::ExecuteInDefaultAppDomain 调用
        // 签名必须为 public static int MethodName(string arg) -> int
        public static int Run(string arg)
        {
            DoPatch();
            return 0;
        }

        // 旧方式保留：构造函数触发（用于 CreateInstanceFromAndUnwrap 路径）
        public PatchEntry()
        {
            DoPatch();
        }

        // 旧方式保留：CLR Hosting ExecuteInDefaultAppDomain 调用（签名不匹配，已弃用）
        public static int Init(IntPtr args, int argLength)
        {
            DoPatch();
            return 0;
        }

        private static void DoPatch()
        {
            try
            {
                if (_patched)
                {
                    Log("already patched, skip");
                    return;
                }

                // 等待目标程序集加载（带重试，因为注入时机可能早于程序集加载）
                Type camExt = null;
                Type dsDevice = null;
                for (int i = 0; i < 30; i++)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (camExt == null) camExt = asm.GetType("EasiCamera.CameraExtension");
                        if (dsDevice == null) dsDevice = asm.GetType("DirectShowLib.DsDevice");
                    }
                    if (camExt != null && dsDevice != null) break;
                    System.Threading.Thread.Sleep(500);
                }

                if (camExt == null)
                {
                    Log("FAIL: type EasiCamera.CameraExtension not found");
                    return;
                }
                if (dsDevice == null)
                {
                    Log("FAIL: type DirectShowLib.DsDevice not found");
                    return;
                }

                var target = camExt.GetMethod("IsSeewoCamera",
                    BindingFlags.Public | BindingFlags.Static,
                    null, new[] { dsDevice }, null);
                if (target == null)
                {
                    Log("FAIL: method IsSeewoCamera(DsDevice) not found");
                    return;
                }

                var harmony = new HarmonyLib.Harmony("easicamera.patch.v1");
                var prefix = typeof(PatchEntry).GetMethod("SeewoPrefix",
                                BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _patched = true;
                Log("patched OK: IsSeewoCamera(DsDevice) -> true");
            }
            catch (Exception e)
            {
                Log("EXCEPTION: " + e);
            }
        }

        // Prefix：跳过原方法，强制返回 true
        private static bool SeewoPrefix(ref bool __result)
        {
            __result = true;
            return false;
        }

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
