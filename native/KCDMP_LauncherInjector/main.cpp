// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// KCDMP_LauncherInjector.exe -- loads KCDMP.dll into a running game process.
//
// Command line is fixed by KCDMP_launcher, which already invokes it as:
//     KCDMP_LauncherInjector.exe --pid <pid> --dll "<absolute path>"
// See KCDMP_launcher/Pages/Home.razor.cs. Do not change the argument shape
// without changing the launcher.
//
// Linux support (docs/LINUX.md): the same program runs inside the game's Proton prefix, where the game's Windows process id is not
// something a Linux script knows. So, in addition:
//     --process KingdomCome.exe      find the game by its image name instead of --pid
//     --module WHGame.dll            wait until that module is loaded in the process (the engine; injecting earlier breaks the plugin)
//     --wait <seconds>               how long to wait for the process (and the module) to appear; default 0 = they must already be there
// A path may be given Windows-style (Z:\home\...) or Unix-style (/home/...): Wine maps both.
//
// Classic CreateRemoteThread(LoadLibraryA) injection. That is sufficient here
// because KCD2 has no anti-cheat: no EAC, BattlEye, Denuvo or packer in either
// build (see docs/NATIVE-PLUGIN-findings.md). Nothing exotic is warranted.

#include <windows.h>
#include <tlhelp32.h>

#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

namespace {

int fail(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vfprintf(stderr, fmt, args);
    va_end(args);
    fputc('\n', stderr);
    return 1;
}

// LoadLibraryA lives in kernel32, which is mapped at the same base in every
// process on a given boot, so our own address is valid in the target. This is
// the standard assumption and holds for same-architecture, same-session
// injection; it would not hold across a WOW64 boundary, and both sides here are
// x64.
bool is_wow64(HANDLE process) {
    BOOL wow = FALSE;
    IsWow64Process(process, &wow);
    return wow == TRUE;
}

// First process whose image name is `name` (case-insensitive), or 0.
DWORD find_pid(const char* name) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;
    PROCESSENTRY32 pe{};
    pe.dwSize = sizeof(pe);
    DWORD found = 0;
    if (Process32First(snap, &pe)) {
        do {
            if (_stricmp(pe.szExeFile, name) == 0) { found = pe.th32ProcessID; break; }
        } while (Process32Next(snap, &pe));
    }
    CloseHandle(snap);
    return found;
}

bool has_module(DWORD pid, const char* name) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return false;
    MODULEENTRY32 me{};
    me.dwSize = sizeof(me);
    bool found = false;
    if (Module32First(snap, &me)) {
        do {
            if (_stricmp(me.szModule, name) == 0) { found = true; break; }
        } while (Module32Next(snap, &me));
    }
    CloseHandle(snap);
    return found;
}

} // namespace

int main(int argc, char** argv) {
    DWORD       pid = 0;
    std::string dll, procName, module;
    unsigned    waitSeconds = 0;

    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--pid") == 0 && i + 1 < argc) {
            pid = static_cast<DWORD>(std::strtoul(argv[++i], nullptr, 10));
        } else if (std::strcmp(argv[i], "--dll") == 0 && i + 1 < argc) {
            dll = argv[++i];
        } else if (std::strcmp(argv[i], "--process") == 0 && i + 1 < argc) {
            procName = argv[++i];
        } else if (std::strcmp(argv[i], "--module") == 0 && i + 1 < argc) {
            module = argv[++i];
        } else if (std::strcmp(argv[i], "--wait") == 0 && i + 1 < argc) {
            waitSeconds = static_cast<unsigned>(std::strtoul(argv[++i], nullptr, 10));
        }
    }

    if ((pid == 0 && procName.empty()) || dll.empty()) {
        return fail("usage: KCDMP_LauncherInjector.exe (--pid <pid> | --process <name.exe> [--module <name.dll>] [--wait <seconds>]) --dll <path>");
    }

    if (pid == 0) {   // by name: wait for the process, then for the engine module inside it
        const ULONGLONG deadline = GetTickCount64() + static_cast<ULONGLONG>(waitSeconds) * 1000ull;
        for (;;) {
            pid = find_pid(procName.c_str());
            if (pid != 0 && (module.empty() || has_module(pid, module.c_str()))) break;
            if (GetTickCount64() >= deadline) {
                if (pid == 0) return fail("process %s not found within %u s", procName.c_str(), waitSeconds);
                return fail("process %s is running but %s is not loaded in it within %u s", procName.c_str(), module.c_str(), waitSeconds);
            }
            Sleep(500);
        }
        if (!module.empty()) Sleep(1500);   // the engine's module is mapped before its init has finished: give it a moment, as the launcher does
        printf("found %s as pid %lu\n", procName.c_str(), pid);
    }

    // Resolve to an absolute path here rather than in the target: the game's
    // working directory is its own Bin folder, so a relative path would be
    // resolved against the wrong root and LoadLibrary would fail with a
    // misleading "module not found".
    char full[MAX_PATH]{};
    if (!GetFullPathNameA(dll.c_str(), MAX_PATH, full, nullptr)) {
        return fail("cannot resolve path: %s", dll.c_str());
    }
    if (GetFileAttributesA(full) == INVALID_FILE_ATTRIBUTES) {
        return fail("DLL not found: %s", full);
    }

    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                                 PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                                 FALSE, pid);
    if (!process) {
        return fail("OpenProcess(%lu) failed: %lu (run as the same user as the game)", pid, GetLastError());
    }

    if (is_wow64(process)) {
        CloseHandle(process);
        return fail("target is 32-bit; KCDMP.dll is x64");
    }

    const SIZE_T bytes = std::strlen(full) + 1;
    void* remote = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        CloseHandle(process);
        return fail("VirtualAllocEx failed: %lu", GetLastError());
    }

    if (!WriteProcessMemory(process, remote, full, bytes, nullptr)) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return fail("WriteProcessMemory failed: %lu", GetLastError());
    }

    auto loader = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        GetProcAddress(GetModuleHandleA("kernel32.dll"), "LoadLibraryA"));

    HANDLE thread = CreateRemoteThread(process, nullptr, 0, loader, remote, 0, nullptr);
    if (!thread) {
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return fail("CreateRemoteThread failed: %lu", GetLastError());
    }

    WaitForSingleObject(thread, 15'000);

    // The remote thread returns LoadLibraryA's HMODULE truncated to 32 bits.
    // Zero means the load failed inside the target; non-zero only means the
    // module mapped, not that the plugin is healthy -- check kcdmp-native.log
    // next to the DLL for that.
    DWORD result = 0;
    GetExitCodeThread(thread, &result);

    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    CloseHandle(process);

    if (result == 0) {
        return fail("LoadLibrary failed inside pid %lu (missing dependency, or wrong architecture)", pid);
    }

    printf("injected %s into pid %lu\n", full, pid);
    return 0;
}
