// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// How the agent reaches KCDMP.dll (docs/LINUX.md). The rules mirror native/KCDMP/transport.h, which decides what the DLL serves:
///   * Windows: the named pipe "kcdmp", as always.
///   * Linux (the game runs under Proton, the agent is a native Linux program): TCP 127.0.0.1:14070. A named pipe that lives inside
///     wineserver cannot be opened from a native process; a Wine socket is a real host socket.
///   * KCDMP_TRANSPORT=tcp|pipe forces either, KCDMP_TCP_PORT=&lt;1024-65535&gt; picks the port (the same variables the DLL reads, so one
///     setting in the launch script covers both ends).
/// </summary>
public readonly record struct PluginTransport(bool UseTcp, int Port, string Why)
{
    public const int DefaultTcpPort = 14070;

    /// <summary>Same range check as the DLL: 1024..65535, anything else means "not given".</summary>
    public static int ParsePort(string? s) =>
        int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v is >= 1024 and <= 65535 ? v : 0;

    public static PluginTransport Choose(string? envTransport, string? envPort, bool isWindows)
    {
        int port = ParsePort(envPort);
        if (port == 0) port = DefaultTcpPort;
        if (envTransport == "tcp")  return new(true, port, "KCDMP_TRANSPORT=tcp");
        if (envTransport == "pipe") return new(false, 0, "KCDMP_TRANSPORT=pipe");
        return isWindows ? new(false, 0, "Windows: the named pipe")
                         : new(true, port, "not Windows: the plugin runs under Wine/Proton and serves TCP");
    }

    public static PluginTransport FromEnvironment() =>
        Choose(Environment.GetEnvironmentVariable("KCDMP_TRANSPORT"), Environment.GetEnvironmentVariable("KCDMP_TCP_PORT"), OperatingSystem.IsWindows());
}
