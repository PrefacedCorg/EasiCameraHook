// Bootstrap DLL：被注入到 EasiCamera.exe 进程后，
// 通过 ICLRRuntimeHost::ExecuteInDefaultAppDomain 调用补丁 DLL 中的
// public static int Run(string) 方法，触发 Harmony patch
//
// 必须编译为 x86（EasiCamera 是 32 位）
#define INITGUID
#include <initguid.h>
#include <windows.h>
#include <objbase.h>
#include <oaidl.h>
#include <oleauto.h>
#include <stdio.h>

// MinGW 头文件中 IID_NULL -> GUID_NULL，但 GUID_NULL 缺定义，手动声明
EXTERN_C const IID GUID_NULL = { 0, 0, 0, { 0, 0, 0, 0, 0, 0, 0, 0 } };

// ====== CLR Hosting GUID ======
static const GUID CLSID_CLRMetaHost =
    { 0x9280188d, 0xe8e, 0x4867, { 0xb3, 0xc, 0x7f, 0xa8, 0x38, 0x84, 0xe8, 0xde } };
static const GUID IID_ICLRMetaHost =
    { 0xd332db9e, 0xb9b3, 0x4125, { 0x82, 0x7, 0xa1, 0x48, 0x84, 0xf5, 0x32, 0x16 } };
static const GUID IID_ICLRRuntimeInfo =
    { 0xbd39d1d2, 0xba2f, 0x486a, { 0x89, 0xb0, 0xb4, 0xb0, 0xcb, 0x46, 0x68, 0x91 } };
// ICLRRuntimeHost (.NET 4 推荐接口，ExecuteInDefaultAppDomain 所在)
static const GUID CLSID_CLRRuntimeHost =
    { 0x90f1a06e, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
static const GUID IID_ICLRRuntimeHost =
    { 0x90f1a06c, 0x7712, 0x4762, { 0x86, 0xb5, 0x7a, 0x5e, 0xba, 0x6b, 0xdb, 0x02 } };
// ICorRuntimeHost (.NET 2.0 旧接口，作为 fallback)
static const GUID CLSID_CorRuntimeHost =
    { 0xcb2f6723, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };
static const GUID IID_ICorRuntimeHost =
    { 0xcb2f6722, 0xab3a, 0x11d2, { 0x9c, 0x40, 0x00, 0xc0, 0x4f, 0xa3, 0x0a, 0x3e } };

typedef HRESULT (__stdcall *PFN_CLRCreateInstance)(REFCLSID, REFIID, LPVOID*);
typedef HRESULT (__stdcall *PFN_CorBindToRuntimeEx)(
    LPCWSTR, LPCWSTR, DWORD, REFCLSID, REFIID, LPVOID*);

// ====== 自定义 CLR Hosting 接口（C 风格 vtable，this 作为第一个栈参数） ======
struct MyICLRMetaHostVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);
    ULONG   (__stdcall *AddRef)(void*);
    ULONG   (__stdcall *Release)(void*);
    HRESULT (__stdcall *GetRuntime)(void*, LPCWSTR, REFIID, LPVOID*);        // [3]
    void* pad4;  // GetVersionFromFile                                     // [4]
    void* pad5;  // EnumerateInstalledRuntimes                             // [5]
    HRESULT (__stdcall *EnumerateLoadedRuntimes)(void*, HANDLE, IUnknown**); // [6]
    void* pad7; void* pad8; void* pad9;
};
struct MyICLRMetaHost { MyICLRMetaHostVtbl* vtbl; };

// IEnumUnknown（用于枚举 EnumerateLoadedRuntimes 返回的运行时）
struct MyIEnumUnknownVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);              // [0]
    ULONG   (__stdcall *AddRef)(void*);                                       // [1]
    ULONG   (__stdcall *Release)(void*);                                      // [2]
    HRESULT (__stdcall *Next)(void*, ULONG, IUnknown**, ULONG*);             // [3]
    void* pad4; void* pad5; void* pad6;
};
struct MyIEnumUnknown { MyIEnumUnknownVtbl* vtbl; };

struct MyICLRRuntimeInfoVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);
    ULONG   (__stdcall *AddRef)(void*);
    ULONG   (__stdcall *Release)(void*);
    void* pad3; void* pad4; void* pad5; void* pad6; void* pad7; void* pad8;
    HRESULT (__stdcall *GetInterface)(void*, REFCLSID, REFIID, LPVOID*);  // [9]
    void* pad10; void* pad11; void* pad12; void* pad13; void* pad14;
};
struct MyICLRRuntimeInfo { MyICLRRuntimeInfoVtbl* vtbl; };

