// SPDX-License-Identifier: GPL-3.0-only
#include "combat_watch_rules.h"
#include <cstdio>
#include <limits>
#include <thread>
#include <atomic>

int combat_watch_tests(int* passed) {
    using namespace kcdmp::combatwatch;
    int ok = 0, fail = 0;
    auto check = [&](bool result, const char* name) {
        if (result) ++ok;else { ++fail;std::printf("FAIL combat watch: %s\n", name); }
    };
    Targets targets;
    int a = 0, b = 0;
    targets.note(7, &a, true);
    const Target first = targets.find(7);
    check(first && targets.live(first), "initial lifetime is live");
    targets.note(7, &a, true);
    check(targets.live(first), "same lifetime refresh retains outstanding hits");
    targets.note(7, nullptr, false);
    check(!targets.live(first), "release invalidates outstanding hits");
    targets.note(7, &a, true);
    check(!targets.live(first), "ID AND soul address reuse does not revive a hit");
    const Target second = targets.find(7);
    targets.note(7, &b, true);
    check(!targets.live(second) && targets.find(7).soul == &b, "replacement soul invalidates outstanding hits");
    targets.note(0, &a, true);
    check(!targets.find(0), "zero identity rejected");
    targets.note(7, nullptr, true);
    check(!targets.find(7), "null replacement clears target");
    for (uint32_t i = 1; i <= 20; ++i) targets.note(i, &a, true);
    uint32_t eids[16]{};void* souls[16]{};
    check(targets.list(eids, souls, 16) == 16 && !targets.find(20), "bounded target registry");
    check(targets.list(eids, souls, 2) == 2, "bounded list output");
    std::atomic<bool> complete{false}, bad{false};
    Targets concurrent;
    std::thread writer([&] {
        for (int i = 0; i < 20000; ++i) {
            concurrent.note(1, &a, true);concurrent.note(1, nullptr, false);
            concurrent.note(2, &b, true);concurrent.note(2, nullptr, false);
        }
        complete = true;
    });
    while (!complete.load()) {
        const auto t1 = concurrent.find(1), t2 = concurrent.find(2);
        if ((t1 && t1.soul != &a) || (t2 && t2.soul != &b)) bad = true;
    }
    writer.join();
    check(!bad, "hit hook cannot observe torn entity/soul identities");
    check(restored_amount(100, 80, 100, true, true) == 20, "verified restoration conserves damage");
    check(restored_amount(100, 80, 80, true, true) == 0, "accepted inert setter cannot forward damage");
    check(restored_amount(100, 80, 90, true, true) == 10, "partial restoration counts only restored health");
    check(restored_amount(100, 80, 120, true, true) == 20, "regeneration cannot inflate damage");
    check(restored_amount(100, 80, 70, true, true) == 0, "another loss cannot masquerade as restoration");
    check(restored_amount(100, 80, 100, false, true) == 0, "failed invocation cannot forward damage");
    check(restored_amount(100, 80, 100, true, false) == 0, "missing readback cannot forward damage");
    check(restored_amount(100, 80, std::numeric_limits<float>::quiet_NaN(), true, true) == 0, "nonfinite readback rejected");
    check(fully_restored(100, 100, 0.01f, true, true), "verified exact native state");
    check(!fully_restored(100, 90, 0.01f, true, true), "partial native state is uncertain");
    check(!fully_restored(100, 100, 0.01f, false, true), "faulted write is uncertain");
    *passed += ok;return fail;
}
