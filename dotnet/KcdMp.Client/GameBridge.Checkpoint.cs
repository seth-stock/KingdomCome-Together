// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coop.Contract;
using Coop.Persistence;
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    private sealed record CheckpointBegin(uint Seed, string World, string Branch, string Content, Dictionary<byte, string> Roster);
    private sealed record CheckpointCharacter(string WorldMd5, ReconcileService.CaptureCharacter Character);
    private sealed class CheckpointRun(Guid epoch, Guid id, CheckpointBegin begin, byte authority)
    {
        public readonly Guid Epoch = epoch, Id = id;
        public readonly CheckpointBegin Begin = begin;
        public readonly byte Authority = authority;
        public readonly long Deadline = Environment.TickCount64 + 90_000;
        public readonly ConcurrentDictionary<byte, bool> Ready = new(), Ack = new();
        public readonly ConcurrentDictionary<byte, CheckpointAssembly> Assemblies = new();
        public readonly ConcurrentDictionary<byte, ReconcileService.CaptureCharacter> Characters = new();
        public readonly ConcurrentDictionary<byte, string> CharacterWorlds = new();
        public readonly ConcurrentDictionary<ushort, TaskCompletionSource<bool>> Credits = new();
        public volatile bool Aborted;
        public volatile bool CommitSent;
        public string? WorldMd5;
    }
    private readonly ConcurrentDictionary<byte, string> _checkpointIdentities = new();
    private readonly object _checkpointGate = new();
    private CheckpointRun? _checkpointHost, _checkpointPeer;
    private Guid _checkpointEpoch = Guid.NewGuid();
    private bool _candidateCheckpoints; // opt-in until hold + native saves + inventory exclusion are engine-proven together
    private bool CheckpointHolding => (_checkpointHost ?? _checkpointPeer) is { Aborted: false } r && Environment.TickCount64 < r.Deadline;

    private async Task CheckpointSendAsync(CheckpointRun run, byte target, bool host, byte kind, byte[] bytes)
    {
        int count = (bytes.Length + CheckpointPacket.ChunkBytes - 1) / CheckpointPacket.ChunkBytes;
        if (count is < 1 or > CheckpointPacket.MaxChunks) throw new InvalidDataException("Checkpoint artifact too large.");
        for (int i = 0; i < count; ++i)
        {
            var packet = new CheckpointPacket(kind, run.Epoch, run.Id, (ushort)i, (ushort)count,
                bytes.AsSpan(i * CheckpointPacket.ChunkBytes, Math.Min(CheckpointPacket.ChunkBytes, bytes.Length - i * CheckpointPacket.ChunkBytes)).ToArray());
            var credit = !host && kind == 2 ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            if (credit is not null) run.Credits[(ushort)i] = credit;
            await WriteJoinAsync(Protocol.BuildJoinUp(host ? Protocol.CheckpointHostUp : Protocol.CheckpointPeerUp, target, 0, packet.Encode()));
            if (credit is not null)
            {
                await credit.Task.WaitAsync(TimeSpan.FromSeconds(10));
                run.Credits.TryRemove((ushort)i, out _);
            }
        }
    }
    private Task CheckpointControlAsync(CheckpointRun run, byte target, bool host, byte kind, string text) =>
        CheckpointSendAsync(run, target, host, kind, Encoding.UTF8.GetBytes(text));

    private async Task CheckpointHoldAsync(CheckpointRun run)
    {
        var held = await _combat.Wo138Async(Wo138Codec.OpSharedHold, Wo138Codec.SharedHoldBody(true, 20));
        if (held is not { Ok: true }) throw new InvalidOperationException("Native world hold was not acknowledged.");
        var prepared = await AskModAsync("KCD2MP_CheckpointPrepare", 6000);
        if (!prepared.StartsWith("ok=true", StringComparison.Ordinal)) throw new InvalidOperationException("Loot did not settle: " + prepared);
        lock (_durableLootGate)
            if (_durableLootPending.Count > 0) throw new InvalidOperationException("Host loot mutations are still pending.");
        if (run.Aborted || !CheckpointHolding) throw new InvalidOperationException("Checkpoint expired during preparation.");
    }
    private async Task CheckpointWaitAsync(CheckpointRun run, Func<bool> ready)
    {
        while (true)
        {
            if (run.Aborted || Environment.TickCount64 >= run.Deadline || !_wo122Connected
                || !Wo134HostRole || _hostWorldSeed != run.Begin.Seed || _where != GameWhere.World
                || !run.Begin.Roster.Keys.Where(p => p != _myGhostId).Order().SequenceEqual(LivePartners().Order()))
                throw new InvalidOperationException("Checkpoint timed out, disconnected, or its roster changed.");
            if (ready()) return;
            await Task.Delay(100);
        }
    }
    private async Task CheckpointReleaseAsync(CheckpointRun run)
    {
        run.Aborted = true;
        lock (_checkpointGate)
        {
            if (!ReferenceEquals(_checkpointHost, run) && !ReferenceEquals(_checkpointPeer, run)) return;
        }
        try { await _transport.ExecuteNowAsync("if KCD2MP_CheckpointRelease then KCD2MP_CheckpointRelease() end"); } catch { }
        // Shared pause owns the same native source. Re-evaluate it instead of unconditionally unpausing a friend's menu.
        _sharedHoldOn = true;
        try { await Wo138SharedPauseTickAsync(Environment.TickCount64, CancellationToken.None); } catch { }
        lock (_checkpointGate)
        {
            if (ReferenceEquals(_checkpointHost, run)) _checkpointHost = null;
            if (ReferenceEquals(_checkpointPeer, run)) _checkpointPeer = null;
        }
    }

    private async Task<ObservedSave?> RequestCheckpointAsync(string why)
    {
        if (!_sharedWorld || !Wo134HostRole || _hostWorldSeed is not uint seed || _where != GameWhere.World
            || Wo123HostJoinActive || CheckpointHolding) return null;
        var roster = new Dictionary<byte, string> { [_myGhostId] = RoomContract.Identity().ParticipantId };
        foreach (byte peer in LivePartners())
        {
            if (!_checkpointIdentities.TryGetValue(peer, out var participant))
            { Console.WriteLine("MP-CHECKPOINT refused: a peer has no relay-verified identity"); return null; }
            roster.Add(peer, participant);
        }
        if (roster.Count > 4 || roster.Values.Distinct().Count() != roster.Count) return null;
        string world = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("kcd2:" + WhsSave.SeedTag(seed))).AsSpan(0, 16)).ToLowerInvariant();
        string content = RoomContract.ContentProfile(RoomContract.GameDirectory() ?? "");
        var run = new CheckpointRun(_checkpointEpoch, Guid.NewGuid(), new(seed, world, Guid.NewGuid().ToString("N"), content, roster), _myGhostId);
        lock (_checkpointGate) { if (_checkpointHost is not null) return null; _checkpointHost = run; }
        string root = Path.Combine(Path.GetDirectoryName(HenryStore.DefaultRoot())!, "checkpoints");
        OperationJournal? journal = null;
        string op = "cp:" + run.Id.ToString("N");
        try
        {
            journal = new OperationJournal(Path.Combine(root, "operations", run.Id.ToString("N") + ".jsonl"));
            string descriptor = JsonSerializer.Serialize(run.Begin);
            journal.Begin(op, "checkpoint", CheckpointStore.Hash(Encoding.UTF8.GetBytes(descriptor)));
            journal.Advance(op, OpState.Reserved);
            foreach (byte peer in roster.Keys.Where(p => p != _myGhostId))
                await CheckpointControlAsync(run, peer, true, 1, descriptor);
            await CheckpointHoldAsync(run);
            run.Ready[_myGhostId] = true;
            await CheckpointWaitAsync(run, () => run.Ready.Count == roster.Count);
            journal.Advance(op, OpState.IntentRecorded, "all roster world holds acknowledged; unsettled loot refused");
            journal.Advance(op, OpState.EngineApplying, "before requesting native world save");
            var observed = await RequestWorldSaveCoreAsync(why, checkpointInternal: true);
            if (observed is null || _hostWorldSeed != seed) throw new InvalidOperationException("World save refused or changed.");
            run.WorldMd5 = Convert.ToHexString(observed.Md5).ToLowerInvariant();
            byte[] worldBytes = WhsSave.ReadShared(observed.FullPath);
            var worldData = WhsSave.Inflate(worldBytes);
            string build = WhsSave.DescriptionSummary(worldData.Desc).GetValueOrDefault("BuildInfo") ?? "";
            var parts = WhsSave.PartsFromStream(worldData.Raw, build, WhsSave.HenryParts.OriginSnapshot);
            Wo134FlushHost();
            string chest;
            lock (_w134Gate) chest = Wo134HostLedgerFor(WhsSave.SeedTag(seed)).ToJson();
            run.Characters[_myGhostId] = new(roster[_myGhostId], parts.Soul == WhsSave.HenrySoul ? "Henry" : "Godwin", WhsSave.SerializeBlock(parts), Encoding.UTF8.GetBytes(chest));
            await CheckpointWaitAsync(run, () => run.Characters.Count == roster.Count);
            if (run.Characters.Any(p => p.Value.ParticipantId != roster[p.Key]) || run.CharacterWorlds.Any(p => p.Value != run.WorldMd5))
                throw new InvalidDataException("Participant identity or snapshot/world pairing changed.");
            // Capture checks build, seed, character kind, chest rows and cross-participant item instance ownership.
            var store = new CheckpointStore(root);
            var ancestry = BranchNewestFirst();
            var parent = store.Checkpoints().Where(m => m.WorldId == world && m.GameBuild == build && m.ContentFingerprint == content)
                .Select(m => (Manifest: m, Position: ancestry.IndexOf(WhsSave.Verify(store.ReadArtifact(m.WorldHash)).Md5.ToLowerInvariant())))
                .Where(p => p.Position >= 0).OrderBy(p => p.Position).ThenBy(p => p.Manifest.CheckpointId, StringComparer.Ordinal).FirstOrDefault().Manifest;
            var manifest = new ReconcileService(root).Capture(world, parent?.BranchId ?? run.Begin.Branch, parent?.CheckpointId, content, worldBytes,
                run.Characters.OrderBy(p => p.Key).Select(p => p.Value).ToArray(), run.Id.ToString("N"), publish: false);
            string digest = store.PutArtifact(JsonSerializer.SerializeToUtf8Bytes(manifest));
            journal.Advance(op, OpState.EngineVerified, "verified world and all paired character/ledger artifacts", digest);
            journal.Advance(op, OpState.LedgerCommitted, "prepared manifest blob is durable; publication waits for all acknowledgements");
            run.CommitSent = true;
            foreach (byte peer in roster.Keys.Where(p => p != _myGhostId))
                await CheckpointControlAsync(run, peer, true, 2, run.WorldMd5 + " " + digest);
            journal.Advance(op, OpState.Delivered, "commit sent to every roster participant");
            run.Ack[_myGhostId] = true;
            await CheckpointWaitAsync(run, () => run.Ack.Count == roster.Count);
            journal.Advance(op, OpState.RecipientVerified, "all roster participants acknowledged their matching local snapshot");
            store.PublishCheckpoint(manifest);
            journal.Advance(op, OpState.Complete, "barrier complete; engine integration remains Candidate");
            foreach (byte peer in roster.Keys.Where(p => p != _myGhostId))
                await CheckpointControlAsync(run, peer, true, 4, run.WorldMd5);
            Console.WriteLine($"MP-CHECKPOINT complete id={run.Id:N} roster={roster.Count} world={run.WorldMd5}; Candidate");
            return observed;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MP-CHECKPOINT aborted: " + ex.Message);
            if (journal?.Get(op) is { } record && !OperationJournal.IsTerminal(record.State))
                journal.Advance(op, record.State <= OpState.IntentRecorded ? OpState.Rejected : OpState.RecoveryRequired, ex.Message);
            foreach (byte peer in roster.Keys.Where(p => p != _myGhostId))
                try { await CheckpointControlAsync(run, peer, true, 3, "aborted"); } catch { }
            return null;
        }
        finally { journal?.Dispose(); await CheckpointReleaseAsync(run); }
    }

    private async Task CheckpointFrameAsync(int type, byte source, byte[] body)
    {
        CheckpointRun? run = type == Protocol.CheckpointHostDown ? _checkpointPeer : _checkpointHost;
        try
        {
            var packet = CheckpointPacket.Decode(body);
            if (type == Protocol.CheckpointHostDown && packet.Kind == 1 && Wo134JoinerRole && _joinedWorld && _joinedTag is not null
                && _where == GameWhere.World && packet.Count == 1 && packet.Index == 0)
            {
                if (run is not null) return; // a duplicate begin must not reset its timeout or mutate again
                var begin = JsonSerializer.Deserialize<CheckpointBegin>(packet.Data) ?? throw new InvalidDataException("Missing roster.");
                if (!_peerSeedKnown || begin.Seed != _peerSeed || begin.Roster.Count is < 1 or > 4
                    || !begin.Roster.TryGetValue(_myGhostId, out string? own) || own != RoomContract.Identity().ParticipantId
                    || !begin.Roster.TryGetValue(source, out string? host) || !_checkpointIdentities.TryGetValue(source, out var knownHost) || host != knownHost
                    || begin.Roster.Values.Any(p => !Guid.TryParseExact(p, "N", out _)) || begin.Roster.Values.Distinct().Count() != begin.Roster.Count)
                    throw new InvalidDataException("Checkpoint world/roster does not match this joined world.");
                run = new(packet.Epoch, packet.Checkpoint, begin, source);
                lock (_checkpointGate) { if (_checkpointPeer is not null) return; _checkpointPeer = run; }
                await CheckpointHoldAsync(run);
                await CheckpointControlAsync(run, Protocol.JoinTargetHost, false, 1, "ready");
                _ = CheckpointPeerTimeoutAsync(run);
                return;
            }
            if (run is null || run.Aborted || packet.Epoch != run.Epoch || packet.Checkpoint != run.Id || Environment.TickCount64 >= run.Deadline) return;
            if (type == Protocol.CheckpointPeerDown && Wo134HostRole && run.Begin.Roster.ContainsKey(source))
            {
                if (packet.Kind == 1 && packet.Count == 1 && Encoding.UTF8.GetString(packet.Data) == "ready") run.Ready[source] = true;
                else if (packet.Kind == 2 && run.Ready.ContainsKey(source))
                {
                    var bytes = run.Assemblies.GetOrAdd(source, _ => new(run.Epoch, run.Id)).Add(packet);
                    if (bytes is not null)
                    {
                        var artifact = JsonSerializer.Deserialize<CheckpointCharacter>(bytes) ?? throw new InvalidDataException("Missing character.");
                        var capture = artifact.Character;
                        if (capture.ParticipantId != run.Begin.Roster[source]) throw new InvalidDataException("Character is from another participant.");
                        run.Characters[source] = capture;
                        run.CharacterWorlds[source] = artifact.WorldMd5;
                    }
                    await CheckpointControlAsync(run, source, true, 5, packet.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (packet.Kind == 3 && run.CommitSent && packet.Count == 1 && Encoding.UTF8.GetString(packet.Data) == run.WorldMd5) run.Ack[source] = true;
                else if (packet.Kind == 4) run.Aborted = true;
            }
            else if (type == Protocol.CheckpointHostDown && Wo134JoinerRole && source == run.Authority && packet.Count == 1)
            {
                if (packet.Kind == 2)
                {
                    string md5 = Encoding.UTF8.GetString(packet.Data).Split(' ')[0];
                    if (md5 != run.WorldMd5 || !run.Characters.ContainsKey(_myGhostId)) throw new InvalidDataException("Commit has no matching character snapshot.");
                    var local = run.Characters[_myGhostId];
                    var store = new CheckpointStore(Path.Combine(Path.GetDirectoryName(HenryStore.DefaultRoot())!, "checkpoints"));
                    store.PutArtifact(local.CharacterBlock); store.PutArtifact(local.ChestLedger);
                    store.PutArtifact(packet.Data); // durable receipt references the host's manifest digest; it is not a complete local world bundle
                    run.CommitSent = true;
                    await CheckpointControlAsync(run, Protocol.JoinTargetHost, false, 3, md5);
                }
                else if (packet.Kind == 3) await CheckpointReleaseAsync(run);
                else if (packet.Kind == 4 && run.CommitSent && Encoding.UTF8.GetString(packet.Data) == run.WorldMd5) await CheckpointReleaseAsync(run);
                else if (packet.Kind == 5 && ushort.TryParse(Encoding.UTF8.GetString(packet.Data), out ushort index)
                    && run.Credits.TryGetValue(index, out var credit)) credit.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MP-CHECKPOINT frame refused: " + ex.Message);
            if (run is not null)
            {
                run.Aborted = true;
                if (type == Protocol.CheckpointHostDown)
                {
                    try { await CheckpointControlAsync(run, Protocol.JoinTargetHost, false, 4, "refused"); } catch { }
                    await CheckpointReleaseAsync(run);
                }
            }
        }
    }
    private async Task CheckpointPeerTimeoutAsync(CheckpointRun run)
    {
        await Task.Delay(Math.Max(1, (int)(run.Deadline - Environment.TickCount64)));
        if (ReferenceEquals(_checkpointPeer, run)) await CheckpointReleaseAsync(run);
    }
    private async Task CheckpointSnapshotAsync(HenryStore.Snapshot snap, WhsSave.HenryParts parts)
    {
        var run = _checkpointPeer;
        if (run is null || run.Aborted || !CheckpointHolding || !_peerSeedKnown || run.Begin.Seed != _peerSeed) return;
        run.WorldMd5 = snap.Md5;
        string ledger = _henry.LoadChestLedger(snap) ?? throw new InvalidDataException("Snapshot has no chest ledger.");
        var capture = new ReconcileService.CaptureCharacter(RoomContract.Identity().ParticipantId,
            parts.Soul == WhsSave.HenrySoul ? "Henry" : "Godwin", WhsSave.SerializeBlock(parts), Encoding.UTF8.GetBytes(ledger));
        run.Characters[_myGhostId] = capture;
        await CheckpointSendAsync(run, Protocol.JoinTargetHost, false, 2, JsonSerializer.SerializeToUtf8Bytes(new CheckpointCharacter(snap.Md5, capture)));
    }
}