// ICLRRuntimeHost vtable（按 metahost.h 顺序）
// ExecuteInDefaultAppDomain 在 [9]
struct MyICLRRuntimeHostVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);   // [0]
    ULONG   (__stdcall *AddRef)(void*);                            // [1]
    ULONG   (__stdcall *Release)(void*);                           // [2]
    HRESULT (__stdcall *Start)(void*);                             // [3]
    void* pad4;  // Stop                                          // [4]
    void* pad5;  // SetHostControl                                // [5]
    void* pad6;  // GetCLRControl                                 // [6]
    void* pad7;  // GetCurrentAppDomainId                         // [7]
    void* pad8;  // ExecuteApplication                            // [8]
    // [9] ExecuteInDefaultAppDomain(assemblyPath, typeName, methodName, arg, out retval)
    HRESULT (__stdcall *ExecuteInDefaultAppDomain)(
        void*, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, DWORD*);
};
struct MyICLRRuntimeHost { MyICLRRuntimeHostVtbl* vtbl; };

// ICorRuntimeHost 完整 vtable（按 corhost.h 顺序，作为 fallback）
struct MyICorRuntimeHostVtbl {
    HRESULT (__stdcall *QueryInterface)(void*, REFIID, void**);                  // [0]
    ULONG   (__stdcall *AddRef)(void*);                                           // [1]
    ULONG   (__stdcall *Release)(void*);                                          // [2]
    HRESULT (__stdcall *CreateDomain)(void*, LPCWSTR, IUnknown**, IUnknown**);    // [3]
    HRESULT (__stdcall *GetDefaultDomain)(void*, IUnknown**);                     // [4]
    void* pad5; void* pad6; void* pad7; void* pad8; void* pad9; void* pad10;
    void* pad11; void* pad12; void* pad13; void* pad14; void* pad15; void* pad16;
    void* pad17;
    HRESULT (__stdcall *Start)(void*);                                            // [18]
    void* pad19;                                                                  // [19]
};
struct MyICorRuntimeHost { MyICorRuntimeHostVtbl* vtbl; };

// ====== 全局配置 ======
static wchar_t g_patchDllPath[MAX_PATH] =
    L"C:\\Users\\PrefacedCorg\\桌面\\spzt\\Patch\\build\\EasiCameraPatch.dll";
static wchar_t g_logPath[MAX_PATH] =
    L"C:\\Users\\PrefacedCorg\\桌面\\spzt\\Patch\\build\\bootstrap.log";

static void LogMsg(const wchar_t* msg)
{
    HANDLE hFile = CreateFileW(g_logPath, FILE_APPEND_DATA, FILE_SHARE_READ,
                               NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (hFile != INVALID_HANDLE_VALUE) {
        SYSTEMTIME st; GetLocalTime(&st);
        wchar_t buf[2048];
        int n = swprintf_s(buf, 2048, L"[%04d-%02d-%02d %02d:%02d:%02d] %s\r\n",
                           st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, msg);
        DWORD written;
        WriteFile(hFile, buf, (DWORD)(n * sizeof(wchar_t)), &written, NULL);
        CloseHandle(hFile);
    }
}

// ====== 方式 1：ICLRRuntimeHost::ExecuteInDefaultAppDomain ======
// 调用 EasiCameraPatch.PatchEntry.Run(string) -> int
static HRESULT TryExecuteInDefaultAppDomain(MyICLRRuntimeInfo* pRuntimeInfo)
{
    LogMsg(L"--- Trying ICLRRuntimeHost::ExecuteInDefaultAppDomain ---");

    MyICLRRuntimeHost* pHost = NULL;
    HRESULT hr = pRuntimeInfo->vtbl->GetInterface(pRuntimeInfo,
        CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (LPVOID*)&pHost);
    if (FAILED(hr) || !pHost) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: GetInterface(ICLRRuntimeHost) hr=0x%08X", (unsigned)hr);
        LogMsg(m);
        return hr;
    }
    LogMsg(L"GetInterface(ICLRRuntimeHost) OK");

    // 调用 Start。文档说明：若 CLR 已运行，返回 S_FALSE。
    // 之前 ICorRuntimeHost::Start 会崩溃是因为 vtable 索引错误。
    // ICLRRuntimeHost 的 vtable 更简单，Start 在 [3]，索引正确。
    hr = pHost->vtbl->Start(pHost);
    {
        wchar_t m[128]; swprintf_s(m, 128, L"ICLRRuntimeHost::Start hr=0x%08X", (unsigned)hr);
        LogMsg(m);
    }
    // 不管 Start 返回 S_OK 还是 S_FALSE，都继续尝试 ExecuteInDefaultAppDomain
    // 如果 Start 真的启动了新 CLR，ExecuteInDefaultAppDomain 会在新 CLR 里执行
    //（虽然不理想，但至少不会崩溃）

    DWORD retval = 0;
    hr = pHost->vtbl->ExecuteInDefaultAppDomain(pHost,
        g_patchDllPath,
        L"EasiCameraPatch.PatchEntry",
        L"Run",
        L"",
        &retval);

    if (FAILED(hr)) {
        wchar_t m[256];
        swprintf_s(m, 256, L"FAIL: ExecuteInDefaultAppDomain hr=0x%08X retval=%u",
                   (unsigned)hr, retval);
        LogMsg(m);
    } else {
        wchar_t m[128];
        swprintf_s(m, 128, L"ExecuteInDefaultAppDomain OK retval=%u", retval);
        LogMsg(m);
    }
    return hr;
}

