// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-153: the pure half of "the joiner comes along after the host's scene" (docs/WO-153-findings.md).
/// GameBridge.Wo153 owns the wire, the log and the leash call; this owns the rules, and Wo153Tests pins them.
///
/// A scene window is every host-side scene edge from the first start to the last end, plus a short quiet
/// period (<see cref="SettleMs"/>): scenes nest (a fader inside an ingame sequence) and chain (one sequence
/// into the next), and a window that closed between two chained scenes would measure each half alone and
/// lose the relocation between them. The host's position is taken at the START of the first scene and at
/// the END of the last one (the engine's release, after its end placement): not at the close, which is
/// 2 s later, with the player free again. When the window closes, two observable facts decide:
///   * the host ended somewhere else (<see cref="RelocDefaultM"/> or more from where the window opened):
///     a scene that teleports or places the host. Any scene kind counts, faders included. Player input is
///     off in every scene kind (WO-149 6.2), so the host does not walk away inside one;
///   * the host ended a STORY scene (a rendered video or an ingame sequence) with a joiner left
///     <see cref="FarDefaultM"/> or more away: the story goes on where the host stands, in a place the
///     joiner is not. This is the observable stand-in for "the scene locks the host into a gameplay
///     section" -- the engine gives no read of that lock, so a scene that locks the host in place with
///     the joiner already near brings nobody along (and needs nobody).
/// Neither is a distance rule of the leash: the leash (650 m) and its fast-travel jump (200 m) stay as they
/// are, and the leash's own rule that a joiner already within 50 m of the host is never pulled applies to
/// every pull this asks for (<see cref="LeashLogic.FastTravelMinM"/>).
/// </summary>
public static class SceneFollowLogic
{
    /// <summary>The host moved this far between the window's first start and last end: it was relocated.</summary>
    public const float RelocDefaultM = 25f;
    /// <summary>A story scene ended with the farthest joiner this far from the host: bring them to the story.</summary>
    public const float FarDefaultM = 150f;
    /// <summary>The window closes this long after the last scene ended (a chained scene would have started by then).</summary>
    public const int SettleMs = 2000;
    /// <summary>A timer fires this much early at worst; <see cref="Window.TryClose"/> forgives it.</summary>
    public const int SettleSlackMs = 150;
    /// <summary>
    /// The host's end position is the first position sample at least this long after the last scene's end edge: by then
    /// the engine's end placement has been applied and the sample is not the poll loop's stale pre-placement one, and the
    /// player has had well under a second of free movement (a gallop covers ~6 m in 0.4 s, against a 25 m threshold).
    /// </summary>
    public const int EndSampleDelayMs = 400;
    /// <summary>
    /// A scene still "running" this long after its start is an orphan (its end edge was never delivered, e.g.
    /// a name the log parser refuses): it is forgotten at the next edge, so it cannot hold a window open for
    /// good. Long enough for any real scene; a scene's real END is never refused for its age.
    /// </summary>
    public const int OrphanSceneMs = 20 * 60 * 1000;
    /// <summary>The thresholds the cvars accept, in whole metres. The far threshold starts at the leash's own 50 m: a joiner nearer than that is never pulled.</summary>
    public const int MinM = 5, MinFarM = 50, MaxM = 5000;
    /// <summary>A pull this soon after the host's own story scene ENDED is worded as the scene's, not as a fast travel.</summary>
    public const int SceneTextWindowS = 10;

    /// <summary>The words (this project's own; the game's text is never used).</summary>
    public static class Text
    {
        public const string JoinerPulledScene = "Your host's scene took them elsewhere; you were brought along.";
    }

    public enum Verdict { None, Relocated, Far }

    /// <param name="storyScene">any scene of the window was a rendered video or an ingame sequence</param>
    /// <param name="hostMovedM">2D distance between the host's position at the window's open and at its last end</param>
    /// <param name="farthestJoinerM">2D distance to the farthest joiner in the host's world with a fresh position; null = none known</param>
    public static Verdict Decide(bool storyScene, double hostMovedM, double? farthestJoinerM, float relocM, float farM)
    {
        if (double.IsNaN(hostMovedM) || hostMovedM < 0) return Verdict.None;
        if (hostMovedM >= relocM) return Verdict.Relocated;
        if (storyScene && farthestJoinerM is double j && !double.IsNaN(j) && j >= farM) return Verdict.Far;
        return Verdict.None;
    }

