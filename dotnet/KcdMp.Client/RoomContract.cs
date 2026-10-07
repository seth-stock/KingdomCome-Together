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

    public sealed record Fingerprint(string Agent, string Lua, string Native, string Engine, string Content, IReadOnlyList<string>? Dlc = null);

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

    /// <summary>
    /// The DLC that is active and affects saves, as the game itself logged it at startup (kcd.log, "DLC list:"). null while the game has not logged it.
    /// KCD2's DLC is a Steam entitlement, not files (its content is inside the base paks), so the log is the only honest source.
    /// </summary>
    public static IReadOnlyList<string>? ReadDlc()
    {
        try
        {
            string? log = KcdLogLocator.Find();
            if (log is null || !File.Exists(log)) return null;
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var rd = new StreamReader(fs, Encoding.UTF8);
            var lines = new List<string>();
            string? line;
            while ((line = rd.ReadLine()) is not null) lines.Add(line);
            return DlcLog.ActiveSaveAffecting(lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The OTHER MODS installed with the game, as one hash. Different mods keep a room at presence. (DLC is compared by name, not by this hash.)</summary>
    public static string ContentProfile(string gameDir)
    {
        var sb = new StringBuilder();
        try
        {
            string mods = Path.Combine(gameDir, "Mods");
            if (Directory.Exists(mods))
                foreach (var d in Directory.EnumerateDirectories(mods).Select(Path.GetFileName).Where(n => !string.Equals(n, "kdcmp", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("M:").Append(d!.ToLowerInvariant()).Append('\n');
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        // an install with no other mods still has a profile (of nothing): "" would mean "unknown" and hide a real difference
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static Fingerprint Measure(string? gameDir, string? nativeDll)
    {
        string agent = FileHash(Environment.ProcessPath);
        if (gameDir is null) return new Fingerprint(agent, "", FileHash(nativeDll), "", "");
        string engine = new[] { "Win64ReleaseSteamLTO_DLL", "Win64ReleaseSteam", "Win64" }
            .Select(b => Path.Combine(gameDir, "Bin", b, "WHGame.dll")).FirstOrDefault(File.Exists) ?? "";
        return new Fingerprint(agent, FileHash(Path.Combine(gameDir, "Mods", "kdcmp", "Data", "kdcmp.pak")), FileHash(nativeDll), FileHash(engine), ContentProfile(gameDir), ReadDlc());
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
        new(GameId, ReleaseVersionInfo.Current, Protocol.Version, RoomHandshake.CurrentContractVersion, f.Agent, f.Lua, f.Native, f.Engine, f.Content, Capabilities(f.Native.Length > 0), f.Dlc);

    private static Fingerprint? _cached;
    private static ParticipantBindings.Identity? _identity;

    public static RoomHandshake Current()
    {
        _cached ??= Measure(GameDirectory(), Path.Combine(AppContext.BaseDirectory, "KCDMP.dll"));
        if (_cached.Dlc is null && ReadDlc() is { } dlc) _cached = _cached with { Dlc = dlc };      // the game logs its DLC when it starts; ask again until it has
        return Build(_cached);
    }

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

    /// <summary>What a player's own Ack says about their content against the host's (the per-player tokens in the "missing" list).</summary>
    public sealed record Flags(bool ModsDiffer, IReadOnlyList<string> DlcHostExtra, IReadOnlyList<string> DlcPeerExtra)
    {
        public static readonly Flags None = new(false, Array.Empty<string>(), Array.Empty<string>());
    }

    private static IReadOnlyList<string> Names(string token, string key) =>
        token.Length > key.Length + 1 ? token[(key.Length + 1)..].Split('+', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();

    public static Flags ParseFlags(string missing)
    {
        bool mods = false; IReadOnlyList<string> hostExtra = Array.Empty<string>(), peerExtra = Array.Empty<string>();
        foreach (var tok in missing.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok == Protocol.RoomContentDiffers) mods = true;
            else if (tok.StartsWith(Protocol.RoomDlcHostExtra + "=", StringComparison.Ordinal)) hostExtra = Names(tok, Protocol.RoomDlcHostExtra);
            else if (tok.StartsWith(Protocol.RoomDlcPeerExtra + "=", StringComparison.Ordinal)) peerExtra = Names(tok, Protocol.RoomDlcPeerExtra);
        }
        return new Flags(mods, hostExtra, peerExtra);
    }

    /// <summary>The missing-capabilities list without the per-player content tokens.</summary>
    public static string StripFlags(string missing) => string.Join(',', missing.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Where(t => t != Protocol.RoomContentDiffers && !t.StartsWith(Protocol.RoomDlcHostExtra, StringComparison.Ordinal) && !t.StartsWith(Protocol.RoomDlcPeerExtra, StringComparison.Ordinal)));

    /// <summary>True when this player's other MODS differ from the host's: a world or a Henry must not be moved between differently-modded installs.</summary>
    public static bool ContentDiffers(string missing) => ParseFlags(missing).ModsDiffer;

    /// <summary>The plain sentence for the content flags, "" when there is nothing to say.</summary>
    public static string ContentSentence(Flags f)
    {
        var parts = new List<string>();
        if (f.ModsDiffer) parts.Add("Your game has other mods than your host's: worlds and characters will not be moved between you.");
        if (f.DlcPeerExtra.Count > 0) parts.Add($"Your game has DLC your host's does not ({string.Join(", ", f.DlcPeerExtra)}): it stays out of what you share. To play exactly like your host, turn it off in Steam (Properties, DLC) and restart.");
        if (f.DlcHostExtra.Count > 0) parts.Add($"Your host's game has DLC you do not have ({string.Join(", ", f.DlcHostExtra)}): their world needs it, so it cannot be moved to you. Get it, or ask your host to play without it.");
        return string.Join(" ", parts);
    }

    public static string ModeWord(byte mode) => mode switch { 2 => "shared", 1 => "partial", _ => "presence" };

    /// <summary>"Room: presence only ..." in plain words; never "shared" for a room that is not.</summary>
    public static string Sentence(byte mode, string missing)
    {
        string m = StripFlags(missing).Replace(",", ", ");
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
