// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-153: one main quest and whether it LOCKS the player into a staged part of the story (docs/WO-153-findings.md).
/// The codes, internal names, levels and titles are the repo's own registry (docs/WO-94-mainquest-registry.csv); the
/// quest titles are Warhorse's, cited as identifiers only.
/// </summary>
public sealed record StorySection(string Code, string Quest, string Level, string Title, bool Locked, string Why)
{
    /// <summary>Position in the story (the table's order): a later quest moving means an earlier one is over.</summary>
    public int Order { get; init; }
}

/// <summary>The 32 main quests, in story order.</summary>
public static class StorySections
{
    // Locked = the story stages the scene in one place and holds the player there (a ceremony, a castle, a siege, a staged
    // mission). The engine gives no read of that lock; this table is the maintainers' judgement from the quest data
    // (docs/WO-149-cutscene-census.md: FilterInput locks, LockUp, player switches, scripted fights) and the user's list.
    private static readonly StorySection[] Table =
    {
        new("M01", "prepadeni", "trosecko", "Easy Riders", false, ""),
        new("M02", "zachrana", "trosecko", "Fortuna", false, ""),
        new("M03", "socky", "trosecko", "Laboratores", false, ""),
        new("M05", "svatba", "trosecko", "Wedding Crashers", true, "the wedding in Semine"),
        new("M06", "naTroskach", "trosecko", "For Whom the Bell Tolls", true, "Trosky castle"),
        new("M07", "nebakovPruzkum", "trosecko", "Back in the Saddle", false, ""),
        new("M08", "mucirna", "trosecko", "Necessary Evil", false, ""),
        new("M09", "utokNaNebakov", "trosecko", "For Victory!", false, ""),
        new("M10", "bohutovaVlozka", "trosecko", "Divine Messenger", false, ""),
        new("M11", "nebakovObrana", "trosecko", "The Finger of God", false, ""),
        new("M12", "vezniNaTroskach", "trosecko", "Storm", true, "Trosky castle"),
        new("M30", "posledniPomazani", "kutnohorsko", "Last Rites", true, "the move to Kuttenberg"),
        new("M31", "prijezdNaSuchdol", "kutnohorsko", "The Sword and the Quill", true, "arrival in Kuttenberg"),
        new("M32", "sedmStatecnych", "kutnohorsko", "Speak of the Devil", true, "the devil's job"),
        new("M33", "hledaniLichtenstejna", "kutnohorsko", "Into the Underworld", false, ""),
        new("M34", "kralovskeStribro", "kutnohorsko", "Via Argentum", false, ""),
        new("M35", "zachranaPtacka", "kutnohorsko", "Taking French Leave", false, ""),
        new("M37a", "setkaniVRatbori1", "kutnohorsko", "The King's Gambit", false, ""),
        new("M37b", "setkaniVRatbori2", "kutnohorsko", "The Feast", false, ""),
        new("M38", "sedmStatecnych2", "kutnohorsko", "The Devil's Pack", true, "the devil's job"),
        new("M42", "pogrom", "kutnohorsko", "Exodus", true, "the burning of the Jewish quarter"),
        new("M44a", "zikmunduvTabor", "kutnohorsko", "The Lion's Den", true, "Sigismund's camp"),
        new("M44b", "utokNaMalesov", "kutnohorsko", "Dancing with the Devil", true, "the devil's job"),
        new("M45", "papezskyLegat", "kutnohorsko", "Oratores", true, "the cardinal"),
        new("M46", "prepadeniVlasskehoDvora", "kutnohorsko", "The Italian Job", true, "the Italian Job"),
        new("M47", "erik", "kutnohorsko", "Civitas Pragensis", true, "the final set"),
        new("M48a", "oblehaniSuchdole", "kutnohorsko", "So it begins…", true, "the final set"),
        new("M48b", "rutinaAVypad", "kutnohorsko", "Besieged", true, "the final set"),
        new("M48c", "hladAZmar", "kutnohorsko", "Hunger and Despair", true, "the final set"),
        new("M49", "stealthMiseZaJindru", "kutnohorsko", "Reckoning", true, "the final set"),
        new("M50", "zoufalaObranaZaBohutu", "kutnohorsko", "Last Rites", true, "the final set"),
        new("M51", "finale", "kutnohorsko", "Judgement Day", true, "the final set"),
    };