// ====== 方式 2（fallback）：ICorRuntimeHost + IDispatch ======
// 通过 AppDomain.CreateInstanceFromAndUnwrap 触发 PatchEntry 构造函数
static HRESULT TryCorRuntimeHostIDispatch(MyICLRRuntimeInfo* pRuntimeInfo)
{
    LogMsg(L"--- Trying ICorRuntimeHost + IDispatch fallback ---");

    MyICorRuntimeHost* pHost = NULL;
    HRESULT hr = pRuntimeInfo->vtbl->GetInterface(pRuntimeInfo,
        CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (LPVOID*)&pHost);
    if (FAILED(hr) || !pHost) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: GetInterface(CorRuntimeHost) hr=0x%08X", (unsigned)hr);
        LogMsg(m);
        return hr;
    }
    LogMsg(L"GetInterface(CorRuntimeHost) OK");

    IUnknown* pAppDomainUnk = NULL;
    LogMsg(L"Calling GetDefaultDomain...");
    hr = pHost->vtbl->GetDefaultDomain(pHost, &pAppDomainUnk);
    if (FAILED(hr) || !pAppDomainUnk) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: GetDefaultDomain hr=0x%08X", (unsigned)hr);
        LogMsg(m);
        return hr;
    }
    LogMsg(L"GetDefaultDomain OK");

    IDispatch* pAppDomain = NULL;
    hr = pAppDomainUnk->QueryInterface(IID_IDispatch, (void**)&pAppDomain);
    if (FAILED(hr) || !pAppDomain) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: QI(IDispatch) hr=0x%08X", (unsigned)hr);
        LogMsg(m);
        return hr;
    }
    LogMsg(L"AppDomain IDispatch OK");

    // IDispatch::Invoke 调用 CreateInstanceFromAndUnwrap(assemblyPath, typeName)
    DISPID dispid = 0;
    LPOLESTR methodName = (LPOLESTR)L"CreateInstanceFromAndUnwrap";
    hr = pAppDomain->GetIDsOfNames(IID_NULL, &methodName, 1, 0, &dispid);
    if (FAILED(hr)) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: GetIDsOfNames hr=0x%08X", (unsigned)hr);
        LogMsg(m);
        return hr;
    }
    LogMsg(L"GetIDsOfNames OK");

    VARIANT args[2];
    VariantInit(&args[0]); VariantInit(&args[1]);
    args[1].vt = VT_BSTR;
    args[1].bstrVal = SysAllocString(g_patchDllPath);
    args[0].vt = VT_BSTR;
    args[0].bstrVal = SysAllocString(L"EasiCameraPatch.PatchEntry");

    DISPPARAMS params;
    params.rgvarg = args;
    params.rgdispidNamedArgs = NULL;
    params.cArgs = 2;
    params.cNamedArgs = 0;

    VARIANT result; VariantInit(&result);
    EXCEPINFO excep; ZeroMemory(&excep, sizeof(excep));
    UINT argErr = 0;

    hr = pAppDomain->Invoke(dispid, IID_NULL, 0, DISPATCH_METHOD,
                            &params, &result, &excep, &argErr);
    SysFreeString(args[0].bstrVal);
    SysFreeString(args[1].bstrVal);

    if (FAILED(hr)) {
        wchar_t m[256];
        swprintf_s(m, 256, L"FAIL: Invoke hr=0x%08X scode=%u argErr=%u",
                   (unsigned)hr, excep.scode, argErr);
        LogMsg(m);
    } else {
        LogMsg(L"Invoke OK - patch triggered via constructor");
    }
    VariantClear(&result);
    return hr;
}

