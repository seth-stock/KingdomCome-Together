// SPDX-License-Identifier: GPL-3.0-only
using KcdMp.Wire;
using Coop.Contract;

namespace KcdMp.Client;

public partial class GameBridge
{
    private readonly object _durableLootGate = new();
    private DurableLootLedger? _durableLoot;
    private readonly Dictionary<string, DurableLootLedger.Decision> _durableLootPending = new(StringComparer.Ordinal);

    private DurableLootLedger DurableLoot()
    {
        if (_durableLoot is not null) return _durableLoot;
        string root = Path.GetDirectoryName(HenryStore.DefaultRoot())!;
        _durableLoot = new(Path.Combine(root, "loot-journal.jsonl"));
        Console.WriteLine($"MP-LOOT durable journal opened; quarantined={_durableLoot.QuarantinedCount}; recipient receipt unverified");
        return _durableLoot;
    }

    private void Wo134InvalidateDurable()
    {
        lock (_durableLootGate)
        {
            _durableLoot?.InvalidatePending();
            _durableLootPending.Clear();
        }
    }

    private async Task<bool> Wo134BeginDurableAsync(byte peer, string scope, LootMsg request, byte replyKind, string expectedSuffix)
    {
        BeginResult result;
        try
        {
            lock (_durableLootGate)
            {
                result = DurableLoot().Begin(peer, scope, request.Tok, request.Kind, request.Text);
                if (result.Kind == BeginKind.New)
                {
                    _durableLootPending.Add(result.Record.OperationId, new(replyKind, expectedSuffix));
                    return true;
                }
            }
            if (DurableLootLedger.Replay(result) is { } replay)
                await Wo134SendAsync(Protocol.LootHostUp, peer, replay.Kind, request.Tok, replay.Text, scope);
            else if (result.Kind == BeginKind.Quarantined)
                await Wo134SendAsync(Protocol.LootHostUp, peer, replyKind, request.Tok, "gone " + expectedSuffix, scope);
            Console.WriteLine($"MP-LOOT {result.Kind}: {result.Record.OperationId}; no engine mutation");
        }
        catch (Exception ex)
        {
            // An unreadable/full/locked journal must never fall back to unjournaled inventory writes.
            Console.WriteLine($"MP-LOOT request refused: durable intent unavailable: {ex.Message}");
        }
        return false;
    }

    private async Task Wo134DurableResultAsync(byte peer, uint token, string scope, byte kind, string text)
    {
        try
        {
            lock (_durableLootGate)
            {
                string id = DurableLootLedger.Id(peer, scope, token);
                int split = text.IndexOf(' ');
                if (!Wo134HostRole || !_durableLootPending.TryGetValue(id, out var expected)
                    || kind != expected.Kind || split < 0 || text[(split + 1)..] != expected.Text) return;
                if (!DurableLoot().Complete(peer, scope, token, new(kind, text))) return;
                _durableLootPending.Remove(id);
            }
            await Wo134SendAsync(Protocol.LootHostUp, peer, kind, token, text, scope);
            if (kind == Protocol.LootHostItemResult && text.StartsWith("ok ", StringComparison.Ordinal))
                foreach (byte g in Wo134Peers().Where(g => g != peer))
                    await Wo134SendAsync(Protocol.LootHostUp, g, Protocol.LootHostItemGone, 0, text[3..]);
        }
        catch (Exception ex) { Console.WriteLine($"MP-LOOT result withheld: durable decision unavailable: {ex.Message}"); }
    }
}
