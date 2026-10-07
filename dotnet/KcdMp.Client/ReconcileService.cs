// SPDX-License-Identifier: GPL-3.0-only
using Coop.Persistence;
using System.Text.Json;

namespace KcdMp.Client;

/// <summary>Checkpoint-backed selection and verified preparation. Preparation
/// never writes into a playline or declares a running session ready. The live
/// authority barrier and participant acknowledgements are separate prerequisites.</summary>
public sealed class ReconcileService
{
    public sealed record CaptureCharacter(string ParticipantId, string CharacterKind, byte[] CharacterBlock, byte[] ChestLedger);
    public sealed record Selection(CheckpointRelation Relation, string SelectedId, string ArchivedAlternativeId, IReadOnlyList<string> ParticipantIds);
    private readonly CheckpointStore _store;
    public ReconcileService(string root) { _store = new(root); }
    private static WhsSave.HenryParts VerifyCharacter(ParticipantArtifact p, byte[] block, string build, uint seed)
    {
        var parts = WhsSave.ParseBlock(block);
        string kind = parts.Soul == WhsSave.HenrySoul ? "Henry" : parts.Soul == WhsSave.BohutaSoul ? "Godwin" : "Unknown";
        if (parts.Build != build || p.CharacterKind != kind || kind == "Unknown" || parts.Seed != seed)
            throw new InvalidDataException("Character build, kind or playthrough seed does not match checkpoint.");
        return parts;
    }
    private static void VerifyLedger(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > Wo134Rules.Ledger.MaxEntries)
            throw new InvalidDataException("Invalid chest ledger.");
        var ledger = JsonSerializer.Deserialize<Wo134Rules.Ledger>(bytes) ?? throw new InvalidDataException("Missing chest ledger.");
        if (ledger.Entries.Any(e => !Wo134Rules.Valid(e))) throw new InvalidDataException("Invalid chest ledger row.");
    }
    public CheckpointManifest Capture(string worldId, string branchId, string? parentId, string contentFingerprint,
        byte[] world, IReadOnlyList<CaptureCharacter> characters, string? checkpointId = null, bool publish = true)
    {
        var verified = WhsSave.Verify(world);
        if (!verified.Ok) throw new InvalidDataException("World does not verify: " + verified.Reason);
        var container = WhsSave.Inflate(world);
        uint seed = WhsSave.ReadSeed(container.Raw) ?? throw new InvalidDataException("World has no playthrough seed.");
        if (parentId is not null)
        {
            var parent = _store.ReadCheckpoint(parentId);
            if (WhsSave.ReadSeed(WhsSave.Inflate(_store.ReadArtifact(parent.WorldHash)).Raw) != seed)
                throw new InvalidDataException("Parent belongs to a different playthrough.");
        }
        string build = WhsSave.DescriptionSummary(container.Desc).GetValueOrDefault("BuildInfo") ?? "";
        var player = WhsSave.PlayerOf(container.Raw);
        if (!player.IsKnown) throw new InvalidDataException("World player is neither Henry nor Godwin.");
        string kind = player.Soul == WhsSave.HenrySoul ? "Henry" : "Godwin";
        var references = new List<ParticipantArtifact>();
        var ownedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in characters)
        {
            if (c.CharacterKind != kind) throw new InvalidDataException("Cannot mix character kinds in one checkpoint.");
            var p = new ParticipantArtifact(c.ParticipantId, c.CharacterKind, _store.PutArtifact(c.CharacterBlock), _store.PutArtifact(c.ChestLedger));
            var parts = VerifyCharacter(p, c.CharacterBlock, build, seed);
            foreach (string instance in WhsSave.InventoryInstanceIds(parts))
                if (!ownedItems.Add(instance)) throw new InvalidDataException("An item instance is owned by two checkpoint participants.");
            VerifyLedger(c.ChestLedger); references.Add(p);
        }
        var manifest = new CheckpointManifest(1, worldId, checkpointId ?? Guid.NewGuid().ToString("N"), parentId, branchId,
            build, contentFingerprint, _store.PutArtifact(world), references);
        if (publish) _store.PublishCheckpoint(manifest);
        return manifest;
    }
    public Selection Select(string localId, string remoteId, string? chooseId = null)
    {
        var local = _store.ReadCheckpoint(localId); var remote = _store.ReadCheckpoint(remoteId);
        uint? localSeed = WhsSave.ReadSeed(WhsSave.Inflate(_store.ReadArtifact(local.WorldHash)).Raw);
        uint? remoteSeed = WhsSave.ReadSeed(WhsSave.Inflate(_store.ReadArtifact(remote.WorldHash)).Raw);
        if (localSeed is null || localSeed != remoteSeed) throw new InvalidDataException("Checkpoints belong to different playthroughs.");
        CheckpointManifest? Lookup(string id)
        { try { return _store.ReadCheckpoint(id); } catch (FileNotFoundException) { return null; } }
        var relation = CheckpointStore.Compare(local, remote, Lookup);
        if (relation == CheckpointRelation.Incompatible) throw new InvalidDataException("Worlds/builds/content are incompatible.");
        chooseId ??= relation switch
        {
            CheckpointRelation.Same or CheckpointRelation.LocalDescendant => localId,
            CheckpointRelation.RemoteDescendant => remoteId,
            _ => throw new InvalidOperationException("These branches diverge or have unknown ancestry. Explicitly choose one; both remain archived."),
        };
        if (chooseId != localId && chooseId != remoteId) throw new ArgumentException("Choose one of the compared checkpoints.");
        var selected = chooseId == localId ? local : remote;
        var required = local.Participants.Select(p => p.ParticipantId).Union(remote.Participants.Select(p => p.ParticipantId)).ToArray();
        if (required.Any(id => !selected.Participants.Any(p => p.ParticipantId == id)))
            throw new InvalidOperationException("The selected checkpoint lacks a paired character for a participant. Select a complete checkpoint; no cross-branch character was substituted.");
        var ownedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in selected.Participants)
        {
            var parts = VerifyCharacter(p, _store.ReadArtifact(p.CharacterHash), selected.GameBuild, localSeed.Value);
            foreach (string instance in WhsSave.InventoryInstanceIds(parts))
                if (!ownedItems.Add(instance)) throw new InvalidDataException("An item instance has two owners in the selected checkpoint.");
            VerifyLedger(_store.ReadArtifact(p.LedgerHash));
        }
        if (!WhsSave.Verify(_store.ReadArtifact(selected.WorldHash)).Ok) throw new InvalidDataException("Selected world is not valid.");
        return new(relation, chooseId, chooseId == localId ? remoteId : localId, required);
    }
    public void Prepare(Selection selection, string output, Dictionary<string, string> questClasses)
    {
        // Re-evaluate both input checkpoints; do not trust a deserialized selection.
        selection = Select(selection.SelectedId, selection.ArchivedAlternativeId, selection.SelectedId);
        var manifest = _store.ReadCheckpoint(selection.SelectedId);
        if (questClasses.Count == 0) throw new InvalidDataException("A verified quest-item catalog is required to prepare personalized saves.");
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output exists; choose a new staging directory.");
        Directory.CreateDirectory(output);
        void Write(string name, byte[] bytes)
        {
            using var f = new FileStream(Path.Combine(output, name), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            f.Write(bytes); f.Flush(true);
        }
        byte[] world = _store.ReadArtifact(manifest.WorldHash);
        Write("world.whs", world);
        foreach (var p in manifest.Participants)
        {
            byte[] block = _store.ReadArtifact(p.CharacterHash);
            Write(p.ParticipantId + ".hblk", block);
            Write(p.ParticipantId + ".chests.json", _store.ReadArtifact(p.LedgerHash));
            var parts = WhsSave.ParseBlock(block);
            byte[] personalized = WhsSave.SpliceParts(world, parts, questClasses, WhsSave.QuestItemMode.Strip).File;
            var failures = WhsSave.CheckParts(world, parts, personalized, questClasses, WhsSave.QuestItemMode.Strip);
            if (failures.Count > 0 || !WhsSave.Verify(personalized).Ok) throw new InvalidDataException("Personalized save did not verify.");
            Write(p.ParticipantId + ".whs", personalized);
        }
        Write("manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
        Write("selection.json", JsonSerializer.SerializeToUtf8Bytes(selection));
        Write("quest-catalog.json", JsonSerializer.SerializeToUtf8Bytes(questClasses));
        // Last marker means all verified artifacts were staged. This is not an engine commit.
        Write("PREPARED.txt", System.Text.Encoding.UTF8.GetBytes("Verified staging only. No game save or running session was changed.\n"));
    }
}
