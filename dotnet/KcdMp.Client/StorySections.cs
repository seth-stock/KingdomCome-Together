// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>How much of a quest keeps the player on rails (WO-156, docs/WO-156-quest-gating.md).</summary>
public enum StoryTier
{
    /// <summary>Open-world content: you go where you like. Its scripted scenes are the cutscene notice's business.</summary>
    Open,
    /// <summary>Scripted stretches alternate with free ones. A period for the friend's choice; no tether (the ordinary leash), a shorter idle time.</summary>
    Mixed,
    /// <summary>Staged for its whole run (a siege, a castle, a tournament, the world locked for the finale). A period, the tether, a long idle time.</summary>
    Rails,
}

/// <summary>
/// One quest root and whether it keeps the player on rails (docs/WO-156-quest-gating.md). Every field of the generated
/// <see cref="QuestCatalog"/> row, read from the game's own quest data and reviewed by hand in docs/WO-156-quest-gating-plan.csv.
/// The quest titles are Warhorse's, cited as identifiers only.
/// </summary>
public sealed record StorySection(string Code, string Quest, string Level, string Title, bool Locked, string Why)
{
    /// <summary>Position in the story (main quests only: the game's own chronological order): a later quest moving means an earlier one is over.</summary>
    public int Order { get; init; }
    public StoryTier Tier { get; init; } = StoryTier.Rails;
    public bool IsMain { get; init; } = true;
    /// <summary>The id of the period this section belongs to (the code of the period's first section); empty for an open quest.</summary>
    public string Period { get; init; } = "";
    /// <summary>Module names: the section is entered only once a State inside one of them changes (the part of the quest that really is staged).</summary>
    public IReadOnlyList<string> Markers { get; init; } = Array.Empty<string>();
    public string Kind { get; init; } = "main";
    /// <summary>The folders the quest is kept in under its level (fight_clubs, kovarske_mikroquesty ...): they are modules on its State paths.</summary>
    public IReadOnlyList<string> Folders { get; init; } = Array.Empty<string>();

    /// <summary>How long the host may do nothing in this section (no new State, no conversation, no scene) before it is taken as over.</summary>
    public int IdleMs => (IsMain, Tier) switch
    {
        (true, StoryTier.Rails) => 40 * 60 * 1000,
        (true, _) => 12 * 60 * 1000,
        (false, StoryTier.Rails) => 15 * 60 * 1000,
        _ => 8 * 60 * 1000,
    };
}

/// <summary>WO-155: a locked stretch of the story. <paramref name="Id"/> = the code of its first section.</summary>
public sealed record StoryPeriod(string Id, string Why, IReadOnlyList<string> Codes);

/// <summary>
/// The sections: the 32 main quests in the game's own order, and every side quest, task, activity and event that keeps the player on rails.
/// Built from <see cref="QuestCatalog"/> (generated from the game's data by tools/Build-QuestCatalog.py, with the classification in
/// docs/WO-156-quest-gating-plan.csv); nothing here is typed by hand.
/// </summary>
public static class StorySections
{
    private static StorySection Make(QuestRoot q)
    {
        var tier = q.Tier switch { "rails" => StoryTier.Rails, "mixed" => StoryTier.Mixed, _ => StoryTier.Open };
        bool main = q.Kind == "main";
        var folders = q.Folders.Length == 0 ? Array.Empty<string>() : q.Folders.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // A directory named like the quest holds its modules; it is the quest's own module, not another segment of its path.
        if (folders.Length > 0 && string.Equals(folders[^1], q.Name, StringComparison.OrdinalIgnoreCase)) folders = folders[..^1];
        return new StorySection(q.Code, q.Name, q.Level, q.Title, tier != StoryTier.Open, tier == StoryTier.Open ? "" : q.Why)
        {
            Tier = tier,
            IsMain = main,
            Kind = q.Kind,
            Period = q.Period,
            Folders = folders,
            Markers = q.Markers.Length == 0 ? Array.Empty<string>() : q.Markers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            Order = main ? q.Order - 1 : 0,
        };
    }

    /// <summary>The 32 main quests, in the order the game plays them (locked or not).</summary>
    public static IReadOnlyList<StorySection> All { get; } =
        QuestCatalog.All.Where(q => q.Kind == "main").OrderBy(q => q.Order).Select(Make).ToArray();

    /// <summary>Every other quest that keeps the player on rails (tier mixed or rails): side quests, tasks, activities, events, DLC.</summary>
    public static IReadOnlyList<StorySection> Others { get; } =
        QuestCatalog.All.Where(q => q.Kind != "main" && q.Tier != "open").Select(Make).ToArray();

