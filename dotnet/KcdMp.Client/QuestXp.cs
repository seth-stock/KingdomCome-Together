// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text;

namespace KcdMp.Client;

/// <summary>
/// Session 10: causal quest XP, the KCD2 half. The plugin's XP observer (native xpread.h) records every C_StatXPEffect /
/// C_SkillXPEffect Apply with its call stack; a row whose stack runs through C_RPGQuest::OnStatXPReward / OnSkillXPReward is a
/// QUEST's reward. These rules decide what crosses between players: the host's quest-reward XP for one quest change, minus what
/// the joiner's own engine paid for the same change (the same graph runs there when the host's change is applied), once.
/// </summary>
public readonly record struct XpRow(uint Seq, bool Skill, ulong Soul, uint Index, uint Requested, uint Applied, uint Before,
                                    uint AgeMs, byte Cause, uint Root, uint Tag);

/// <summary>One XP slot: a stat or a skill, by the engine's id (Libs/Tables/rpg/stat.xml, skill.xml).</summary>
public readonly record struct XpKey(bool Skill, uint Index) : IComparable<XpKey>
{
    public int CompareTo(XpKey o) => Skill != o.Skill ? Skill.CompareTo(o.Skill) : Index.CompareTo(o.Index);
}

public static class QuestXpRules
{
    /// <summary>Amounts are the engine's fixed-point XP (1/128); a single reward is never anywhere near 1000 XP.</summary>
    public const uint MaxAmount = 1000 * 128;
    public const int MaxEntries = 12;

    // The stats and skills a quest can pay that exist in the game's tables. storyProgress (stat 8) is deliberately absent: it is
    // world-owned (the host's value is written at the splice; each copy grants its own completions), so it never crosses.
    private static readonly HashSet<uint> Stats = new() { 0, 1, 2, 3, 9 };
    private static readonly HashSet<uint> Skills = new()
    {
        0, 1, 2, 3, 4, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16, 17, 19, 20, 22, 23, 24, 26, 27, 28, 29, 30, 31, 32, 33, 34,
    };

    public static bool Known(bool skill, uint index) => (skill ? Skills : Stats).Contains(index);

    /// <summary>A row that belongs to a quest's reward and may be shared.</summary>
    public static bool Shares(in XpRow r) => r.Cause is 1 or 2 && r.Requested > 0 && r.Requested <= MaxAmount && Known(r.Skill, r.Index);

    public static string Text(uint seq, IReadOnlyDictionary<XpKey, uint> totals)
    {
        var sb = new StringBuilder();
        sb.Append(seq.ToString(CultureInfo.InvariantCulture)).Append(' ');
        bool first = true;
        foreach (var (k, v) in totals.OrderBy(p => p.Key))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(k.Skill ? 'k' : 's').Append(k.Index.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    public static bool TryParse(string text, out uint seq, out Dictionary<XpKey, uint> totals)
    {
        seq = 0; totals = new();
        var parts = text.Split(' ');
        if (parts.Length != 2 || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out seq) || seq == 0) return false;
        var entries = parts[1].Split(',');
        if (entries.Length is 0 or > MaxEntries) return false;
        foreach (var e in entries)
        {
            int colon = e.IndexOf(':');
            if (colon < 2 || e[0] is not ('s' or 'k')) return false;
            if (!uint.TryParse(e.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out uint idx) ||
                !uint.TryParse(e.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out uint amount)) return false;
            var key = new XpKey(e[0] == 'k', idx);
            if (!Known(key.Skill, idx) || amount == 0 || amount > MaxAmount || !totals.TryAdd(key, amount)) return false;
        }
        return true;
    }

    /// <summary>What this machine still lacks: host total minus its own native total, per slot, only where positive.</summary>
    public static Dictionary<XpKey, uint> Difference(IReadOnlyDictionary<XpKey, uint> host, IReadOnlyDictionary<XpKey, uint> native)
    {
        var give = new Dictionary<XpKey, uint>();
        foreach (var (k, h) in host)
        {
            native.TryGetValue(k, out uint n);
            if (h > n) give[k] = h - n;
        }
        return give;
    }

    /// <summary>The Lua table KCD2MP_QuestXpGive takes: {{skill, id, amount}, ...}.</summary>
    public static string Lua(IReadOnlyDictionary<XpKey, uint> give) =>
        "{" + string.Join(",", give.OrderBy(p => p.Key).Select(p => FormattableString.Invariant($"{{{(p.Key.Skill ? "true" : "false")},{p.Key.Index},{p.Value}}}"))) + "}";

    internal static void Add(Dictionary<XpKey, uint> into, XpKey key, uint amount)
    {
        into.TryGetValue(key, out uint cur);
        into[key] = (uint)Math.Min((ulong)cur + amount, uint.MaxValue);
    }
}

/// <summary>
/// The host's side: quest-reward rows are grouped by the quest change they ran under (the plugin stamps the outermost recorded
/// State change as the row's root); a group nobody has added to for a moment is complete and goes out once.
/// </summary>
public sealed class QuestXpHost
{
    private sealed class Bucket { public Dictionary<XpKey, uint> Totals = new(); public long LastMs; }
    private readonly Dictionary<uint, Bucket> _buckets = new();
    public int Unattributed { get; private set; }
    public int Unshared { get; private set; }
    public int Sent { get; private set; }

