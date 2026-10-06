// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Net;
using System.Net.Sockets;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// Linux support (docs/LINUX.md): the agent reaches KCDMP.dll over TCP loopback when the game runs under Proton.
/// (synthetic) -- a fake plugin speaking the real frame format on a real socket; no game, no Wine. What this cannot prove is that the
/// DLL's own listener works inside Wine; that is native-side and was exercised only on Windows with KCDMP_TRANSPORT=tcp.
/// </summary>
public class LinuxTransportTests
{
    [Theory]
    [InlineData(null, null, true, false, 0)]            // Windows: pipe
    [InlineData(null, null, false, true, 14070)]        // Linux: tcp, default port
    [InlineData("tcp", "15000", true, true, 15000)]     // forced on Windows (how the TCP path is tested there)
    [InlineData("pipe", null, false, false, 0)]         // forced on Linux
    [InlineData(null, "15001", false, true, 15001)]
    [InlineData("tcp", "80", false, true, 14070)]       // below 1024: ignored
    [InlineData("tcp", "99999", false, true, 14070)]    // above 65535: ignored
    [InlineData("tcp", "abc", false, true, 14070)]
    [InlineData("bogus", null, true, false, 0)]         // unknown value: the OS default
    [InlineData("bogus", null, false, true, 14070)]
    public void Choice_follows_the_rules_the_plugin_uses(string? transport, string? port, bool windows, bool tcp, int expectedPort)
    {
        var c = PluginTransport.Choose(transport, port, windows);
        Assert.Equal(tcp, c.UseTcp);
        Assert.Equal(expectedPort, c.Port);
        Assert.False(string.IsNullOrEmpty(c.Why));
    }

    // A fake plugin: answers Ping (0x03) with Pong (0x83), the frame being [type:1][len:2 LE][payload].
    private static (TcpListener Listener, Task Server) StartFakePlugin(Func<Socket, Task>? afterFirstPing = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var sock = await listener.AcceptSocketAsync();
            var head = new byte[3];
            await ReadExact(sock, head);
            Assert.Equal(0x03, head[0]);
            await sock.SendAsync(new byte[] { 0x83, 0, 0 });
            if (afterFirstPing is not null) await afterFirstPing(sock);
        });
        return (listener, server);
    }

    private static async Task ReadExact(Socket s, byte[] buf)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReceiveAsync(buf.AsMemory(got), SocketFlags.None);
            if (n <= 0) throw new IOException("closed");
            got += n;
        }
    }

    [Fact]
    public async Task Ping_round_trips_over_tcp()
    {
        var (listener, server) = StartFakePlugin();
        try
        {
            await using var pipe = new CombatPipe { Transport = new PluginTransport(true, ((IPEndPoint)listener.LocalEndpoint).Port, "test") };
            Assert.True(await pipe.EnsureConnectedAsync());
            Assert.True(pipe.IsConnected);
            Assert.True(await pipe.PingAsync());
            await server;
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Nothing_listening_is_a_normal_state_not_an_exception()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int freePort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();   // nothing listens here now
        await using var pipe = new CombatPipe { Transport = new PluginTransport(true, freePort, "test") };
        Assert.False(await pipe.EnsureConnectedAsync());
        Assert.False(pipe.IsConnected);
        Assert.False(await pipe.PingAsync());
    }

    [Fact]
    public async Task A_closed_connection_is_noticed_and_a_new_plugin_is_picked_up()
    {
        var (listener, server) = StartFakePlugin(async s => { s.Shutdown(SocketShutdown.Both); await Task.CompletedTask; });
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            await using var pipe = new CombatPipe { Transport = new PluginTransport(true, port, "test") };
            Assert.True(await pipe.PingAsync());
            await server;
            listener.Stop();   // the game is gone: nothing listens any more
            // the plugin went away: the reader sees EOF and the pipe reports disconnected
            for (int i = 0; i < 50 && pipe.IsConnected; i++) await Task.Delay(100);
            Assert.False(pipe.IsConnected);
            Assert.False(await pipe.PingAsync());   // fails cleanly (no hang, no exception)
            // the game restarted with the plugin: a fresh listener on the same port is reconnected to
            var again = new TcpListener(IPAddress.Loopback, port);
            again.Start();
            try
            {
                var serve = Task.Run(async () =>
                {
                    using var sock = await again.AcceptSocketAsync();
                    var head = new byte[3];
                    await ReadExact(sock, head);
                    await sock.SendAsync(new byte[] { 0x83, 0, 0 });
                    await Task.Delay(300);
                });
                Assert.True(await pipe.PingAsync());
                await serve;
            }
            finally { again.Stop(); }
        }
        finally { listener.Stop(); }
    }
}
