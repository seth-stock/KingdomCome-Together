// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// Session 10: causal quest XP (KCD2). The host reads the plugin's XP observer, groups the quest-reward rows by the quest change they
/// ran under and sends each group once (LootHost QuestXp); a joiner measures what its own engine paid when it applied that change and
/// gives only the difference. See QuestXp.cs and native xpread.h. Not run with two real players: one real game and a synthetic peer.
/// </summary>
public partial class GameBridge
{
    private readonly QuestXpHost _xpHost = new();
    private readonly QuestXpGuest _xpGuest = new();
    private readonly object _xpLock = new();
    private readonly ConcurrentDictionary<uint, long> _xpShared = new();   // host change seqs that were sent to the partners
    private uint _xpCursor;
    private bool _xpPrimed;
    private int _xpGiven, _xpGivenSlots, _xpLostRows;

    private void XpNoteSharedChange(uint seq)
    {
        if (seq == 0) return;
        long now = Environment.TickCount64;
        _xpShared[seq] = now;
        if (_xpShared.Count > 2048)
            foreach (var old in _xpShared.Where(p => now - p.Value > 300_000).Select(p => p.Key).ToArray()) _xpShared.TryRemove(old, out _);
    }

    private void XpOnJoinerApplied(uint seq, bool changed, long startMs)
    {
        if (seq == 0) return;
        lock (_xpLock)
        {
            if (changed) _xpGuest.Applied(seq, startMs, Environment.TickCount64);
            else _xpGuest.AlreadyThere(seq, Environment.TickCount64);
        }
    }

    private void XpReset()
    {
        lock (_xpLock) { _xpHost.Clear(); _xpGuest.Clear(); }
        _xpShared.Clear();
        _xpPrimed = false;
    }

    /// <summary>The 1 s loop: read the observer, send the host's finished groups, settle the joiner's.</summary>
    private async Task Wo137XpTickAsync(bool host, bool joinerActive, long now)
    {
        bool active = (host && LivePartners().Count > 0) || joinerActive;
        if (!active) { if (_xpPrimed) XpReset(); return; }
        for (int pass = 0; pass < 4; pass++)
        {
            var r = await _combat.Wo137XpReadAsync(_xpPrimed ? _xpCursor : uint.MaxValue);
            if (r is not { } batch) return;                              // not armed (an old DLL) or no answer: nothing is shared, nothing is guessed
            if (!_xpPrimed) { _xpPrimed = true; _xpCursor = batch.Now; break; }   // history before the session is not ours
            if (batch.Lost > 0) { Interlocked.Add(ref _xpLostRows, (int)batch.Lost); Console.WriteLine($"MP-W137 xp: the observer's ring dropped {batch.Lost} row(s) before they were read"); }
            lock (_xpLock)
            {
                if (host) _xpHost.Feed(batch.Rows, now); else _xpGuest.NoteNative(batch.Rows, now);
            }
            _xpCursor = batch.Rows.Count > 0 ? batch.Rows[^1].Seq + 1 : batch.Now;
            if (batch.Rows.Count < 12) break;
        }
        if (host)
        {
            List<(uint Seq, Dictionary<XpKey, uint> Totals)> done;
            lock (_xpLock) done = _xpHost.Take(now, 1500, seq => _xpShared.ContainsKey(seq));
            foreach (var (seq, totals) in done)
            {
                string text = QuestXpRules.Text(seq, totals);
                foreach (byte peer in Wo134Peers())
                    await Wo134SendAsync(Protocol.LootHostUp, peer, Protocol.LootHostQuestXp, 0, text, _w134RequestScope);
                Console.WriteLine($"MP-W137 xp host: quest change #{seq} paid {XpBody(seq, totals)} -> {Wo134Peers().Count} joiner(s)");
            }
        }
        else
        {
            List<(uint Seq, Dictionary<XpKey, uint> Give, Dictionary<XpKey, uint> Host, Dictionary<XpKey, uint> Native)> ready;
            lock (_xpLock) ready = _xpGuest.Settle(now);
            foreach (var s in ready)
            {
                string hostText = XpBody(s.Seq, s.Host), ownText = s.Native.Count == 0 ? "nothing" : XpBody(s.Seq, s.Native);
                if (s.Give.Count == 0)
                {
                    Console.WriteLine($"MP-W137 xp joiner: change #{s.Seq}: host {hostText}, own engine paid {ownText} -- nothing owed");
                    continue;
                }
                Interlocked.Increment(ref _xpGiven); Interlocked.Add(ref _xpGivenSlots, s.Give.Count);
                Console.WriteLine($"MP-W137 xp joiner: change #{s.Seq}: host {hostText}, own engine paid {ownText} -- giving {XpBody(s.Seq, s.Give)}");
                await ExecLuaAsync($"if KCD2MP_QuestXpMode then KCD2MP_QuestXpMode(\"{_hostContainerScope}\") end; if KCD2MP_QuestXpGive then KCD2MP_QuestXpGive(\"q{s.Seq}\", {QuestXpRules.Lua(s.Give)}) end");
            }
        }
    }

    /// <summary>The host's amounts for one quest change reached this joiner.</summary>
    private void XpOnHostAmounts(string text)
    {
        if (!QuestXpRules.TryParse(text, out uint seq, out var totals)) { Console.WriteLine("MP-W137 xp joiner: a malformed quest-xp frame was dropped"); return; }
        lock (_xpLock) _xpGuest.HostAmounts(seq, totals, Environment.TickCount64);
    }

    private static string XpBody(uint seq, IReadOnlyDictionary<XpKey, uint> totals)
    {
        string t = QuestXpRules.Text(seq, totals);
        return t[(t.IndexOf(' ') + 1)..];
    }

    private string XpStatsLine()
    {
        lock (_xpLock)
            return FormattableString.Invariant($"MP-W137 xp: host sent={_xpHost.Sent} unshared={_xpHost.Unshared} unattributed={_xpHost.Unattributed}; joiner settled={_xpGuest.Settled} expired={_xpGuest.Expired} gave={_xpGiven} slots={_xpGivenSlots}; lostRows={_xpLostRows}");
    }
}