    public void Feed(IEnumerable<XpRow> rows, long nowMs)
    {
        foreach (var r in rows)
        {
            if (r.Cause is not (1 or 2)) continue;
            if (!QuestXpRules.Shares(r)) continue;                  // storyProgress and unknown slots stay where they were paid
            if (r.Root == 0) { Unattributed++; continue; }          // paid by something that was not a State change: not attributable
            if (!_buckets.TryGetValue(r.Root, out var b)) _buckets[r.Root] = b = new Bucket();
            QuestXpRules.Add(b.Totals, new XpKey(r.Skill, r.Index), r.Requested);
            b.LastMs = nowMs;
        }
        if (_buckets.Count > 256)
            foreach (var old in _buckets.Where(p => nowMs - p.Value.LastMs > 120_000).Select(p => p.Key).ToArray()) _buckets.Remove(old);
    }

    /// <summary>The complete groups (quiet for <paramref name="quietMs"/>), each once. Groups whose change was never shared with a partner are dropped.</summary>
    public List<(uint Seq, Dictionary<XpKey, uint> Totals)> Take(long nowMs, long quietMs, Func<uint, bool> shared)
    {
        var done = new List<(uint, Dictionary<XpKey, uint>)>();
        foreach (var (seq, b) in _buckets.ToArray())
        {
            if (nowMs - b.LastMs < quietMs) continue;
            _buckets.Remove(seq);
            if (!shared(seq)) { Unshared++; continue; }
            if (b.Totals.Count > QuestXpRules.MaxEntries) continue;
            Sent++;
            done.Add((seq, b.Totals));
        }
        return done;
    }

    public void Clear() => _buckets.Clear();
}

/// <summary>
/// The joiner's side. It measures what its OWN engine paid in quest-reward XP around each application of the host's change
/// (rows stamped with that apply's tag, or rows landing in its time window), and when the host's amounts for that change arrive
/// it gives only the difference. A change this copy already had (the apply moved nothing) is never settled: this copy's own
/// engine paid, or deliberately did not pay, when it reached that state itself.
/// </summary>
public sealed class QuestXpGuest
{
    private readonly record struct Native(long AtMs, uint Tag, XpKey Key, uint Requested);
    private readonly List<Native> _native = new();
    private readonly Dictionary<uint, (Dictionary<XpKey, uint> Totals, long RecvMs)> _host = new();
    private readonly Dictionary<uint, (long StartMs, long EndMs)> _applied = new();
    private readonly Dictionary<uint, long> _unchanged = new();
    public int Settled { get; private set; }
    public int Expired { get; private set; }

    public void NoteNative(IEnumerable<XpRow> rows, long nowMs)
    {
        foreach (var r in rows)
            if (r.Cause is 1 or 2 && QuestXpRules.Known(r.Skill, r.Index) && r.Requested > 0)
                _native.Add(new Native(nowMs - r.AgeMs, r.Tag, new XpKey(r.Skill, r.Index), r.Requested));
        _native.RemoveAll(n => nowMs - n.AtMs > 180_000);
    }

    public void HostAmounts(uint seq, Dictionary<XpKey, uint> totals, long nowMs)
    {
        if (_unchanged.ContainsKey(seq)) return;            // this copy already had that state: nothing is owed for it
        _host[seq] = (totals, nowMs);
    }

    /// <summary>The apply of this change moved nothing here (the state was already reached): the host's amounts for it are not ours to add.</summary>
    public void AlreadyThere(uint seq, long nowMs)
    {
        _host.Remove(seq);
        _unchanged[seq] = nowMs;
        if (_unchanged.Count > 512)
            foreach (var old in _unchanged.Where(p => nowMs - p.Value > 180_000).Select(p => p.Key).ToArray()) _unchanged.Remove(old);
    }

    public void Applied(uint seq, long startMs, long endMs) => _applied[seq] = (startMs, endMs);

    public List<(uint Seq, Dictionary<XpKey, uint> Give, Dictionary<XpKey, uint> Host, Dictionary<XpKey, uint> Native)> Settle(
        long nowMs, long settleMs = 3500, long expireMs = 120_000)
    {
        var ready = new List<(uint, Dictionary<XpKey, uint>, Dictionary<XpKey, uint>, Dictionary<XpKey, uint>)>();
        foreach (var (seq, host) in _host.ToArray())
        {
            if (!_applied.TryGetValue(seq, out var win))
            {
                if (nowMs - host.RecvMs > expireMs) { _host.Remove(seq); Expired++; }   // the apply never happened here: nothing is owed
                continue;
            }
            if (nowMs < win.EndMs + settleMs) continue;
            var native = new Dictionary<XpKey, uint>();
            foreach (var n in _native)
                if (n.Tag == seq || (n.AtMs >= win.StartMs - 300 && n.AtMs <= win.EndMs + 3000)) QuestXpRules.Add(native, n.Key, n.Requested);
            _host.Remove(seq); _applied.Remove(seq);
            Settled++;
            var give = QuestXpRules.Difference(host.Totals, native);
            ready.Add((seq, give, host.Totals, native));
        }
        foreach (var old in _applied.Where(p => nowMs - p.Value.EndMs > expireMs).Select(p => p.Key).ToArray()) _applied.Remove(old);
        return ready;
    }

    public void Clear() { _native.Clear(); _host.Clear(); _applied.Clear(); _unchanged.Clear(); }
}
