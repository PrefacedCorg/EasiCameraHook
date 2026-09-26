// 注入器：通过 CreateRemoteThread + LoadLibraryW 把 Bootstrap.dll 注入 EasiCamera.exe
// 必须编译为 x86
//
// 用法：Injector.exe <PID> [bootstrap.dll路径]
//   PID              EasiCamera.exe 的进程 ID
//   bootstrap.dll路径 默认为当前目录下的 Bootstrap.dll
#include <windows.h>
#include <psapi.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <string>

#pragma comment(lib, "psapi.lib")

static int InjectDll(DWORD pid, const wchar_t* dllPath)
{
    wprintf(L"[*] Opening process %u ...\n", pid);
    HANDLE hProc = OpenProcess(
        PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
        FALSE, pid);
    if (!hProc) {
        wprintf(L"[!] OpenProcess failed: %lu\n", GetLastError());
        return 1;
    }

    // 校验 DLL 路径为绝对路径
    wchar_t absPath[MAX_PATH];
    GetFullPathNameW(dllPath, MAX_PATH, absPath, NULL);
    wprintf(L"[*] Bootstrap DLL: %s\n", absPath);

    if (GetFileAttributesW(absPath) == INVALID_FILE_ATTRIBUTES) {
        wprintf(L"[!] Bootstrap DLL not found\n");
        CloseHandle(hProc);
        return 2;
    }

    // 1. 在目标进程分配内存写入 DLL 路径
    SIZE_T pathBytes = (wcslen(absPath) + 1) * sizeof(wchar_t);
    LPVOID remoteMem = VirtualAllocEx(hProc, NULL, pathBytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remoteMem) {
        wprintf(L"[!] VirtualAllocEx failed: %lu\n", GetLastError());
        CloseHandle(hProc);
        return 3;
    }
    WriteProcessMemory(hProc, remoteMem, absPath, pathBytes, NULL);
    wprintf(L"[*] Wrote DLL path to remote memory @ 0x%p\n", remoteMem);

    // 2. 远程调用 LoadLibraryW 加载 Bootstrap.dll
    HMODULE hK32 = GetModuleHandleW(L"kernel32.dll");
    FARPROC pLoadLibrary = GetProcAddress(hK32, "LoadLibraryW");
    wprintf(L"[*] LoadLibraryW @ 0x%p\n", pLoadLibrary);

    HANDLE hThread = CreateRemoteThread(
        hProc, NULL, 0,
        (LPTHREAD_START_ROUTINE)pLoadLibrary,
        remoteMem, 0, NULL);
    if (!hThread) {
        wprintf(L"[!] CreateRemoteThread (LoadLibrary) failed: %lu\n", GetLastError());
        VirtualFreeEx(hProc, remoteMem, 0, MEM_RELEASE);
        CloseHandle(hProc);
        return 4;
    }
    WaitForSingleObject(hThread, 10000);
    wprintf(L"[+] Bootstrap.dll loaded into target\n");

    // 3. 设置补丁 DLL 路径（通过导出函数 SetPatchPath）
    // 由于跨进程调用带参数的导出函数较复杂，这里采用"Bootstrap 内部默认路径"方案
    // 如需自定义路径，可在注入前修改 Bootstrap.cpp 的 g_patchDllPath 重新编译

    VirtualFreeEx(hProc, remoteMem, 0, MEM_RELEASE);
    CloseHandle(hThread);
    CloseHandle(hProc);

    wprintf(L"[+] Injection done. Check bootstrap.log & EasiCameraPatch.log\n");
    return 0;
}

// 自动查找 EasiCamera.exe 进程
static DWORD FindEasiCamera()
{
    HANDLE hSnap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32W pe;
    pe.dwSize = sizeof(pe);
    DWORD pid = 0;
    if (Process32FirstW(hSnap, &pe)) {
        do {
            if (_wcsicmp(pe.szExeFile, L"EasiCamera.exe") == 0) {
                pid = pe.th32ProcessID;
                break;
            }
        } while (Process32NextW(hSnap, &pe));
    }
    CloseHandle(hSnap);
    return pid;
}

int wmain(int argc, wchar_t** argv)
{
    wprintf(L"=== EasiCamera Injector ===\n");

    DWORD pid = 0;
    const wchar_t* dllPath = L"Bootstrap.dll";

    if (argc >= 2) {
        pid = (DWORD)_wtol(argv[1]);
    } else {
        wprintf(L"[*] No PID given, searching for EasiCamera.exe ...\n");
        pid = FindEasiCamera();
    }
    if (argc >= 3) {
        dllPath = argv[2];
    }

    if (pid == 0) {
        wprintf(L"[!] EasiCamera.exe not found. Please start it first.\n");
        return 10;
    }
    wprintf(L"[+] Target PID: %u\n", pid);
    return InjectDll(pid, dllPath);
}
