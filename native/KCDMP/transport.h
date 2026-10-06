// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
//
// Which transport the plugin serves its agent over (docs/LINUX.md). Pure: no Windows headers, so the rules are unit-tested
// (native/tests/transport_rules_tests.cpp) and pipe_server.cpp only applies the answer.
//
//   * Windows: the named pipe \\.\pipe\kcdmp, as always.
//   * Wine / Proton (the game on Linux): a TCP listener on 127.0.0.1. A named pipe lives inside wineserver, which a native Linux
//     process cannot open, while a Wine socket is a real host socket, so the agent (a native Linux program) connects to it.
//   * KCDMP_TRANSPORT=tcp|pipe forces either; KCDMP_TCP_PORT=<1024-65535> picks the port.
#pragma once

#include <cstdint>
#include <cstdlib>
#include <cstring>

namespace kcdmp::transport {

enum class Kind : uint8_t { Pipe, Tcp };

constexpr uint16_t kDefaultTcpPort = 14070;

struct Choice {
    Kind kind;
    uint16_t port;          // meaningful for Tcp
    const char* why;        // a sentence for the log
};

// Parses a port; 0 = not a usable port (empty, not a number, below 1024 or above 65535).
inline uint16_t parse_port(const char* s) {
    if (!s || !*s) return 0;
    char* end = nullptr;
    const unsigned long v = std::strtoul(s, &end, 10);
    if (end == s || *end != '\0' || v < 1024 || v > 65535) return 0;
    return static_cast<uint16_t>(v);
}

inline Choice choose(const char* env_transport, const char* env_port, bool under_wine) {
    const uint16_t port = [&] { const uint16_t p = parse_port(env_port); return p ? p : kDefaultTcpPort; }();
    if (env_transport && std::strcmp(env_transport, "tcp") == 0)  return { Kind::Tcp, port, "KCDMP_TRANSPORT=tcp" };
    if (env_transport && std::strcmp(env_transport, "pipe") == 0) return { Kind::Pipe, 0, "KCDMP_TRANSPORT=pipe" };
    if (under_wine) return { Kind::Tcp, port, "running under Wine/Proton: a named pipe is not reachable from Linux" };
    return { Kind::Pipe, 0, "Windows: the named pipe" };
}

} // namespace kcdmp::transport
