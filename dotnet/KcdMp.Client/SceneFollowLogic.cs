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
/// lose the relocation between them. When the window closes, two observable facts decide:
///   * the host ended somewhere else (<see cref="RelocDefaultM"/> or more from where the window opened):
///     a scene that teleports or places the host. Any scene kind counts, faders included;
///   * the host ended a STORY scene (a rendered video or an ingame sequence) with a joiner left
///     <see cref="FarDefaultM"/> or more away: the story goes on where the host stands, in a place the
///     joiner is not. This is the observable stand-in for "the scene locks the host into a gameplay
///     section" -- the engine gives no read of that lock, so a scene that locks the host in place with
///     the joiner already near brings nobody along (and needs nobody).
/// Neither is a distance rule of the leash: the leash (650 m) and its fast-travel jump (200 m) stay as they are.
/// </summary>
public static class SceneFollowLogic
{
    /// <summary>The host moved this far between the window's first start and last end: it was relocated.</summary>
    public const float RelocDefaultM = 25f;
    /// <summary>A story scene ended with the farthest joiner this far from the host: bring them to the story.</summary>
    public const float FarDefaultM = 150f;
    /// <summary>The window closes this long after the last scene ended (the engine's end placement has run, and a chained scene would have started).</summary>
    public const int SettleMs = 2000;
    /// <summary>A timer fires this much early at worst; <see cref="Window.TryClose"/> forgives it.</summary>
    public const int SettleSlackMs = 150;
    /// <summary>
    /// A window with no edge for this long is an orphan (an end the engine never logged): the next START drops it.
    /// An END is never refused for its age: a long scene's real end must still be evaluated.
    /// </summary>
    public const int StaleWindowMs = 30 * 60 * 1000;
    /// <summary>The thresholds the cvars accept, in whole metres.</summary>
    public const int MinM = 5, MaxM = 5000;
    /// <summary>A pull this soon after the host's own story scene ENDED is worded as the scene's, not as a fast travel.</summary>
    public const int SceneTextWindowS = 10;

    /// <summary>The words (this project's own; the game's text is never used).</summary>
    public static class Text
    {
        public const string JoinerPulledScene = "Your host's scene took them elsewhere; you were brought along.";
    }

    public enum Verdict { None, Relocated, Far }

    /// <param name="storyScene">any scene of the window was a rendered video or an ingame sequence</param>
    /// <param name="hostMovedM">2D distance between where the window opened and where it closed</param>
    /// <param name="farthestJoinerM">2D distance to the farthest joiner in the host's world with a fresh position; null = none known</param>
    public static Verdict Decide(bool storyScene, double hostMovedM, double? farthestJoinerM, float relocM, float farM)
    {
        if (double.IsNaN(hostMovedM) || hostMovedM < 0) return Verdict.None;
        if (hostMovedM >= relocM) return Verdict.Relocated;
        if (storyScene && farthestJoinerM is double j && !double.IsNaN(j) && j >= farM) return Verdict.Far;
        return Verdict.None;
    }

    /// <summary>A threshold cvar's value: whole metres in range, else null.</summary>
    public static float? ParseMetres(string? text)
    {
        if (!int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)) return null;
        return n is >= MinM and <= MaxM ? n : null;
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

    /// <summary>
    /// The window's bookkeeping, pure. Scenes are tracked BY NAME: a duplicate start (a re-logged edge) counts
    /// once, and an end that matches no running scene (a duplicate release, the guard giving up and the engine
    /// then releasing, a load) changes nothing, so it can never close an outer scene early.
    ///   Start  opens the window, or continues it (a start inside the quiet period after the last end joins the
    ///          same window, keeping its first position and its story flag);
    ///   End    returns true when it left no scene running: the caller then waits <see cref="SettleMs"/> and asks
    ///          <see cref="TryClose"/>;
    ///   TryClose  closes the window only when nothing runs and the quiet period has passed; later calls (a
    ///          second timer for a window that continued) return null, and the last end's timer closes it.
    ///   Reset  forgets everything (a load or a disconnect: the engine interrupts every scene).
    /// </summary>
    public sealed class Window
    {
        private readonly HashSet<string> _running = new(StringComparer.Ordinal);
        private bool _open;
        private long _lastEdgeMs, _lastEndMs;
        private (float X, float Y) _start;
        private bool _story;

        public bool Open => _open;
        public int Depth => _running.Count;

        public void Reset() { _running.Clear(); _open = false; _story = false; }

        /// <summary>A scene started. Returns true when this edge opened a NEW window.</summary>
        public bool Start(long nowMs, float x, float y, string type, string name)
        {
            if (_open && nowMs - _lastEdgeMs > StaleWindowMs) Reset();   // an orphan: its end was never logged
            bool opened = !_open;
            if (opened) { _open = true; _start = (x, y); _story = false; }
            _running.Add(name);
            _story |= IsStoryScene(type);
            _lastEdgeMs = nowMs;
            return opened;
        }

        /// <summary>A scene ended. True = no scene is running now (the window may close after the quiet period).</summary>
        public bool End(long nowMs, string name)
        {
            if (!_open || !_running.Remove(name)) return false;
            _lastEdgeMs = _lastEndMs = nowMs;
            return _running.Count == 0;
        }

        /// <summary>The window's facts when it may close now, else null.</summary>
        public (float StartX, float StartY, bool Story)? TryClose(long nowMs)
        {
            if (!_open || _running.Count > 0) return null;
            if (nowMs - _lastEndMs < SettleMs - SettleSlackMs) return null;
            _open = false;
            return (_start.X, _start.Y, _story);
        }
    }
}
