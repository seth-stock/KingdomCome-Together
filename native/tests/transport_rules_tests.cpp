// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Linux support: which transport the plugin serves (native/KCDMP/transport.h). Linked into KCDMP_NativeTests.
#include <cstdio>
#include <cstring>

#include "transport.h"

using namespace kcdmp::transport;

namespace {
int g_fail = 0, g_pass = 0;
#define TCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int transport_rules_tests(int* passed) {
    // Windows keeps its named pipe, whatever else is set about ports
    TCHECK(choose(nullptr, nullptr, false).kind == Kind::Pipe, "plain Windows is the pipe");
    TCHECK(choose(nullptr, "15000", false).kind == Kind::Pipe, "a port alone does not switch Windows to TCP");
    // Wine / Proton switches to TCP on its own, on the default port
    {
        const Choice c = choose(nullptr, nullptr, true);
        TCHECK(c.kind == Kind::Tcp && c.port == kDefaultTcpPort && kDefaultTcpPort == 14070, "Wine: TCP on 14070");
        TCHECK(c.why && std::strstr(c.why, "Wine"), "the log line says why");
    }
    // forcing wins over detection in both directions
    TCHECK(choose("tcp", nullptr, false).kind == Kind::Tcp, "forced TCP on Windows (how the TCP path is tested there)");
    TCHECK(choose("pipe", nullptr, true).kind == Kind::Pipe, "forced pipe under Wine");
    TCHECK(choose("nonsense", nullptr, false).kind == Kind::Pipe && choose("nonsense", nullptr, true).kind == Kind::Tcp, "an unknown value is ignored");
    // ports: valid ones are taken, bad ones fall back to the default, never to 0 or a privileged port
    TCHECK(choose("tcp", "15001", false).port == 15001, "an explicit port");
    TCHECK(choose("tcp", "1024", false).port == 1024 && choose("tcp", "65535", false).port == 65535, "the range ends");
    TCHECK(choose("tcp", "80", false).port == kDefaultTcpPort, "a privileged port is refused");
    TCHECK(choose("tcp", "70000", false).port == kDefaultTcpPort, "above 65535 is refused");
    TCHECK(choose("tcp", "abc", false).port == kDefaultTcpPort && choose("tcp", "", false).port == kDefaultTcpPort, "junk is refused");
    TCHECK(choose("tcp", "15000x", false).port == kDefaultTcpPort, "trailing junk is refused");
    TCHECK(parse_port(nullptr) == 0 && parse_port("0") == 0, "no port is 0");
    *passed = g_pass;
    return g_fail;
}
