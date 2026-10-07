// SPDX-License-Identifier: GPL-3.0-only
// The v12 room contract at the real relay: handshake, payload match, participant identity and the room's honest mode. (real relay, real TCP, synthetic peers)
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Coop.Contract;
using KcdMp.Client;
using KcdMp.Wire;

namespace KcdMp.Relay.Tests;

/// <summary>A relay that REQUIRES the room contract (the product's default), unlike the fixture of the older round-trip tests.</summary>
public sealed class ContractRelayFixture : RelayFixture
{
    protected override string[] ExtraArgs => Array.Empty<string>();
}

public class ContractRelayTests : IClassFixture<ContractRelayFixture>
{
    private readonly ContractRelayFixture _relay;
    public ContractRelayTests(ContractRelayFixture relay) { _relay = relay; }

    private static RoomHandshake Hs(string game = "kcd2", string lua = "a", string content = "", int contract = 1, IReadOnlyDictionary<string, CapabilityLevel>? caps = null) =>
        new(game, ReleaseVersionInfo.Current, Protocol.Version, contract, "", lua.Length == 0 ? "" : new string(lua[0], 64), "", "", content.Length == 0 ? "" : new string(content[0], 64),
            caps ?? new Dictionary<string, CapabilityLevel>());

    private static string Trailing(RoomHandshake hs, string identity) =>
        ReleaseVersionInfo.Current + Protocol.HandshakeFieldSeparator + hs.Encode() + Protocol.HandshakeFieldSeparator + identity;

    private sealed class P : IAsyncDisposable
    {
        public readonly TcpClient Tcp = new();
        public NetworkStream S = null!;
        public readonly ParticipantBindings.Identity Id = ParticipantBindings.Identity.CreateEphemeral();

        public static async Task<P> Connect(int port) { var p = new P(); await p.Tcp.ConnectAsync(IPAddress.Loopback, port); p.S = p.Tcp.GetStream(); return p; }

        public async Task<(byte Type, byte[] Body)> Admit(string name, RoomHandshake? hs, string? identityField = null)
        {
            byte[] frame = hs is null
                ? RelayConnector.BuildHandshake(name, ReleaseVersionInfo.Current)
                : RelayConnector.BuildHandshake(name, Trailing(hs, identityField ?? Id.ParticipantId + ";" + Id.PublicKey));
            await S.WriteAsync(frame);
            return await RoomContract.ReadAdmissionAsync(S, Id, new CancellationTokenSource(5000).Token);
        }

        public async Task<(byte Type, byte[] Body)> Next(byte type, int ms = 3000)
        {
            using var cts = new CancellationTokenSource(ms);
            while (true) { var f = await RelayConnector.ReadFrameAsync(S, cts.Token); if (f.Type == type) return f; }
        }

        public ValueTask DisposeAsync() { Tcp.Dispose(); return ValueTask.CompletedTask; }
    }

