// SPDX-License-Identifier: GPL-3.0-only
// The client side of the v12 room contract: the handshake this installation sends, the identity proof, and how the relay's answers are read.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Coop.Contract;
using KcdMp.Wire;

namespace KcdMp.Client;

public static class RoomContract
{
    public const string GameId = "kcd2";

    public sealed record Fingerprint(string Agent, string Lua, string Native, string Engine, string Content);

    private static string FileHash(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path)) return "";
            using var s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>The game folder: the one kcd.log lives in (the Modding Tools workspace is the build that is launched).</summary>
    public static string? GameDirectory() => KcdLogLocator.Find() is { } log ? Path.GetDirectoryName(log) : null;

    /// <summary>DLC and other mods installed with the game, as one hash. Different content keeps a room at presence.</summary>
    public static string ContentProfile(string gameDir)
    {
        var sb = new StringBuilder();
        try
        {
            string data = Path.Combine(gameDir, "Data");
            if (Directory.Exists(data))
                foreach (var f in Directory.EnumerateFiles(data, "*.pak").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("D:").Append(Path.GetFileName(f).ToLowerInvariant()).Append(':').Append(new FileInfo(f).Length).Append('\n');
            string mods = Path.Combine(gameDir, "Mods");
            if (Directory.Exists(mods))
                foreach (var d in Directory.EnumerateDirectories(mods).Select(Path.GetFileName).Where(n => !string.Equals(n, "kdcmp", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("M:").Append(d!.ToLowerInvariant()).Append('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        return sb.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static Fingerprint Measure(string? gameDir, string? nativeDll)
    {
        string agent = FileHash(Environment.ProcessPath);
        if (gameDir is null) return new Fingerprint(agent, "", FileHash(nativeDll), "", "");
        string engine = new[] { "Win64ReleaseSteamLTO_DLL", "Win64ReleaseSteam", "Win64" }
            .Select(b => Path.Combine(gameDir, "Bin", b, "WHGame.dll")).FirstOrDefault(File.Exists) ?? "";
        return new Fingerprint(agent, FileHash(Path.Combine(gameDir, "Mods", "kdcmp", "Data", "kdcmp.pak")), FileHash(nativeDll), FileHash(engine), ContentProfile(gameDir));
    }

    /// <summary>
    /// The capability table of THIS build by its evidence (docs/CAPABILITIES.md). Authority over NPCs, combat and quests is engine-verified only with the native plugin present;
    /// durable loot and the live checkpoint barrier are NOT claimed (docs/IMPLEMENTATION-STATUS.md): the room is "partly shared", never "shared", until they are proved.
    /// </summary>
    public static IReadOnlyDictionary<string, CapabilityLevel> Capabilities(bool nativePluginPresent) => new Dictionary<string, CapabilityLevel>
    {
        [CapabilityNames.PresenceBodies] = CapabilityLevel.EngineVerified,
        [CapabilityNames.Locomotion] = CapabilityLevel.EngineVerified,
        [CapabilityNames.Outfits] = CapabilityLevel.EngineVerified,
        [CapabilityNames.PersonalCharacter] = CapabilityLevel.EngineVerified,
        [CapabilityNames.ParticipantIdentity] = CapabilityLevel.IntegrationVerified,
        [CapabilityNames.CheckpointBarrier] = CapabilityLevel.Candidate,
        [CapabilityNames.AuthorityNpc] = nativePluginPresent ? CapabilityLevel.EngineVerified : CapabilityLevel.Candidate,
        [CapabilityNames.AuthorityCombat] = nativePluginPresent ? CapabilityLevel.EngineVerified : CapabilityLevel.Candidate,
        [CapabilityNames.AuthorityQuest] = CapabilityLevel.EngineVerified,
        [CapabilityNames.AuthorityLoot] = CapabilityLevel.Candidate,
    };

    public static RoomHandshake Build(Fingerprint f) =>
        new(GameId, ReleaseVersionInfo.Current, Protocol.Version, RoomHandshake.CurrentContractVersion, f.Agent, f.Lua, f.Native, f.Engine, f.Content, Capabilities(f.Native.Length > 0));

    private static Fingerprint? _cached;
    private static ParticipantBindings.Identity? _identity;

    public static RoomHandshake Current() => Build(_cached ??= Measure(GameDirectory(), Path.Combine(AppContext.BaseDirectory, "KCDMP.dll")));

    public static ParticipantBindings.Identity Identity()
    {
        if (_identity is not null) return _identity;
        try
        {
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KCDMP", "participant.key");
            return _identity = ParticipantBindings.Identity.LoadOrCreate(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return _identity = ParticipantBindings.Identity.CreateEphemeral(); }
    }

    /// <summary>The Handshake frame, v12: [protocol][nameLen][name][release SEP contract SEP participantId;publicKey].</summary>
    public static byte[] BuildHandshake(string name, string release, RoomHandshake hs, ParticipantBindings.Identity id) =>
        RelayConnector.BuildHandshake(name, release + Protocol.HandshakeFieldSeparator + hs.Encode() + Protocol.HandshakeFieldSeparator + id.ParticipantId + ";" + id.PublicKey);

    /// <summary>
    /// Reads the relay's first real answer. An identity challenge is answered with the signed proof and reading continues; everything else (the Ack, or a refusal) is returned.
    /// </summary>
    public static async Task<(byte Type, byte[] Body)> ReadAdmissionAsync(Stream stream, ParticipantBindings.Identity id, CancellationToken ct)
    {
        while (true)
        {
            var (type, body) = await RelayConnector.ReadFrameAsync(stream, ct);
            if (type != Protocol.IdentityChallengeDown) return (type, body);
            var sig = Encoding.UTF8.GetBytes(id.Sign(Encoding.UTF8.GetString(body)));
            var frame = new byte[3 + sig.Length];
            frame[0] = Protocol.IdentityProofUp;
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)sig.Length);
            sig.CopyTo(frame, 3);
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
        }
    }

    public static string ModeWord(byte mode) => mode switch { 2 => "shared", 1 => "partial", _ => "presence" };

    /// <summary>"Room: presence only ..." in plain words; never "shared" for a room that is not.</summary>
    public static string Sentence(byte mode, string missing)
    {
        string m = missing.Length > 0 ? missing.Replace(",", ", ") : "";
        return mode switch
        {
            2 => "Room: shared simulation.",
            1 => "Room: partly shared (not every authority capability is verified" + (m.Length > 0 ? ": " + m : "") + ").",
            _ => "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active" + (m.Length > 0 ? " (" + m + ")" : "") + ".",
        };
    }

    /// <summary>The Ack body: [id][mode][missing UTF-8]. An older Ack has only the id.</summary>
    public static (byte Mode, string Missing) ParseAck(byte[] body) =>
        body.Length >= 2 ? (body[1], Encoding.UTF8.GetString(body, 2, body.Length - 2)) : ((byte)0, "");
}
