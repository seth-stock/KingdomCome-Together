// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coop.Contract;

namespace KcdMp.Client;

/// <summary>Durable host decisions, not a claim that the recipient kept an item.
/// Unobserved engine outcomes are quarantined on restart. No automatic retry or eviction.</summary>
public sealed class DurableLootLedger : IDisposable
{
    public sealed record Decision(byte Kind, string Text);
    private readonly OperationJournal _journal;
    private readonly object _gate = new();
    public DurableLootLedger(string path) { _journal = new(path); _journal.Recover(); }
    public static string Id(byte peer, string scope, uint token)
    {
        if (!Guid.TryParseExact(scope, "N", out _)) throw new ArgumentException("Invalid loot scope.");
        return FormattableString.Invariant($"lt:{peer}:{scope.ToLowerInvariant()}:{token}");
    }
    public BeginResult Begin(byte peer, string scope, uint token, byte kind, string payload)
    {
        lock (_gate)
        {
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind.ToString(CultureInfo.InvariantCulture) + "\n" + payload))).ToLowerInvariant();
            var result = _journal.Begin(Id(peer, scope, token), "loot-" + kind.ToString(CultureInfo.InvariantCulture), digest);
            if (result.Kind == BeginKind.New)
            {
                _journal.Advance(result.Record.OperationId, OpState.Reserved);
                _journal.Advance(result.Record.OperationId, OpState.IntentRecorded, "validated request; host authority owns the mutation");
                _journal.Advance(result.Record.OperationId, OpState.EngineApplying, "durable before queuing the Lua call");
            }
            return result;
        }
    }
    public bool Complete(byte peer, string scope, uint token, Decision decision)
    {
        lock (_gate)
        {
            string id = Id(peer, scope, token);
            var current = _journal.Get(id);
            if (current?.State != OpState.EngineApplying) return false;
            _journal.Advance(id, OpState.EngineVerified, "scoped Lua result after inventory/entity readback", JsonSerializer.Serialize(decision));
            _journal.Advance(id, OpState.LedgerCommitted, "host decision persisted before any reply");
            _journal.Advance(id, OpState.HostDecisionComplete, "host decision only; recipient delivery is unverified");
            return true;
        }
    }
    public static Decision? Replay(BeginResult result) => result.Kind == BeginKind.Replay
        && result.Record.State == OpState.HostDecisionComplete ? JsonSerializer.Deserialize<Decision>(result.Record.Outcome) : null;
    public void InvalidatePending() { lock (_gate) _journal.Recover(); }
    public int QuarantinedCount => _journal.Quarantined().Count;
    public void Dispose() => _journal.Dispose();
}