    [Fact]
    public async Task A_build_that_sends_no_room_handshake_is_refused_with_a_reason()
    {
        await using var p = await P.Connect(_relay.TcpPort);
        var (type, body) = await p.Admit("old", null);
        Assert.Equal(Protocol.ContractRefusedDown, type);
        Assert.Contains("same build", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task A_good_handshake_is_admitted_and_the_ack_says_what_kind_of_room_it_is()
    {
        await using var p = await P.Connect(_relay.TcpPort);
        var (type, body) = await p.Admit("good", Hs());
        Assert.Equal(Protocol.Ack, type);
        var (mode, missing) = RoomContract.ParseAck(body);
        Assert.Equal(0, mode);                                                  // no authority capability: presence only
        Assert.Contains("authority.combat", missing);
        var room = await p.Next(Protocol.RoomModeDown);                         // and the room announces it
        Assert.Equal(0, room.Body[0]);
        Assert.Contains("NOT active", RoomContract.Sentence(mode, missing));
    }

    [Fact]
    public async Task Another_game_another_contract_or_another_mod_payload_is_refused_before_admission()
    {
        await using var host = await P.Connect(_relay.TcpPort);
        Assert.Equal(Protocol.Ack, (await host.Admit("host", Hs())).Type);
        foreach (var (hs, expect) in new[] { (Hs(game: "kcd1"), "different games"), (Hs(contract: 2), "contract version"), (Hs(lua: "b"), "different mod payloads") })
        {
            await using var p = await P.Connect(_relay.TcpPort);
            var (type, body) = await p.Admit("peer", hs);
            Assert.Equal(Protocol.ContractRefusedDown, type);
            Assert.Contains(expect, Encoding.UTF8.GetString(body));
        }
    }

    [Fact]
    public async Task A_payload_that_cannot_be_verified_is_refused()
    {
        await using var p = await P.Connect(_relay.TcpPort);
        var (type, body) = await p.Admit("nomod", Hs(lua: ""));                 // no Lua hash at all
        Assert.Equal(Protocol.ContractRefusedDown, type);
        Assert.Contains("could not be verified", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Another_connection_cannot_take_a_participant_id_by_claiming_it()
    {
        await using var alice = await P.Connect(_relay.TcpPort);
        Assert.Equal(Protocol.Ack, (await alice.Admit("alice", Hs())).Type);

        var mallory = ParticipantBindings.Identity.CreateEphemeral();
        await using var m = await P.Connect(_relay.TcpPort);
        var (type, body) = await m.Admit("mallory", Hs(), alice.Id.ParticipantId + ";" + mallory.PublicKey);   // alice's id, mallory's key
        Assert.Equal(Protocol.ContractRefusedDown, type);
        Assert.StartsWith("identity:", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task A_proof_that_does_not_follow_the_challenge_is_refused()
    {
        await using var p = await P.Connect(_relay.TcpPort);
        await p.S.WriteAsync(RelayConnector.BuildHandshake("x", Trailing(Hs(), p.Id.ParticipantId + ";" + p.Id.PublicKey)));
        var challenge = await RelayConnector.ReadFrameAsync(p.S, new CancellationTokenSource(3000).Token);
        Assert.Equal(Protocol.IdentityChallengeDown, challenge.Type);
        var junk = new byte[3]; junk[0] = Protocol.Ping;                      // a bare header: nothing unread is left to turn the refusal into a reset
        await p.S.WriteAsync(junk);
        var (type, body) = await RelayConnector.ReadFrameAsync(p.S, new CancellationTokenSource(3000).Token);
        Assert.Equal(Protocol.ContractRefusedDown, type);
        Assert.Contains("did not follow the challenge", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task A_stale_proof_signed_for_an_earlier_challenge_is_not_accepted()
    {
        await using var first = await P.Connect(_relay.TcpPort);
        // capture the signature of one challenge...
        await first.S.WriteAsync(RelayConnector.BuildHandshake("a", Trailing(Hs(), first.Id.ParticipantId + ";" + first.Id.PublicKey)));
        var c1 = await RelayConnector.ReadFrameAsync(first.S, new CancellationTokenSource(3000).Token);
        string oldSig = first.Id.Sign(Encoding.UTF8.GetString(c1.Body));
        // ...and replay it on a second connection that was given a different challenge
        await using var second = await P.Connect(_relay.TcpPort);
        await second.S.WriteAsync(RelayConnector.BuildHandshake("b", Trailing(Hs(), first.Id.ParticipantId + ";" + first.Id.PublicKey)));
        await RelayConnector.ReadFrameAsync(second.S, new CancellationTokenSource(3000).Token);
        var sig = Encoding.UTF8.GetBytes(oldSig);
        var frame = new byte[3 + sig.Length]; frame[0] = Protocol.IdentityProofUp; BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)sig.Length); sig.CopyTo(frame, 3);
        await second.S.WriteAsync(frame);
        var (type, body) = await RelayConnector.ReadFrameAsync(second.S, new CancellationTokenSource(3000).Token);
        Assert.Equal(Protocol.ContractRefusedDown, type);
        Assert.Contains("not valid", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Engine_verified_authority_on_both_sides_makes_a_partial_room_not_a_shared_one()
    {
        var caps = new Dictionary<string, CapabilityLevel> { ["authority.combat"] = CapabilityLevel.EngineVerified };
        await using var a = await P.Connect(_relay.TcpPort);
        await a.Admit("a", Hs(caps: caps));
        await using var b = await P.Connect(_relay.TcpPort);
        var (typeB, ackB) = await b.Admit("b", Hs(caps: caps));
        Assert.Equal(Protocol.Ack, typeB);
        // other tests' presence-only peers may share this relay, so only this peer's OWN negotiated mode is asserted exactly
        Assert.Equal(1, RoomContract.ParseAck(ackB).Mode);
        Assert.Equal("partial", RoomContract.ModeWord(1));
    }
}
