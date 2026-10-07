using Coop.Contract;
using KcdMp.Wire;

public sealed class DurableLootLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kcd-loot-tests-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_root, "journal.jsonl");
    private const string Scope = "0123456789abcdef0123456789abcdef";
    [Fact] public void CommittedDecisionSurvivesRestartWithoutClaimingRecipientReceipt()
    {
        using (var ledger = new DurableLootLedger(PathName))
        {
            Assert.Equal(BeginKind.New, ledger.Begin(1, Scope, 7, Protocol.LootAskBodyTake, "body class 1 1").Kind);
            Assert.True(ledger.Complete(1, Scope, 7, new(Protocol.LootHostTakeResult, "ok body class 1")));
            Assert.False(ledger.Complete(1, Scope, 7, new(Protocol.LootHostTakeResult, "gone body class 1")));
        }
        using var reopened = new DurableLootLedger(PathName);
        var replay = reopened.Begin(1, Scope, 7, Protocol.LootAskBodyTake, "body class 1 1");
        Assert.Equal(OpState.HostDecisionComplete, replay.Record.State);
        Assert.Equal("ok body class 1", DurableLootLedger.Replay(replay)!.Text);
        Assert.Equal(0, reopened.QuarantinedCount);
    }
    [Fact] public void LostEngineResultIsQuarantinedAndCannotBeRetried()
    {
        using (var ledger = new DurableLootLedger(PathName))
        {
            Assert.Equal(BeginKind.New, ledger.Begin(1, Scope, 7, 2, "request").Kind);
            Assert.Equal(BeginKind.InProgress, ledger.Begin(1, Scope, 7, 2, "request").Kind);
        }
        using var reopened = new DurableLootLedger(PathName);
        Assert.Equal(BeginKind.Quarantined, reopened.Begin(1, Scope, 7, 2, "request").Kind);
        Assert.False(reopened.Complete(1, Scope, 7, new(2, "ok")));
        Assert.Equal(1, reopened.QuarantinedCount);
    }
    [Fact] public void PayloadKindPeerAndLoadScopeCannotAlias()
    {
        using var ledger = new DurableLootLedger(PathName);
        ledger.Begin(1, Scope, 7, 2, "request");
        Assert.Equal(BeginKind.Conflict, ledger.Begin(1, Scope, 7, 3, "request").Kind);
        Assert.Equal(BeginKind.Conflict, ledger.Begin(1, Scope, 7, 2, "different").Kind);
        Assert.Equal(BeginKind.New, ledger.Begin(2, Scope, 7, 2, "request").Kind);
        Assert.Equal(BeginKind.New, ledger.Begin(1, new string('f', 32), 7, 2, "request").Kind);
    }
    [Fact] public void LoadInvalidationRejectsLateCallbacksAndPreservesFinishedDecisions()
    {
        using var ledger = new DurableLootLedger(PathName);
        ledger.Begin(1, Scope, 7, 2, "pending");
        ledger.Begin(1, Scope, 8, 2, "finished");
        ledger.Complete(1, Scope, 8, new(2, "ok"));
        ledger.InvalidatePending();
        Assert.False(ledger.Complete(1, Scope, 7, new(2, "ok")));
        Assert.Equal(BeginKind.Quarantined, ledger.Begin(1, Scope, 7, 2, "pending").Kind);
        Assert.Equal(BeginKind.Replay, ledger.Begin(1, Scope, 8, 2, "finished").Kind);
    }
    [Fact] public void DamagedJournalFailsClosedAndReleasesTheFileHandle()
    {
        using (var ledger = new DurableLootLedger(PathName)) ledger.Begin(1, Scope, 7, 2, "request");
        File.WriteAllText(PathName, File.ReadAllText(PathName).Replace("loot-2", "loot-3"));
        Assert.Throws<InvalidDataException>(() => new DurableLootLedger(PathName));
        File.Move(PathName, PathName + ".damaged");
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
