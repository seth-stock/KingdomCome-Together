// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-153 -- see wo153.h.
#include "wo153.h"
#include "wo153_rules.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>

#include "fault_guard.h"
#include "hook_prologues.h"
#include "inline_hook.h"
#include "log.h"
#include "wo137.h"

namespace kcdmp::wo153 {
namespace {

// GUIModule's export (the decorated name is the anchor; its absence = not armed).
constexpr const char* kExport =
    "?EnqueueCutscene@C_CutscenePlayer@guimodule@wh@@UEAAXAEAV?$shared_ptr@VC_CutsceneConfiguration@guimodule@wh@@@std@@@Z";
constexpr auto& kPrologue = hookpro::kCutsceneEnqueue;   // hook_prologues.h: mov [rsp+18h],rbx; push rdi; sub rsp,20h; mov rbx,rcx; mov rdi,rdx

std::atomic<bool> g_armed{false};
std::atomic<uint8_t> g_mode{0};
std::atomic<uint16_t> g_windowMs{wo153rules::kDefaultWindowMs};
std::atomic<uint32_t> c_seen{0}, c_refused{0}, c_inApply{0}, c_inWindow{0};
const char* g_why = "not installed";

template <class T> bool rd(const void* base, size_t off, T* out) {
    KCDMP_FAULT_READ(site, "wo153::rd");
    return fault::guarded(site, [&] { *out = *reinterpret_cast<const T*>(static_cast<const char*>(base) + off); });
}
bool copy_cstr(const char* s, char* out, size_t n) {
    if (!s || n == 0) return false;
    KCDMP_FAULT_READ(site, "wo153::copy_cstr");
    if (fault::guarded(site, [&] { size_t i = 0; for (; i + 1 < n && s[i]; ++i) out[i] = s[i]; out[i] = 0; })) return true;
    out[0] = 0;
    return false;
}
// The class name behind an object's vptr (its RTTI locator; logs only).
bool rtti_name(const void* obj, char* out, size_t n) {
    out[0] = 0;
    const void* vptr = nullptr;
    if (!obj || !rd(obj, 0, &vptr) || !vptr) return false;
    const void* col = nullptr;
    if (!rd(vptr, static_cast<size_t>(-8), &col) || !col) return false;
    uint32_t sig = 0, tdRva = 0, selfRva = 0;
    if (!rd(col, 0, &sig) || sig != 1 || !rd(col, 12, &tdRva) || !rd(col, 20, &selfRva)) return false;
    const char* base = static_cast<const char*>(col) - selfRva;
    return copy_cstr(base + tdRva + 0x10, out, n);
}

// What the configuration looks like, for the log: the class of the object the shared_ptr holds, and the class of
// every pointer in its first 0x80 bytes that points at an object with RTTI (the cutscene itself is one of them).
// Logs only, and only for the first enqueues of a run.
void describe(const void* sp, char* out, size_t n) {
    out[0] = 0;
    const void* cfg = nullptr;
    if (!sp || !rd(sp, 0, &cfg) || !cfg) { std::snprintf(out, n, "cfg=?"); return; }
    char cls[96]; cls[0] = 0;
    rtti_name(cfg, cls, sizeof cls);
    int w = std::snprintf(out, n, "cfg=%s", cls[0] ? cls : "?");
    for (size_t off = 8; off <= 0x80 && w > 0 && static_cast<size_t>(w) + 60 < n; off += 8) {
        const void* p = nullptr;
        char c2[96];
        if (!rd(cfg, off, &p) || !p || reinterpret_cast<uintptr_t>(p) < 0x10000 || !rtti_name(p, c2, sizeof c2)) continue;
        w += std::snprintf(out + w, n - static_cast<size_t>(w), " +0x%zX=%.40s", off, c2);
    }
}

// rcx = the cutscene player, rdx = the shared_ptr<C_CutsceneConfiguration>& . True = refuse (return; the function is void).
bool __fastcall gate(void* /*self*/, void* sp) {
    const uint32_t seen = c_seen.fetch_add(1, std::memory_order_relaxed) + 1;
    const auto mode = wo153rules::mode_from(g_mode.load(std::memory_order_relaxed));
    const bool applying = wo137::apply_active();
    const uint64_t since = wo137::ms_since_apply_end();
    const bool refuse = wo153rules::refuse(mode, applying, since, g_windowMs.load(std::memory_order_relaxed), false);
    if (seen <= 24 || refuse) {   // the first enqueues of a run say what they look like; every refusal is logged (a scene is rare)
        char what[420];
        describe(sp, what, sizeof what);
        logf("WO153-GATE enqueue #%u mode=%u apply=%d since_apply_ms=%lld -> %s | %s", seen, static_cast<unsigned>(mode), applying ? 1 : 0,
             since == wo153rules::kNever ? -1ll : static_cast<long long>(since), refuse ? "REFUSED (not enqueued: this game's own copy of the host's scene is not played)" : "enqueued", what);
    }
    if (refuse) {
        c_refused.fetch_add(1, std::memory_order_relaxed);
        if (applying) c_inApply.fetch_add(1, std::memory_order_relaxed); else c_inWindow.fetch_add(1, std::memory_order_relaxed);
    }
    return refuse;
}

} // namespace

void install() {
    HMODULE gm = GetModuleHandleA("GUIModule.dll");
    if (!gm) { g_why = "GUIModule.dll not loaded"; logf("WO153-GATE NOT armed -- %s", g_why); return; }
    auto* fn = reinterpret_cast<uint8_t*>(GetProcAddress(gm, kExport));
    if (!fn) { g_why = "GUIModule does not export C_CutscenePlayer::EnqueueCutscene (another build?)"; logf("WO153-GATE NOT armed -- %s", g_why); return; }
    const char* why = nullptr;
    if (!inlinehook::install_gate(fn, kPrologue, sizeof kPrologue, &gate, &why)) {
        g_why = why ? why : "install failed";
        logf("WO153-GATE NOT armed -- %s", g_why);
        return;
    }
    g_armed = true; g_why = "";
    logf("WO153-GATE armed at GUIModule+0x%llX (C_CutscenePlayer::EnqueueCutscene): off until the agent turns it on for a joiner who chose to keep playing",
         static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(fn) - reinterpret_cast<uintptr_t>(gm)));
}

bool armed() { return g_armed.load(); }

void set_mode(uint8_t mode, uint16_t windowMs) {
    const uint8_t m = static_cast<uint8_t>(wo153rules::mode_from(mode));
    const uint16_t w = wo153rules::clamp_window(windowMs);
    const uint8_t was = g_mode.exchange(m);
    g_windowMs.store(w);
    if (was != m) logf("WO153-GATE mode %u -> %u (window %u ms; gate %s)", was, m, w, g_armed ? "armed" : g_why);
}

void on_disconnect() {
    if (g_mode.exchange(0) != 0) logf("WO153-GATE the pipe closed: mode back to 0 (nothing is refused)");
}

int status_text(char* out, int n) {
    return std::snprintf(out, n, "wo153 scene_gate=%s mode=%u window_ms=%u refused=%u (in_apply=%u in_window=%u) seen=%u",
                         g_armed ? "armed" : "off", g_mode.load(), g_windowMs.load(), c_refused.load(), c_inApply.load(), c_inWindow.load(), c_seen.load());
}

} // namespace kcdmp::wo153
