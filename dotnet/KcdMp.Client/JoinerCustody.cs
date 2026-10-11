// SPDX-License-Identifier: GPL-3.0-only
// Durable custody of a joiner's takes (docs/CAPABILITIES.md, "Loot", Recovery). A body take (w134_take) or a live container take (w134_ctake) has ALREADY moved
// the item into the joiner's Henry when the host is asked; the mod's probation tables (sess.pend, KCD2MP_Containers.pending) die with every load and crash.
// The joiner's agent keeps the same probation on disk, in the hash-chained OperationJournal, keyed by the request scope and token the host's DurableLootLedger
// uses too, so the host can find its own decision again after either side restarted (LootAskRecover):
//
//   take seen      Begin -> Reserved -> IntentRecorded -> EngineApplying -> EngineVerified   (the move happened; the take's detail is every record's evidence)
//   host answered  LedgerCommitted, outcome ok | gone | none   ("unanswered" when the mod gave it back before an answer came)
//   mod settled    Delivered -> RecipientVerified, outcome kept | returned | reverted | partial -> Complete; a receipt goes to the host (LootAskReceipt)
//
// A take of an earlier load (its scope is not the current request scope) is settled by KCD2MP_W134Recover with the durable answer: a take newer than the
// loaded save is not in it; otherwise ok keeps it and anything else returns it (and a shop's charge is refunded). Nothing unanswered is ever guessed.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coop.Contract;

namespace KcdMp.Client;

public sealed record JoinerTake(string Scope, uint Tok, string Kind, string Holder, string Category, string Class, int Amount, float Health, int Charge, double TakeWt)
{
    public string Json() => JsonSerializer.Serialize(this);
    public static JoinerTake? Parse(string s) { try { return JsonSerializer.Deserialize<JoinerTake>(s); } catch (JsonException) { return null; } }
}

public sealed class JoinerCustody : IDisposable
{
    public const string JournalKind = "joiner-take";
    private readonly OperationJournal? _j;
    private readonly Dictionary<string, (JoinerTake Take, string Verdict, OpState State)> _mem = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public JoinerCustody(string path = "") { if (path.Length > 0) _j = new OperationJournal(path); }

    public static string Key(string scope, uint tok) => FormattableString.Invariant($"jt:{scope}:{tok}");

    private static string Digest(JoinerTake t) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"{t.Kind}|{t.Holder}|{t.Category}|{t.Class}|{t.Amount}|{t.Charge}|{t.TakeWt:0}")))).ToLowerInvariant();

    public JoinerTake Record(JoinerTake t)
    {
        if (!Guid.TryParseExact(t.Scope, "N", out _)) throw new ArgumentException("Invalid take scope.");
        lock (_gate)
        {
            string id = Key(t.Scope, t.Tok);
            if (_j == null) { _mem[id] = (t, "", OpState.EngineVerified); return t; }
            var begin = _j.Begin(id, JournalKind, Digest(t));
            if (begin.Kind != BeginKind.New) return t;                   // a repeated event for the same take: recorded once
            foreach (var s in new[] { OpState.Reserved, OpState.IntentRecorded, OpState.EngineApplying, OpState.EngineVerified }) _j.Advance(id, s, t.Json());
            return t;
        }
    }

    public IReadOnlyList<(JoinerTake Take, string Verdict, OpState State)> Open()
    {
        lock (_gate)
        {
            if (_j == null) return _mem.Values.Where(v => v.State != OpState.Complete).ToList();
            var list = new List<(JoinerTake, string, OpState)>();
            foreach (var r in _j.All())
            {
                if (r.Kind != JournalKind || OperationJournal.IsTerminal(r.State) || r.State == OpState.RecoveryRequired) continue;
                if (JoinerTake.Parse(r.Evidence) is { } t) list.Add((t, r.State >= OpState.LedgerCommitted ? r.Outcome : "", r.State));
            }
            return list;
        }
    }

    public JoinerTake? Find(string scope, uint tok) => Open().Select(o => o.Take).FirstOrDefault(t => t.Scope == scope && t.Tok == tok);

    private (string Verdict, OpState State) StateOf(string id)
    {
        if (_j == null) return _mem.TryGetValue(id, out var m) ? (m.Verdict, m.State) : ("", OpState.Requested);
        var r = _j.Get(id);
        return r == null ? ("", OpState.Requested) : (r.State >= OpState.LedgerCommitted ? r.Outcome : "", r.State);
    }

    private void Move(JoinerTake t, OpState next, string outcome)
    {
        string id = Key(t.Scope, t.Tok);
        if (_j == null) { var m = _mem[id]; _mem[id] = (t, outcome.Length > 0 ? outcome : m.Verdict, next); return; }
        _j.Advance(id, next, t.Json(), outcome);
    }

    /// <summary>The host's answer: ok | gone | none (mine is none: nobody else got anything). Recorded once.</summary>
    public bool Answer(string scope, uint tok, string verdict)
    {
        if (verdict == "mine") verdict = "none";
        if (verdict is not ("ok" or "gone" or "none")) return false;
        lock (_gate)
        {
            var t = Find(scope, tok);
            if (t == null) return false;
            var (v, st) = StateOf(Key(scope, tok));
            if (st >= OpState.LedgerCommitted) return v == verdict;
            Move(t, OpState.LedgerCommitted, verdict);
            return true;
        }
    }

    /// <summary>The mod settled it. Nothing is ever settled as kept without an ok.</summary>
    public bool Settle(string scope, uint tok, string how)
    {
        if (how is not ("kept" or "returned" or "reverted" or "partial")) return false;
        lock (_gate)
        {
            var t = Find(scope, tok);
            if (t == null) return false;
            var (v, st) = StateOf(Key(scope, tok));
            if (st < OpState.LedgerCommitted)
            {
                if (how == "kept") return false;
                Move(t, OpState.LedgerCommitted, "unanswered");
            }
            else if (how == "kept" && v != "ok") return false;
            Move(t, OpState.Delivered, "");
            Move(t, OpState.RecipientVerified, how);
            Move(t, OpState.Complete, how);
            return true;
        }
    }

    public void Dispose() => _j?.Dispose();
}
