using Coop.Persistence;
using System.Text;

namespace KcdMp.Client.Tests;
public sealed class ReconcileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coop-reconcile-" + Guid.NewGuid().ToString("N"));
    private readonly string _world = Guid.NewGuid().ToString("N"), _participant = Guid.NewGuid().ToString("N");
    private ReconcileService Service => new(_root);
    private byte[] World => WhsSaveTests.SyntheticSave();
    private byte[] Character => WhsSave.SerializeBlock(WhsSave.PartsFromFile(World, "save"));
    private ReconcileService.CaptureCharacter Own(string? id = null) => new(id ?? _participant, "Henry", Character, Encoding.UTF8.GetBytes("{\"Entries\":[]}"));
    private CheckpointManifest Capture(string? parent = null, IReadOnlyList<ReconcileService.CaptureCharacter>? characters = null) =>
        Service.Capture(_world, Guid.NewGuid().ToString("N"), parent, CheckpointStore.Hash([8]), World, characters ?? [Own()]);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact] public void DescendantSelectionStagesTheExactPairedCharacterAndBothBranchesRemain()
    {
        var a = Capture(); var b = Capture(a.CheckpointId);
        var plan = Service.Select(a.CheckpointId, b.CheckpointId); Assert.Equal(b.CheckpointId, plan.SelectedId);
        string output = Path.Combine(_root, "prepared"); Service.Prepare(plan, output, WhsSaveTests.Quest);
        Assert.Equal(World, File.ReadAllBytes(Path.Combine(output, "world.whs")));
        Assert.Equal(Character, File.ReadAllBytes(Path.Combine(output, _participant + ".hblk")));
        Assert.True(WhsSave.Verify(File.ReadAllBytes(Path.Combine(output, _participant + ".whs"))).Ok);
        Assert.True(File.Exists(Path.Combine(output, "PREPARED.txt")));
        Assert.Equal(2, new CheckpointStore(_root).Checkpoints().Count());
    }
    [Fact] public void DivergenceRequiresExplicitChoice()
    {
        var parent = Capture(); var a = Capture(parent.CheckpointId); var b = Capture(parent.CheckpointId);
        Assert.Throws<InvalidOperationException>(() => Service.Select(a.CheckpointId, b.CheckpointId));
        Assert.Equal(b.CheckpointId, Service.Select(a.CheckpointId, b.CheckpointId, b.CheckpointId).SelectedId);
        Assert.Throws<ArgumentException>(() => Service.Select(a.CheckpointId, b.CheckpointId, parent.CheckpointId));
    }
    [Fact] public void IncompleteParticipantSetIsRejectedWithoutSubstitutingACharacter()
    {
        var a = Capture(characters: [Own(), Own(Guid.NewGuid().ToString("N"))]);
        var b = Capture(a.CheckpointId);
        Assert.Throws<InvalidOperationException>(() => Service.Select(a.CheckpointId, b.CheckpointId));
        Assert.Equal(2, new CheckpointStore(_root).Checkpoints().Count());
    }
    [Fact] public void InvalidWorldCharacterAndLedgerCannotPublish()
    {
        Assert.Throws<InvalidDataException>(() => Service.Capture(_world, Guid.NewGuid().ToString("N"), null, CheckpointStore.Hash([8]), [1, 2], [Own()]));
        Assert.Throws<InvalidDataException>(() => Capture(characters: [Own() with { CharacterKind = "Godwin" }]));
        Assert.Throws<InvalidDataException>(() => Capture(characters: [Own() with { ChestLedger = Encoding.UTF8.GetBytes("{}") }]));
        Assert.Empty(new CheckpointStore(_root).Checkpoints());
    }
    [Fact] public void PreparationDoesNotOverwriteExistingOutput()
    {
        var a = Capture(); var selection = Service.Select(a.CheckpointId, a.CheckpointId);
        string output = Path.Combine(_root, "prepared"); Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "keep.txt"), "original");
        Assert.Throws<IOException>(() => Service.Prepare(selection, output, WhsSaveTests.Quest));
        Assert.Equal("original", File.ReadAllText(Path.Combine(output, "keep.txt")));
    }
    [Fact] public void DifferentPlaythroughCannotBecomeADescendantUnderTheSameManifestWorldId()
    {
        var a = Capture(); byte[] other = WhsSaveTests.File(new WhsSaveTests.Spec { Seed = 999 });
        var own = Own() with { CharacterBlock = WhsSave.SerializeBlock(WhsSave.PartsFromFile(other, "save")) };
        Assert.Throws<InvalidDataException>(() => Service.Capture(_world, Guid.NewGuid().ToString("N"), a.CheckpointId,
            CheckpointStore.Hash([8]), other, [own]));
    }
    [Fact] public void TwoParticipantsCannotOwnTheSameItemInstance()
    {
        var pair = WhsSaveTests.Pair(); byte[] world = WhsSaveTests.File(pair.Host);
        byte[] block = WhsSave.SerializeBlock(WhsSave.PartsFromFile(world, "save"));
        var first = Own() with { CharacterBlock = block };
        var second = first with { ParticipantId = Guid.NewGuid().ToString("N") };
        Assert.Throws<InvalidDataException>(() => Service.Capture(_world, Guid.NewGuid().ToString("N"), null,
            CheckpointStore.Hash([8]), world, [first, second]));
        Assert.Empty(new CheckpointStore(_root).Checkpoints());
    }
}