    /// <summary>The main quests and the others that lock, for lookups.</summary>
    public static IReadOnlyList<StorySection> Everything { get; } = All.Concat(Others).ToArray();

    private static readonly Dictionary<string, StorySection> ByQuestName =
        Everything.ToDictionary(s => s.Quest, StringComparer.OrdinalIgnoreCase);

    /// <summary>EVERY quest root of the game, the open ones too, by name: only to tell which quest a path is deepest in (see <see cref="FromPath"/>).</summary>
    private static readonly Dictionary<string, List<StorySection>> AllByName =
        QuestCatalog.All.Select(Make).GroupBy(s => s.Quest, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

    public static StorySection? ByQuest(string? quest) =>
        quest is not null && ByQuestName.TryGetValue(quest, out var s) ? s : null;

    public static StorySection? ByCode(string? code) =>
        Everything.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

    // ---- periods -- what a friend chooses about ----
    // A friend does not choose per quest: the final act is nine quests and one stretch of story. A PERIOD is the set of locked sections
    // that share a period id in the plan (the opening is three quests; the wedding runs into Trosky). A side quest is its own period.

    /// <summary>The locked stretches of the story: the main ones in story order, then every other quest's.</summary>
    public static IReadOnlyList<StoryPeriod> Periods { get; } = BuildPeriods();

    private static readonly Dictionary<string, StoryPeriod> PeriodByCode = Periods.SelectMany(p => p.Codes.Select(c => (c, p)))
        .ToDictionary(x => x.c, x => x.p, StringComparer.OrdinalIgnoreCase);

    /// <summary>The period a section belongs to; null for an open quest or an unknown code.</summary>
    public static StoryPeriod? PeriodOf(string? code) => code is not null && PeriodByCode.TryGetValue(code, out var p) ? p : null;

    private static IReadOnlyList<StoryPeriod> BuildPeriods()
    {
        var order = new List<string>();
        var members = new Dictionary<string, List<StorySection>>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Everything.Where(x => x.Locked))
        {
            if (!members.TryGetValue(s.Period, out var l)) { members[s.Period] = l = new List<StorySection>(); order.Add(s.Period); }
            l.Add(s);
        }
        return order.Select(id => new StoryPeriod(id, members[id][0].Why, members[id].Select(s => s.Code).ToArray())).ToArray();
    }

