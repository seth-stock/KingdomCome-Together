// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-153: the pure half of "the joiner comes along after the host's scene" (docs/WO-153-findings.md).
/// GameBridge.Wo153 owns the wire, the log and the leash call; this owns the rules, and Wo153Tests pins them.
///
/// A scene window is every host-side scene edge from the first start to the last end (scenes nest: a
/// fader inside an ingame sequence). When the window closes, two observable facts decide:
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
    /// <summary>The window's end is read this long after the engine's release (the end placement has run).</summary>
    public const int SettleMs = 2000;
    /// <summary>A window older than this never closed (a load swallowed the end edge): it is dropped, not evaluated.</summary>
    public const int StaleWindowMs = 10 * 60 * 1000;
    /// <summary>The thresholds the cvars accept, in whole metres.</summary>
    public const int MinM = 5, MaxM = 5000;
    /// <summary>A pull this soon after the host's own story scene is worded as the scene's, not as a fast travel.</summary>
    public const int SceneTextWindowS = 30;

    /// <summary>The words (this project's own; the game's text is never used).</summary>
    public static class Text
    {
        public const string JoinerPulledScene = "Your host's scene took them elsewhere; you were brought along.";
    }

    public enum Verdict { None, Relocated, Far }

    /// <param name="storyScene">any scene of the window was a rendered video or an ingame sequence</param>
    /// <param name="hostMovedM">2D distance between where the window opened and where it closed</param>
    /// <param name="farthestJoinerM">2D distance to the farthest joiner with a fresh position; null = none known</param>
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
    /// The window's bookkeeping, pure: starts open it, ends close it, and the last end returns the closed
    /// window for evaluation. Scenes are tracked BY NAME, so a duplicate start (a re-logged edge) counts
    /// once, and an end that matches no running scene (a duplicate release, the guard giving up and the
    /// engine then releasing, a load) closes nothing: it can never close an outer scene early. A window
    /// older than <see cref="StaleWindowMs"/> is dropped on the next edge instead of being evaluated
    /// against a position from long ago.
    /// </summary>
    public sealed class Window
    {
        private readonly HashSet<string> _running = new(StringComparer.Ordinal);
        private long _openedMs;
        private (float X, float Y) _start;
        private bool _story;

        public bool Open => _running.Count > 0;
        public int Depth => _running.Count;

        public void Reset() { _running.Clear(); _story = false; }

        /// <summary>A scene started. Returns true when this edge opened a new window.</summary>
        public bool Start(long nowMs, float x, float y, string type, string name)
        {
            if (_running.Count > 0 && nowMs - _openedMs > StaleWindowMs) Reset();
            bool opened = _running.Count == 0;
            if (opened) { _openedMs = nowMs; _start = (x, y); _story = false; }
            _running.Add(name);
            _story |= IsStoryScene(type);
            return opened;
        }

        /// <summary>
        /// A scene ended. Returns the closed window's facts when it was the last running one; null while
        /// others run, or when no running scene has this name.
        /// </summary>
        public (float StartX, float StartY, bool Story)? End(long nowMs, string name)
        {
            if (_running.Count == 0) return null;
            if (nowMs - _openedMs > StaleWindowMs) { Reset(); return null; }
            if (!_running.Remove(name)) return null;
            if (_running.Count > 0) return null;
            return (_start.X, _start.Y, _story);
        }
    }
}