    public static IReadOnlyList<StorySection> All { get; } = Table.Select((s, i) => s with { Order = i }).ToArray();

    private static readonly Dictionary<string, StorySection> ByQuestName =
        All.ToDictionary(s => s.Quest, StringComparer.OrdinalIgnoreCase);

    public static StorySection? ByQuest(string? quest) =>
        quest is not null && ByQuestName.TryGetValue(quest, out var s) ? s : null;

    public static StorySection? ByCode(string? code) =>
        All.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The main quest a quest-State path belongs to: "Barbora.trosecko.svatba.hibernovana_cast.x" -> the svatba section.
    /// The path's third segment is the quest root; a side quest, an event or a DLC root is not in the table: null.
    /// </summary>
    public static StorySection? FromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var seg = path.Split('.', 4);
        if (seg.Length < 3 || !string.Equals(seg[0], "Barbora", StringComparison.Ordinal)) return null;
        var s = ByQuest(seg[2]);
        return s is not null && string.Equals(s.Level, seg[1], StringComparison.OrdinalIgnoreCase) ? s : null;
    }

    /// <summary>A State whose path names the quest's end: the section is over.</summary>
    public static bool IsEndPath(string? path) =>
        path is not null && path.Contains("endQuest", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// WO-153: which locked section the HOST is in, from the quest States it changes (pure; Wo153StoryTests pins it).
///   * A change in a LOCKED main quest enters its section (and refreshes it while in it).
///   * A change in a LATER main quest, locked or not, means the earlier section is over (an earlier quest's background
///     States keep ticking and are ignored: they would make the section flap).
///   * A change whose path names the quest's end leaves it.
///   * Nothing from the section for <see cref="IdleMs"/> leaves it.
///   * A load, a disconnect or a world change forgets it (<see cref="Reset"/>).
/// </summary>
public sealed class StoryLock
{
    public const int IdleMs = 25 * 60 * 1000;

    /// <summary>
    /// The leash's warning and pull distances while a locked section holds the joiners close: the tighter of the host's own and the
    /// tether (pull = the tether, warning 30 m inside it but never under the leash's 50 m floor). Off: the host's own, unchanged.
    /// </summary>
    public static (float WarnM, float PullM) Tether(float warnM, float pullM, bool on, float tetherM)
    {
        if (!on) return (warnM, pullM);
        float pull = Math.Min(pullM, tetherM);
        float warn = Math.Min(warnM, Math.Max(50f, tetherM - 30f));
        if (warn >= pull) warn = Math.Max(50f, pull - 1f);   // the leash needs the warning below the pull
        return (warn, pull);
    }

    public enum Kind { None, Enter, Leave }
    public readonly record struct Transition(Kind Kind, StorySection? Section, string Reason);

    private StorySection? _active;
    private long _lastMs;

    public StorySection? Active => _active;

    public void Reset() { _active = null; }

    /// <summary>One quest State change of the host. Returns what changed: nothing, a Leave, an Enter, or a Leave then an Enter (the host moved from one locked section to the next).</summary>
    public IReadOnlyList<Transition> Note(string? path, long nowMs)
    {
        var s = StorySections.FromPath(path);
        if (s is null) return Array.Empty<Transition>();
        var outl = new List<Transition>(2);
        if (_active is { } a)
        {
            if (s.Code == a.Code)
            {
                if (StorySections.IsEndPath(path)) { _active = null; outl.Add(new(Kind.Leave, a, "completed")); return outl; }
                _lastMs = nowMs;
                return outl;
            }
            if (s.Order <= a.Order) return outl;   // an earlier quest's background State: nothing
            _active = null;
            outl.Add(new(Kind.Leave, a, "moved on"));
        }
        if (s.Locked && !StorySections.IsEndPath(path))
        {
            _active = s; _lastMs = nowMs;
            outl.Add(new(Kind.Enter, s, ""));
        }
        return outl;
    }

    /// <summary>Called about once a second: a section the host has not touched for <see cref="IdleMs"/> is over.</summary>
    public Transition? Tick(long nowMs)
    {
        if (_active is { } a && nowMs - _lastMs > IdleMs)
        {
            _active = null;
            return new Transition(Kind.Leave, a, "idle");
        }
        return null;
    }
}