    /// <summary>
    /// The quest a quest-State path belongs to: "Barbora.trosecko.svatba.hibernovana_cast.x" -> the svatba section. A main quest is the
    /// path's third segment. Any other quest is found by its whole address: the level, the folders the game keeps it in (they are modules
    /// on its paths: "Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh"), then its name -- all of them, in order, so a module that merely
    /// shares a name with some quest ("zaby", "drak", "archery") is never taken for it. A quest nested inside another quest's module is the
    /// DEEPER one (the task "A Moment of Fame" lives in a fight club's folder: its States are the task's, not the fight club's).
    /// An open side quest, task, activity or event, or a path nothing names: null.
    /// </summary>
    public static StorySection? FromPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var seg = path.Split('.', 12);
        if (seg.Length < 3 || !string.Equals(seg[0], "Barbora", StringComparison.Ordinal)) return null;
        StorySection? found = null;
        for (int i = 2; i < Math.Min(seg.Length, 9); i++)   // the deepest quest in the game's data sits behind four folders
        {
            if (!AllByName.TryGetValue(seg[i], out var cands)) continue;
            foreach (var s in cands)
            {
                if (!string.Equals(s.Level, seg[1], StringComparison.OrdinalIgnoreCase)) continue;
                if (s.IsMain) { if (i == 2) return s; continue; }
                if (AddressMatches(s, seg, i)) found = s;   // keep looking: a quest nested in another quest's folder is the deeper one
            }
        }
        if (found is null || !found.Locked) return null;
        return found;
    }

    /// <summary>The quest's folders appear, in order, between the level and its name at <paramref name="at"/> (a folder that is not a module may be missing); a quest with no folders must be the root.</summary>
    private static bool AddressMatches(StorySection s, string[] seg, int at)
    {
        if (s.Folders.Count == 0) return at == 2;
        int f = 0;
        for (int i = 2; i < at && f < s.Folders.Count; i++)
            if (string.Equals(seg[i], s.Folders[f], StringComparison.OrdinalIgnoreCase)) f++;
        return f == s.Folders.Count;
    }

    /// <summary>A State whose path names the quest's end: the section is over.</summary>
    public static bool IsEndPath(string? path) =>
        path is not null && path.Contains("endQuest", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a module of <paramref name="markers"/> is one of the path's segments (below the quest root).</summary>
    public static bool HasMarker(string? path, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrEmpty(path) || markers.Count == 0) return false;
        var seg = path.Split('.');
        for (int i = 3; i < seg.Length; i++)
            foreach (var m in markers)
                if (string.Equals(seg[i], m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}

/// <summary>
/// WO-153/156: which section the HOST is in, from the quest States it changes (pure; Wo153StoryTests and Wo156Tests pin it).
///   * EVIDENCE. A main RAILS quest is entered by its first State. A MIXED quest (and a later open main quest, to count as "the story
///     moved on") needs a burst: <see cref="MixedChanges"/> distinct States within <see cref="SustainedWindowMs"/> -- a scripted
///     stretch is a burst, a lone background tick or a repeating timer is not. A side quest, task or activity on rails needs
///     <see cref="SideRailsChanges"/> distinct States. A quest with markers is entered only by a State inside one of its marker modules.
///   * A LATER main quest that shows evidence ends the earlier section (an earlier quest's background States keep ticking and are
///     ignored: they would make the section flap), and the story is then "at" it.
///   * Another kind of quest enters its own section, unless the host is in a main RAILS section (its world is locked: nothing else can
///     be going on there); it replaces another section only when that one has been quiet for <see cref="ReplaceQuietMs"/> (the host is
///     not actively in it), and so does the host's current main quest when its staged part begins while a side section is open.
///   * A State whose path names the quest's end leaves it. Nothing NEW from the section (no new State, no conversation, no scene) for its
///     idle time leaves it: a timer cycling through the same few States does not keep a section alive.
///   * A side quest, task or activity is not entered again for a while after it ended (<see cref="CompletedCooldownMs"/>) or idled out
///     (<see cref="IdleCooldownMs"/>): its cleanup and background timers keep ticking.
///   * A load, a disconnect or a world change forgets it (<see cref="Reset"/>).
/// </summary>
public sealed class StoryLock
{
    /// <summary>A mixed quest is entered once this many distinct States of it changed within <see cref="SustainedWindowMs"/>.</summary>
    public const int MixedChanges = 3;
    /// <summary>A side quest, task or activity on rails: this many.</summary>
    public const int SideRailsChanges = 2;
    public const int SustainedWindowMs = 90_000;
    /// <summary>A section replaces another only when that one has been quiet this long.</summary>
    public const int ReplaceQuietMs = 30_000;
    /// <summary>After a side quest ended: its cleanup States tick for a moment. A repeatable bout (a fight club) can be entered again soon after.</summary>
    public const int CompletedCooldownMs = 90_000;
    /// <summary>After a side quest idled out: its background timers keep ticking, possibly for long.</summary>
    public const int IdleCooldownMs = 10 * 60 * 1000;
    private const int RefreshMemory = 8;

    /// <summary>
    /// The leash's warning and pull distances while a rails section holds the joiners close: the tighter of the host's own and the
    /// tether (pull = the tether, warning 30 m inside it but never under the leash's 50 m floor). Off: the host's own, unchanged.
    /// </summary>
    public static (float WarnM, float PullM) Tether(float warnM, float pullM, bool on, float tetherM)
    {
        if (!on) return (warnM, pullM);
        float pull = Math.Min(pullM, tetherM);
        float warn = Math.Min(warnM, Math.Max(50f, tetherM - 30f));
        if (warn >= pull) warn = Math.Max(1f, pull - 1f);   // the leash needs the warning below the pull, whatever the host's own numbers
        return (warn, pull);
    }

    public enum Kind { None, Enter, Leave }
    public readonly record struct Transition(Kind Kind, StorySection? Section, string Reason);

    private enum Why { Completed, Idle, Advanced, Replaced }

    private StorySection? _active;
    private long _lastMs;
    private readonly List<string> _refreshed = new();   // the last few States that kept the active section alive

    public StorySection? Active => _active;

    // The furthest the host has got (the highest main-quest order that showed evidence), and the main sections it finished.
    // Both stop an earlier quest's leftover States -- a cleanup node after the quest ended, a background timer of a quest long
    // behind -- from re-entering a section the host is no longer in.
    private int _maxOrder = -1;
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _cooldown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(long At, string Path)>> _recent = new(StringComparer.Ordinal);

    public void Reset() { _active = null; _maxOrder = -1; _done.Clear(); _cooldown.Clear(); _recent.Clear(); _refreshed.Clear(); }

    private void Close(StorySection a, long nowMs, Why why)
    {
        if (a.IsMain) { if (why is Why.Completed or Why.Advanced) _done.Add(a.Code); }
        else if (why == Why.Completed) _cooldown[a.Code] = nowMs + CompletedCooldownMs;
        else if (why == Why.Idle) _cooldown[a.Code] = nowMs + IdleCooldownMs;
    }

    private void Refresh(string? path, long nowMs)
    {
        if (path is null || _refreshed.Contains(path)) return;   // a timer cycling through the same States is not the host doing something
        _refreshed.Add(path);
        if (_refreshed.Count > RefreshMemory) _refreshed.RemoveAt(0);
        _lastMs = nowMs;
    }

    /// <summary>Does this State show the host is really in the quest (see the class comment)? Records it.</summary>
    private bool Evidence(StorySection s, string? path, long nowMs)
    {
        int need = s.IsMain && s.Tier == StoryTier.Rails ? 1 : !s.IsMain && s.Tier == StoryTier.Rails ? SideRailsChanges : MixedChanges;
        if (need == 1) return true;
        if (!_recent.TryGetValue(s.Code, out var l)) _recent[s.Code] = l = new List<(long, string)>();
        l.RemoveAll(x => nowMs - x.At > SustainedWindowMs);
        l.Add((nowMs, path ?? ""));
        return l.Select(x => x.Path).Distinct().Count() >= need;
    }

    /// <summary>One quest State change of the host. Returns what changed: nothing, a Leave, an Enter, or a Leave then an Enter (the host moved from one section to the next).</summary>
    public IReadOnlyList<Transition> Note(string? path, long nowMs)
    {
        var s = StorySections.FromPath(path);
        if (s is null) return Array.Empty<Transition>();
        var outl = new List<Transition>(2);
        bool end = StorySections.IsEndPath(path);
        if (s.IsMain && _done.Contains(s.Code)) return outl;   // a quest the host finished: what it still changes is cleanup, not the host being in it
        if (!s.IsMain && _cooldown.TryGetValue(s.Code, out long until) && nowMs < until) return outl;

        if (_active is { } a && s.Code == a.Code)
        {
            if (end) { _active = null; Close(a, nowMs, Why.Completed); outl.Add(new(Kind.Leave, a, "completed")); return outl; }
            Refresh(path, nowMs);
            return outl;
        }
        if (end)   // the end of a quest the host is not in: it will not come back
        {
            if (s.IsMain) _done.Add(s.Code); else _cooldown[s.Code] = nowMs + CompletedCooldownMs;
            return outl;
        }

        bool evidence = Evidence(s, path, nowMs);
        if (s.IsMain)
        {
            if (s.Order < _maxOrder) return outl;                       // an earlier quest's background State: nothing
            if (s.Order > _maxOrder)                                    // the story moved on -- once the later quest shows it
            {
                if (!evidence) return outl;
                _maxOrder = s.Order;
                if (_active is { } old)
                {
                    _active = null;
                    Close(old, nowMs, Why.Advanced);
                    outl.Add(new(Kind.Leave, old, "moved on"));
                }
            }
            else if (_active is { IsMain: false } && nowMs - _lastMs < ReplaceQuietMs) return outl;   // the host's current quest ticking while it is busy in a side section
        }
        else if (_active is { } cur)
        {
            if (cur.IsMain && cur.Tier == StoryTier.Rails) return outl;   // a locked world has no room for a side quest's timers
            if (nowMs - _lastMs < ReplaceQuietMs) return outl;            // the host is busy in the section it is in
        }

        if (!s.Locked || !evidence) return outl;
        if (s.Markers.Count > 0 && !StorySections.HasMarker(path, s.Markers)) return outl;   // the quest's open part

        if (_active is { } prev)   // another section the host has been quiet in
        {
            _active = null;
            Close(prev, nowMs, Why.Replaced);
            outl.Add(new(Kind.Leave, prev, "moved on"));
        }
        _active = s; _lastMs = nowMs; _refreshed.Clear();
        if (path is not null) _refreshed.Add(path);
        outl.Add(new(Kind.Enter, s, ""));
        return outl;
    }

    /// <summary>The host did something in the section that is not a quest State (a conversation, a scene): it is still there.</summary>
    public void Touch(long nowMs) { if (_active is not null) _lastMs = nowMs; }

    /// <summary>Called about once a second: a section the host has not touched for its idle time is over.</summary>
    public Transition? Tick(long nowMs)
    {
        if (_active is { } a && nowMs - _lastMs > a.IdleMs)
        {
            _active = null;
            Close(a, nowMs, Why.Idle);
            return new Transition(Kind.Leave, a, "idle");
        }
        return null;
    }
}
