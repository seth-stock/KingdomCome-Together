// SPDX-License-Identifier: GPL-3.0-only
// Durable joiner custody (JoinerCustody.cs) and the host ledger's recovery helpers (DurableLootLedger.FindDecision / BeginRestore), protocol 16.
using Coop.Contract;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class JoinerCustodyTests : IDisposable
{
    private const string ScopeA = "0123456789abcdef0123456789abcdef";
    private const string ScopeB = "fedcba9876543210fedcba9876543210";
    private const string Apple = "2264f217-590e-4c0f-a4c6-f50c6532b9f6";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kcdmp-custody-" + Guid.NewGuid().ToString("N")[..8]);
    public JoinerCustodyTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string F(string name) => Path.Combine(_dir, name);
    private static JoinerTake Take(string scope, uint tok, double wt = 6000) => new(scope, tok, "container", "chest_1", "chest", Apple, 1, 1f, 0, wt);

    [Fact]
    public void A_take_survives_a_restart_with_its_answer_and_settles_once()
    {
        using (var c = new JoinerCustody(F("c.jsonl")))
        {
            c.Record(Take(ScopeA, 7));
            Assert.True(c.Answer(ScopeA, 7, "gone"));
        }
        using (var c = new JoinerCustody(F("c.jsonl")))
        {
            var open = Assert.Single(c.Open());
            Assert.Equal("gone", open.Verdict);
            Assert.Equal((uint)7, open.Take.Tok);
            Assert.True(c.Settle(ScopeA, 7, "returned"));
            Assert.Empty(c.Open());
            Assert.False(c.Settle(ScopeA, 7, "returned"));                 // completed: nothing twice
        }
    }

    [Fact]
    public void Nothing_is_settled_as_kept_without_an_ok_and_mine_counts_as_none()
    {
        using var c = new JoinerCustody(F("c.jsonl"));
        c.Record(Take(ScopeA, 1));
        Assert.False(c.Settle(ScopeA, 1, "kept"));
        Assert.True(c.Answer(ScopeA, 1, "mine"));
        Assert.Equal("none", c.Open().Single().Verdict);
        Assert.False(c.Answer(ScopeA, 1, "ok"));                          // a different second answer is refused
        Assert.False(c.Settle(ScopeA, 1, "kept"));
    }

    [Fact]
    public void A_take_given_back_before_any_answer_completes_as_returned_unanswered()
    {
        using var c = new JoinerCustody(F("c.jsonl"));
        c.Record(Take(ScopeA, 2));
        Assert.True(c.Settle(ScopeA, 2, "returned"));
        Assert.Empty(c.Open());
    }

    [Fact]
    public void A_repeated_take_event_is_recorded_once_and_bad_scopes_are_refused()
    {
        using var c = new JoinerCustody(F("c.jsonl"));
        c.Record(Take(ScopeA, 3));
        c.Record(Take(ScopeA, 3));
        Assert.Single(c.Open());
        Assert.Throws<ArgumentException>(() => c.Record(Take("not-a-scope", 4)));
    }

    [Fact]
    public void The_host_finds_its_decision_by_scope_and_token_whatever_connection_asked()
    {
        using var l = new DurableLootLedger(F("host.jsonl"));
        var req = new LootMsg(Protocol.LootAskContainerTake, 9, "chest_1 " + Apple + " 1 1.0000 chest 0");
        Assert.Equal(BeginKind.New, l.Begin(3, ScopeA, 9, req.Kind, req.Text).Kind);
        Assert.True(l.Complete(3, ScopeA, 9, new DurableLootLedger.Decision(Protocol.LootHostContainerResult, "ok chest_1 " + Apple + " 1 chest 0")));
        var (v, d) = l.FindDecision(ScopeA, 9);
        Assert.Equal("ok", v);
        Assert.Equal(Protocol.LootHostContainerResult, d!.Kind);
        Assert.Equal(("none", (DurableLootLedger.Decision?)null), l.FindDecision(ScopeB, 9));     // never seen here
    }

    [Fact]
    public void An_interrupted_host_decision_is_answered_gone_never_granted()
    {
        string path = F("host.jsonl");
        using (var l = new DurableLootLedger(path)) l.Begin(3, ScopeA, 11, Protocol.LootAskBodyTake, "body_1 " + Apple + " 1 1.0000");
        using var again = new DurableLootLedger(path);                                            // restart: quarantined
        Assert.Equal("gone", again.FindDecision(ScopeA, 11).Verdict);
    }

    [Fact]
    public void A_put_back_happens_once_even_across_a_restart()
    {
        string path = F("host.jsonl");
        using (var l = new DurableLootLedger(path))
        {
            Assert.True(l.BeginRestore(ScopeA, 5));
            Assert.False(l.BeginRestore(ScopeA, 5));
        }
        using var again = new DurableLootLedger(path);
        Assert.False(again.BeginRestore(ScopeA, 5));
    }

    [Fact]
    public void Protocol_16_appends_the_recover_and_receipt_kinds()
    {
        Assert.Equal(16, Protocol.Version);
        Assert.Equal("recover", Protocol.LootAskName(Protocol.LootAskRecover));
        Assert.Equal("receipt", Protocol.LootAskName(Protocol.LootAskReceipt));
        Assert.Equal("recover-result", Protocol.LootHostName(Protocol.LootHostRecoverResult));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_repeated_take_preserves_its_answer_and_conflicting_metadata_is_refused(bool durable)
    {
        using var c = new JoinerCustody(durable ? F("repeat.jsonl") : "");
        var take = Take(ScopeA, 40) with { World = "world-a" };
        c.Record(take); c.Answer(ScopeA, 40, "gone"); c.Record(take);
        Assert.Equal("gone", Assert.Single(c.Open()).Verdict);
        Assert.Throws<InvalidDataException>(() => c.Record(take with { Health = .5f }));
        Assert.Throws<InvalidDataException>(() => c.Record(take with { World = "world-b" }));
    }

    [Fact]
    public void Completed_receipts_keep_world_identity_and_survive_a_process_restart()
    {
        string path = F("receipts.jsonl");
        using (var c = new JoinerCustody(path))
        {
            c.Record(Take(ScopeA, 41) with { World = "world-a" }); c.Answer(ScopeA, 41, "ok"); c.Settle(ScopeA, 41, "kept");
        }
        using var restarted = new JoinerCustody(path);
        Assert.Empty(restarted.Open());
        var receipt = Assert.Single(restarted.Receipts());
        Assert.Equal("world-a", receipt.Take.World); Assert.Equal("kept", receipt.How);
    }

    [Fact]
    public void A_corrupt_journal_is_preserved_and_never_opened_as_volatile_custody()
    {
        string path = F("corrupt.jsonl");
        File.WriteAllText(path, "{invalid complete record}\n"); var original = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => new JoinerCustody(path));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(OpState.Delivered)]
    [InlineData(OpState.RecipientVerified)]
    public void Interrupted_settlement_continues_forward_without_repeating_engine_work(OpState interrupted)
    {
        string path = F("interrupted.jsonl"); var take = Take(ScopeA, 42);
        using (var c = new JoinerCustody(path)) { c.Record(take); c.Answer(ScopeA, 42, "gone"); }
        using (var j = new OperationJournal(path))
        {
            j.Advance(JoinerCustody.Key(ScopeA, 42), OpState.Delivered, take.Json(), "returned");
            if (interrupted == OpState.RecipientVerified) j.Advance(JoinerCustody.Key(ScopeA, 42), interrupted, take.Json(), "returned");
        }
        using var restarted = new JoinerCustody(path);
        Assert.True(restarted.Settle(ScopeA, 42, "returned")); Assert.Empty(restarted.Open()); Assert.Single(restarted.Receipts());
    }

    [Fact]
    public void Partial_return_is_quarantined_and_not_advertised_as_complete()
    {
        string path = F("partial.jsonl");
        using (var c = new JoinerCustody(path)) { c.Record(Take(ScopeA, 43)); Assert.False(c.Settle(ScopeA, 43, "partial")); }
        using var restarted = new JoinerCustody(path);
        Assert.Equal(1, restarted.QuarantinedCount); Assert.Empty(restarted.Open()); Assert.Empty(restarted.Receipts());
    }
}
