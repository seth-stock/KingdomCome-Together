// SPDX-License-Identifier: GPL-3.0-only
// The joiner's durable loot custody (JoinerCustody.cs) and the host's half of its recovery: protocol 16.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    private readonly object _custodyGate = new();
    private JoinerCustody? _custody;
    private long _custodyAskAtMs;
    private int _custodyReceiptCursor;

    /// <summary>Tests point the joiner's custody at their own file; empty keeps it in memory.</summary>
    public string? CustodyPathOverride { get; init; }

    private JoinerCustody Custody()
    {
        lock (_custodyGate)
        {
            if (_custody != null) return _custody;
            string path = CustodyPathOverride ?? Path.Combine(Path.GetDirectoryName(HenryStore.DefaultRoot())!, "loot-custody.jsonl");
            try { _custody = new JoinerCustody(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Console.WriteLine($"MP-LOOT custody journal unavailable ({ex.Message}): new takes refused; preserve the journal");
                throw;
            }
            return _custody;
        }
    }

    internal IReadOnlyList<(JoinerTake Take, string Verdict, Coop.Contract.OpState State)> CustodyOpen => Custody().Open();

    private bool CustodyRecord(JoinerTake t)
    {
        t = t with { World = _joinedTag ?? "" };
        try { Custody().Record(t); return true; }
        catch (Exception ex) { Console.WriteLine($"MP-LOOT custody: take {t.Tok} not journalled ({ex.Message}); request withheld; the mod's probation still applies"); return false; }
    }

    private void CustodyAnswer(string scope, uint tok, string verdict)
    {
        try { Custody().Answer(scope, tok, verdict); } catch (Exception ex) { Console.WriteLine($"MP-LOOT custody answer not journalled: {ex.Message}"); }
    }

    private void CustodySettle(string scope, uint tok, string how)
    {
        bool settled;
        try { settled = Custody().Settle(scope, tok, how); }
        catch (Exception ex) { Console.WriteLine($"MP-LOOT custody settle not journalled: {ex.Message}"); return; }
        if (settled) _ = Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskReceipt, tok, FormattableString.Invariant($"{scope} {tok} {how}"));
    }

    /// <summary>(joiner, once a second in a shared world) Takes of an earlier load: settle the answered ones in the mod, ask the host about the others.</summary>
    private async Task CustodyRecoverAsync()
    {
        IReadOnlyList<(JoinerTake Take, string Verdict, Coop.Contract.OpState State)> open;
        try { open = Custody().Open(); } catch { return; }
        bool ask = Environment.TickCount64 - _custodyAskAtMs >= 10_000;
        if (ask)
        {
            var receipts = Custody().Receipts().Where(r => r.Take.World.Length > 0 && r.Take.World == _joinedTag).ToArray();
            for (int i = 0; i < Math.Min(32, receipts.Length); i++)
            {
                var (take, how) = receipts[(_custodyReceiptCursor + i) % receipts.Length];
                await Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskReceipt, take.Tok,
                    FormattableString.Invariant($"{take.Scope} {take.Tok} {how}"));
            }
            _custodyReceiptCursor = receipts.Length == 0 ? 0 : (_custodyReceiptCursor + Math.Min(32, receipts.Length)) % receipts.Length;
        }
        foreach (var (t, verdict, state) in open)
        {
            // A different host/world cannot decide ownership in the world that created this take.
            // Legacy records lack that evidence and remain preserved for explicit recovery.
            if (t.World.Length == 0 || t.World != _joinedTag) continue;
            if (state is Coop.Contract.OpState.Delivered or Coop.Contract.OpState.RecipientVerified)
            {
                CustodySettle(t.Scope, t.Tok, verdict is "kept" or "returned" or "reverted" ? verdict : "partial");
                continue;
            }
            if (t.Scope == _w134RequestScope) continue;                          // this load's own: the mod's probation rules (20 s, results)
            string v = verdict == "unanswered" ? "gone" : verdict;              // the mod already gave it back before an answer came
            if (v is "ok" or "gone" or "none")
                await ExecLuaAsync(FormattableString.Invariant(
                    $"if KCD2MP_W134Recover then KCD2MP_W134Recover(\"{t.Scope}\",{t.Tok},\"{t.Kind}\",\"{EscapeLua(t.Holder)}\",\"{t.Class}\",{t.Amount},{Wo134Rules.F(t.Health)},{t.Charge},{t.TakeWt:0},\"{v}\") end"));
            else if (ask)
                await Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskRecover, t.Tok, FormattableString.Invariant($"{t.Scope} {t.Tok}"));
        }
        if (ask) _custodyAskAtMs = Environment.TickCount64;
    }

    /// <summary>(host) A joiner's recover query or receipt. Answers from the durable ledger; puts a granted-but-not-kept item back once.</summary>
    private async Task CustodyHostAskAsync(byte src, LootMsg m, string[] f)
    {
        if (!Wo134HostRole) return;
        if (f.Length < 2 || !Guid.TryParseExact(f[0], "N", out _) || !uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint tok)) return;
        string scope = f[0].ToLowerInvariant();
        (string Verdict, DurableLootLedger.Decision? Decision) found;
        try { lock (_durableLootGate) found = DurableLoot().FindDecision(scope, tok); }
        catch (Exception ex) { Console.WriteLine($"MP-LOOT recover: ledger unavailable ({ex.Message}); no answer"); return; }
        if (m.Kind == Protocol.LootAskRecover && f.Length == 2)
        {
            await Wo134SendAsync(Protocol.LootHostUp, src, Protocol.LootHostRecoverResult, tok, $"{scope} {tok} {found.Verdict}", _w134RequestScope);
            Console.WriteLine($"MP-LOOT recover: joiner {src} asked about {scope[..8]}/{tok}: {found.Verdict}");
            return;
        }
        if (m.Kind != Protocol.LootAskReceipt || f.Length != 3 || f[2] is not ("kept" or "returned" or "reverted" or "partial")) return;
        if (found.Verdict != "ok" || found.Decision is null || f[2] is not ("returned" or "reverted")) return;
        // the decision's text: body "ok <body> <cls> <amt>"; container "ok <id> <cls> <n> <category> <charge>"
        var d = found.Decision.Text.Split(' ');
        bool body = found.Decision.Kind == Protocol.LootHostTakeResult && d.Length == 4;
        bool container = found.Decision.Kind == Protocol.LootHostContainerResult && d.Length == 6;
        if (!body && !container) return;
        bool begun;
        lock (_durableLootGate) begun = DurableLoot().BeginRestore(scope, tok);
        if (!begun) return;                                                      // already put back: a repeated receipt changes nothing
        string call = body
            ? FormattableString.Invariant($"if KCD2MP_W134HostRestore then KCD2MP_W134HostRestore(\"body\",\"{EscapeLua(d[1])}\",\"body\",\"{d[2]}\",{int.Parse(d[3], CultureInfo.InvariantCulture)}) end")
            : FormattableString.Invariant($"if KCD2MP_W134HostRestore then KCD2MP_W134HostRestore(\"container\",\"{EscapeLua(d[1])}\",\"{d[4]}\",\"{d[2]}\",{int.Parse(d[3], CultureInfo.InvariantCulture)}) end");
        await ExecLuaAsync(call);
        Console.WriteLine($"MP-LOOT {scope[..8]}/{tok} was granted but joiner {src} did not keep it ({f[2]}): put back into {d[1]}");
    }
}