extern "C" __declspec(dllexport)
DWORD WINAPI RunPatch(LPVOID lpParam)
{
    LogMsg(L"RunPatch start");

    // APARTMENTTHREADED 与 .NET 主线程 STA 匹配
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    LogMsg(L"CoInitializeEx done");

    HMODULE hMscoree = LoadLibraryW(L"mscoree.dll");
    if (!hMscoree) { LogMsg(L"FAIL: LoadLibrary mscoree.dll"); return 10; }
    PFN_CLRCreateInstance pfnCLRCreateInstance =
        (PFN_CLRCreateInstance)GetProcAddress(hMscoree, "CLRCreateInstance");
    if (!pfnCLRCreateInstance) { LogMsg(L"FAIL: GetProcAddress CLRCreateInstance"); return 11; }

    MyICLRMetaHost* pMetaHost = NULL;
    MyICLRRuntimeInfo* pRuntimeInfo = NULL;
    MyIEnumUnknown* pEnum = NULL;
    IUnknown* pUnkRuntime = NULL;
    ULONG fetched = 0;
    DWORD ret = 1;

    HRESULT hr = pfnCLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&pMetaHost);
    if (FAILED(hr) || !pMetaHost) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: CLRCreateInstance hr=0x%08X", (unsigned)hr);
        LogMsg(m); goto cleanup;
    }
    LogMsg(L"CLRCreateInstance OK");

    // 使用 EnumerateLoadedRuntimes 获取已加载的 CLR（而不是 GetRuntime，
    // 因为 GetRuntime 返回的 ICLRRuntimeInfo 可能不连接已运行的 CLR）
    // 注意：必须传入 GetCurrentProcess()，NULL 会返回 E_HANDLE
    hr = pMetaHost->vtbl->EnumerateLoadedRuntimes(pMetaHost, GetCurrentProcess(), (IUnknown**)&pEnum);
    if (FAILED(hr) || !pEnum) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: EnumerateLoadedRuntimes hr=0x%08X", (unsigned)hr);
        LogMsg(m); goto cleanup;
    }
    LogMsg(L"EnumerateLoadedRuntimes OK");

    hr = pEnum->vtbl->Next(pEnum, 1, &pUnkRuntime, &fetched);
    if (FAILED(hr) || fetched == 0 || !pUnkRuntime) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: Enum Next hr=0x%08X fetched=%u", (unsigned)hr, fetched);
        LogMsg(m); goto cleanup;
    }
    LogMsg(L"Got loaded runtime IUnknown");

    // QI for ICLRRuntimeInfo
    hr = pUnkRuntime->QueryInterface(IID_ICLRRuntimeInfo, (void**)&pRuntimeInfo);
    if (FAILED(hr) || !pRuntimeInfo) {
        wchar_t m[128]; swprintf_s(m, 128, L"FAIL: QI(ICLRRuntimeInfo) hr=0x%08X", (unsigned)hr);
        LogMsg(m); goto cleanup;
    }
    LogMsg(L"QI(ICLRRuntimeInfo) OK - got actually-loaded runtime");

    // 主路径：ICLRRuntimeHost::ExecuteInDefaultAppDomain
    hr = TryExecuteInDefaultAppDomain(pRuntimeInfo);
    if (SUCCEEDED(hr)) {
        ret = 0;
    }

cleanup:
    // 故意不释放接口、不 FreeLibrary(mscoree)：CLR 已被宿主进程使用，
    // 释放/卸载可能破坏运行时状态导致崩溃。进程退出时由 OS 统一回收。
    LogMsg(L"RunPatch end");
    return ret;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD dwReason, LPVOID lpReserved)
{
    if (dwReason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        LogMsg(L"DLL_PROCESS_ATTACH, spawning RunPatch thread");
        HANDLE hThread = CreateThread(NULL, 0, RunPatch, NULL, 0, NULL);
        if (hThread) CloseHandle(hThread);
    }
    return TRUE;
}