    /// <summary>A threshold cvar's value: whole metres from <paramref name="min"/> to <see cref="MaxM"/>, else null.</summary>
    public static float? ParseMetres(string? text, int min = MinM)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)) return null;
        return n >= min && n <= MaxM ? n : null;
    }

    public static bool IsStoryScene(string type) => type is "Rendered" or "Ingame";

    /// <summary>
    /// The joiner's wording of a pull: the scene's only when the host's own story scene ENDED a moment ago
    /// (<see cref="SceneTextWindowS"/>). A fast travel a minute later is a fast travel. Never set by a scene's
    /// start: the pull this wording is for comes after the end.
    /// </summary>
    public static bool UseSceneWording(DateTime nowUtc, DateTime hostStorySceneEndedUtc) =>
        hostStorySceneEndedUtc != DateTime.MinValue
        && nowUtc >= hostStorySceneEndedUtc
        && (nowUtc - hostStorySceneEndedUtc).TotalSeconds < SceneTextWindowS;

    /// <summary>The facts of a closed window.</summary>
    public readonly record struct Closed(float StartX, float StartY, float EndX, float EndY, bool Story)
    {
        public double MovedM => Math.Sqrt(Math.Pow(EndX - StartX, 2) + Math.Pow(EndY - StartY, 2));
    }

    /// <summary>
    /// The window's bookkeeping, pure. Scenes are tracked BY NAME with their start time: a duplicate start (a
    /// re-logged edge) counts once, and an end that matches no running scene (a duplicate release, the guard
    /// giving up and the engine then releasing, a load) changes nothing, so it can never close an outer scene
    /// early. A scene running for <see cref="OrphanSceneMs"/> is forgotten at the next edge.
    ///   Start  opens the window, or continues it (a start inside the quiet period after the last end joins the
    ///          same window, keeping its first position and its story flag);
    ///   End    records where the host stood at that end and returns true when it left no scene running: the
    ///          caller then waits <see cref="SettleMs"/> and asks <see cref="TryClose"/>;
    ///   TryClose  closes the window only when nothing runs and the quiet period has passed; later calls (a
    ///          second timer for a window that continued) return null, and the last end's timer closes it.
    ///   Reset  forgets everything (a load or a disconnect: the engine interrupts every scene).
    /// </summary>
    public sealed class Window
    {
        private readonly Dictionary<string, long> _running = new(StringComparer.Ordinal);
        private bool _open, _hasEnd, _awaitSample;
        private long _lastEndMs;
        private (float X, float Y) _start, _end;
        private bool _story;

        public bool Open => _open;
        public int Depth => _running.Count;
        /// <summary>True while the window still wants the position sample that settles its end position (the caller skips the lock otherwise).</summary>
        public bool AwaitingEndSample => _awaitSample;

        public void Reset() { _running.Clear(); _open = false; _hasEnd = false; _awaitSample = false; _story = false; }

        /// <summary>
        /// A host position sample. The first one at least <see cref="EndSampleDelayMs"/> after the last scene's end
        /// edge replaces the provisional end position (the poll loop's last sample at the edge, possibly from before
        /// the engine's placement). True = it did.
        /// </summary>
        public bool NoteSample(long nowMs, float x, float y)
        {
            if (!_awaitSample || nowMs - _lastEndMs < EndSampleDelayMs) return false;
            _end = (x, y); _awaitSample = false;
            return true;
        }

        private void Purge(long nowMs)
        {
            List<string>? old = null;
            foreach (var (n, at) in _running) if (nowMs - at > OrphanSceneMs) (old ??= new()).Add(n);
            if (old is null) return;
            foreach (var n in old) _running.Remove(n);
            if (_running.Count == 0 && !_hasEnd) Reset();   // nothing ever ended in it: no end position to measure
        }

        /// <summary>A scene started. Returns true when this edge opened a NEW window.</summary>
        public bool Start(long nowMs, float x, float y, string type, string name)
        {
            Purge(nowMs);
            // a window with nothing running whose quiet period is long over was never closed (no timer reached it): a new one
            if (_open && _running.Count == 0 && _hasEnd && nowMs - _lastEndMs > SettleMs + SettleSlackMs) Reset();
            bool opened = !_open;
            if (opened) { _open = true; _hasEnd = false; _start = (x, y); _story = false; }
            _running[name] = nowMs;
            _story |= IsStoryScene(type);
            _awaitSample = false;   // a scene runs again: its own end will want its own sample
            return opened;
        }

        /// <summary>A scene ended where the host stands at (x, y). True = no scene is running now (the window may close after the quiet period).</summary>
        public bool End(long nowMs, string name, float x, float y)
        {
            // The named scene's own end first, whatever its age: a scene's real end is never refused as an orphan.
            // Only then are the OTHER scenes that never ended forgotten.
            if (!_open || !_running.Remove(name)) { Purge(nowMs); return false; }
            _lastEndMs = nowMs; _hasEnd = true; _end = (x, y);   // provisional: NoteSample settles it
            _awaitSample = true;
            Purge(nowMs);
            return _running.Count == 0;
        }

        /// <summary>The window's facts when it may close now, else null.</summary>
        public Closed? TryClose(long nowMs)
        {
            if (!_open || !_hasEnd || _running.Count > 0) return null;
            if (nowMs - _lastEndMs < SettleMs - SettleSlackMs) return null;
            _open = false; _awaitSample = false;
            return new Closed(_start.X, _start.Y, _end.X, _end.Y, _story);
        }
    }
}
