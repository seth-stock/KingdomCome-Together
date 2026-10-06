// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-157 -- send the joiner's Henry home (docs/WO-157-findings.md).
///
/// A joiner who brought a character from his own world into a host's shared world can put him back: his Henry, as he is now (or as of
/// the last host save, when the game will not save right now), is spliced into HIS OWN world's newest save and written as a NEW manual
/// save (saveNNN.whs, never over a file). The own save is the "world" of the splice, so his story progress, renown and every other block
/// stay his own; he comes back with what he gained (skills, stats, perks, money, items, equipment). The pure half is HenryHome.cs.
///
/// Commands: <c>mp_henry_home</c> [playlineN/file] (now, any time), <c>mp_henry_home_on_leave on|off</c> (do it whenever this game leaves a
/// host's world; default off: leaving changes nothing at home unless asked).
/// </summary>
public partial class GameBridge
{
    /// <summary>Files this feature placed in a playline: the saves watcher must not call them a leak and move them out.</summary>
    private readonly ConcurrentDictionary<string, byte> _w157Placed = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _w157OnLeave;
    private int _w157Busy;
    private string? _w157LastTag;   // the world last left, so a character can be sent home after the player has already gone back

    public sealed record HomeResult(bool Ok, HenrySource? Save, string Message);

    private static HomeResult HomeFail(string why) { Console.WriteLine("MP-HOME not done: " + why); return new(false, null, why); }

    private void Wo157OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "wo157_home":      // mp_henry_home [playlineN/file]
                _ = Task.Run(async () =>
                {
                    var r = await Wo157SendHomeAsync(string.IsNullOrWhiteSpace(arg) ? null : arg.Trim(), "console");
                    await Wo125ToastAsync(r.Message);
                });
                return;
            case "wo157_copy":      // mp_world_copy [N]
                _ = Task.Run(async () =>
                {
                    var r = await Wo157WorldCopyAsync(string.IsNullOrWhiteSpace(arg) ? null : arg.Trim());
                    await Wo125ToastAsync(r.Message);
                });
                return;
            case "wo157_dlc":       // mp_quest_dlc on|off
            {
                string v = (arg ?? "").Trim().ToLowerInvariant();
                if (v is "on" or "off") Wo137Rules.DlcShared = v == "on";
                Console.WriteLine($"MP-QUEST mp_quest_dlc {(Wo137Rules.DlcShared ? "on" : "off")}: DLC quest changes {(Wo137Rules.DlcShared ? "are shared like every other quest's" : "stay out of the session")}");
                _ = Wo125ToastAsync(Wo137Rules.DlcShared ? "DLC quests are shared with your partner." : "DLC quests are not shared with your partner.");
                return;
            }
            case "wo157_cfg":       // mp_henry_home_on_leave on|off
            {
                string v = (arg ?? "").Trim().ToLowerInvariant();
                if (v is "on" or "off") _w157OnLeave = v == "on";
                Console.WriteLine($"MP-HOME mp_henry_home_on_leave {(_w157OnLeave ? "on" : "off")}");
                _ = Wo125ToastAsync(_w157OnLeave ? "When you leave your host's world, your character goes back to your own world as he is now." : "Leaving your host's world changes nothing in your own world (use mp_henry_home to send your character back).");
                return;
            }
        }
    }

    /// <summary>A header attribute from a save's description (the first bytes only), or null.</summary>
    private static string? Wo157ReadAttr(string path, string attr)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[8];
            if (fs.Read(head, 0, 8) != 8 || BitConverter.ToUInt32(head, 0) != 0xFFFFFFFFu) return null;
            int n = BitConverter.ToInt32(head, 4);
            if (n <= 0 || n > 64 * 1024) return null;
            var desc = new byte[n];
            int got = 0;
            while (got < n) { int r = fs.Read(desc, got, n - got); if (r <= 0) return null; got += r; }
            var m = Regex.Match(Encoding.UTF8.GetString(desc), $@"\b{attr}=""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>At a first join that brought a character: remember which of the player's own worlds he came from (its tag, never its seed).</summary>
    private void Wo157NoteHome(string worldTag, HenryChoice choice)
    {
        try
        {
            if (choice.Mode != "bring" || choice.Save is not { } s) return;
            if (WhsSave.ReadSeedFromFile(s.FullPath) is not uint seed) return;
            _henry.SetHome(worldTag, WhsSave.SeedTag(seed), s.Display);
            Console.WriteLine($"MP-HOME world {worldTag}: this character's home is world {WhsSave.SeedTag(seed)} (brought from {s.Display})");
        }
        catch (Exception ex) { Console.WriteLine($"MP-HOME could not note the home world: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- the Henry, as he is right now

    /// <summary>A QuickSave of this game's copy of the host's world, read for Henry's parts, then moved out of the playline (the WO-125 route).</summary>
    private async Task<(WhsSave.HenryParts? Parts, string Why)> Wo157LiveSnapshotAsync() { var r = await Wo157LiveSnapshotFileAsync(); return (r.Parts, r.Why); }

    /// <summary>The same, and the verified save file's bytes (the whole world with this player's Henry in it) for a world copy.</summary>
    private async Task<(WhsSave.HenryParts? Parts, byte[]? File, string Why)> Wo157LiveSnapshotFileAsync()
    {
        string? saves = ResolveSavesDirForJoin();
        int pl = _lastWorldPlayline;
        if (saves is null || pl < 0) return (null, null, "the playline of this world is not known");
        for (int i = 0; i < 50 && Volatile.Read(ref _snapBusy) != 0; i++) await Task.Delay(100);   // a pairing snapshot may be running
        if (Interlocked.CompareExchange(ref _snapBusy, 1, 0) != 0) return (null, null, "another snapshot is still running");
        HenryStore.LedgerEntry? led = null;
        try
        {
            string dir = Path.Combine(saves, $"playline{pl}");
            int qs = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "quicksave*.whs").Count() : 0;
            if (qs >= SnapshotQuicksaveGuard) return (null, null, $"playline{pl} holds {qs} quicksaves (the engine rotates at 100)");
            await _combat.SaveListAsync(1, pl, "-");
            led = _henry.LedgerAdd(new HenryStore.LedgerEntry { Playline = pl, SinceUtc = DateTime.UtcNow.AddSeconds(-1), WorldTag = _joinedTag ?? "", Kind = "snapshot" });
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_snapGate) _snapWait = (pl, DateTime.UtcNow.AddSeconds(-1), tcs);
            string r = await AskModAsync("KCD2MP_Wo125Snapshot", 6000);
            if (!r.StartsWith("ok=true", StringComparison.Ordinal))
            {
                _henry.LedgerSet(led.Id, e => e.State = "done");
                return (null, null, $"the game would not save right now ({r}); not during a fight or a scene");
            }
            if (await Task.WhenAny(tcs.Task, Task.Delay(20000)) != tcs.Task) return (null, null, "the game's save did not appear within 20 s");
            string path = tcs.Task.Result;
            byte[] bytes = WhsSave.ReadShared(path);
            var v = WhsSave.Verify(bytes);
            _henry.LedgerSet(led.Id, e => { e.File = Path.GetFileName(path); e.Md5 = v.Md5; e.State = "written"; });
            WhsSave.HenryParts? parts = null; string why = "";
            if (!v.Ok) why = "the game's save does not verify: " + v.Reason;
            else
            {
                var c = WhsSave.Inflate(bytes);
                uint? seed = WhsSave.ReadSeed(c.Raw);
                if (seed is not uint s || (_joinedTag is not null && WhsSave.SeedTag(s) != _joinedTag)) why = "the game's save is not of the host's world";
                else if (WhsSave.PlayerOf(c.Raw) is var pw && (!pw.IsKnown || pw.Soul != (_joinedSoul ?? WhsSave.HenrySoul))) why = "the player is not the character this world was joined as right now";
                else parts = WhsSave.PartsFromStream(c.Raw, WhsSave.DescriptionSummary(c.Desc).GetValueOrDefault("BuildInfo") ?? "", WhsSave.HenryParts.OriginSnapshot);
            }
            bool gone = _henry.MoveOut(path, "a send-home snapshot's QuickSave (the host's world)");
            await _combat.SaveListAsync(1, pl, Path.GetFileNameWithoutExtension(path));
            if (gone) _henry.LedgerSet(led.Id, e => e.State = "done");
            return (parts, parts is null ? null : bytes, why);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return (null, null, ex.Message);
        }
        finally
        {
            lock (_snapGate) _snapWait = null;
            Volatile.Write(ref _snapBusy, 0);
        }
    }

    // ---------------------------------------------------------------- the home world

    /// <summary>The player's own saves that can be this Henry's home, newest first (never a copy of the host's world; the same build; he is Henry there).</summary>
    private List<OwnSave> Wo157Candidates(string saves, string build, string? homeTag, string? explicitTarget, out string why)
    {
        why = "";
        var own = OwnSaves(saves, HostSeedForOwn(), l => Console.WriteLine(l));
        if (explicitTarget is not null)
        {
            var m = Regex.Match(explicitTarget, @"^playline([0-4])/([A-Za-z0-9_]+?)(\.whs)?$");
            if (!m.Success) { why = $"'{explicitTarget}' is not playlineN/file (for example playline1/save021)."; return []; }
            string f = m.Groups[2].Value + ".whs";
            var hit = own.Where(s => s.Save.Playline == int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) && string.Equals(s.Save.File, f, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hit.Count == 0) why = $"{explicitTarget} is not one of your own saves (a copy of your host's world is never yours).";
            return hit;
        }
        if (homeTag is null) { why = "This character did not come from one of your worlds (he started fresh), so there is no home to send him to. Name a save: mp_henry_home playlineN/file."; return []; }
        var same = SameBuildSaves(own, build, l => Console.WriteLine(l));
        var home = same.Where(s => WhsSave.ReadSeedFromFile(s.Save.FullPath) is uint sd && WhsSave.SeedTag(sd) == homeTag).ToList();
        if (home.Count == 0) why = "No save of the world this character came from (of the same game version) was found.";
        return home;
    }

    // ---------------------------------------------------------------- a copy of this world, for me

    /// <summary>
    /// mp_world_copy [N]: the shared world as it is now, with this player's character in it, as this player's OWN save in playline N (default:
    /// the first playline with no saves in it) -- to carry on from separately, at another time or place, whatever the host does next.
    /// A new file only; the host and the other players are not told and not affected.
    /// </summary>
    private async Task<HomeResult> Wo157WorldCopyAsync(string? playlineArg)
    {
        if (Interlocked.CompareExchange(ref _w157Busy, 1, 0) != 0) return new(false, null, "Already saving a copy.");
        try
        {
            string? saves = ResolveSavesDirForJoin();
            if (saves is null) return HomeFail("no saves folder");
            bool live = _joinedWorld && _where == GameWhere.World && _jj is null && !_rewinding && _joinedTag is not null && _peerHenryWorld;
            if (!live) return HomeFail("You are not in your host's world right now, so there is no shared world to copy.");
            int pl = -1;
            if (playlineArg is not null && !(int.TryParse(playlineArg, NumberStyles.None, CultureInfo.InvariantCulture, out pl) && pl is >= 0 and <= 4))
                return HomeFail($"'{playlineArg}' is not a playline number (0 to 4).");
            if (pl < 0)
            {
                for (int i = 0; i <= 4 && pl < 0; i++)
                {
                    string d = Path.Combine(saves, $"playline{i}");
                    if (!Directory.Exists(d) || !Directory.EnumerateFiles(d, "*.whs").Any()) pl = i;
                }
                if (pl < 0) return HomeFail("Every playline already holds saves. Name one to add the copy to: mp_world_copy N (it becomes a save of that playline).");
            }
            string dir = Path.Combine(saves, $"playline{pl}");
            var (parts, file, why) = await Wo157LiveSnapshotFileAsync();
            if (file is null || parts is null) return HomeFail($"The copy could not be made: {why}");

            var c = WhsSave.Inflate(file);
            Directory.CreateDirectory(dir);
            var existing = Directory.EnumerateFiles(dir, "*.whs").ToList();
            var headerIds = existing.Where(f => WorldSaved.ParsePath(f) is null || Path.GetFileName(f).Equals("exit.whs", StringComparison.OrdinalIgnoreCase))
                .Select(f => int.TryParse(Wo157ReadAttr(f, "SaveId"), NumberStyles.None, CultureInfo.InvariantCulture, out int i) ? i : 0).ToList();
            int id = HenryHome.NextSaveId(existing.Select(Path.GetFileName)!, headerIds);
            if (id > 999) return HomeFail($"playline{pl} has no free save number (the engine uses three digits)");
            byte[] copy = WhsSave.Deflate(HenryHome.RewriteDescription(c.DescBytes, id, DateTimeOffset.UtcNow), c.Raw, c.FooterTail);
            if (!WhsSave.Verify(copy).Ok) return HomeFail("the copy did not verify, so nothing was written");

            string name = HenryHome.ManualName(id), final = Path.Combine(dir, name + ".whs"), part = final + ".part";
            _w157Placed[final] = 0;
            try
            {
                using (var fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write)) fs.Write(copy);
                File.Move(part, final);
            }
            catch (IOException ex) { try { File.Delete(part); } catch { } _w157Placed.TryRemove(final, out _); return HomeFail($"could not write {name}: {ex.Message}"); }
            var back = WhsSave.ReadShared(final);
            if (!SHA256.HashData(back).AsSpan().SequenceEqual(SHA256.HashData(copy)) || !WhsSave.Verify(back).Ok)
            {
                try { File.Delete(final); } catch { }
                _w157Placed.TryRemove(final, out _);
                return HomeFail($"{name} did not read back as written");
            }
            var listed = await _combat.SaveListAsync(1, pl, name);
            Console.WriteLine($"MP-HOME COPY the shared world (as of now, with your character) saved as playline{pl}/{name} ({back.Length} B, verify ok); rescan listed={(listed is null ? "?" : On(listed.Listed))}");
            return new(true, new HenrySource(pl, name + ".whs", final, ReadSaveTime(final) ?? 0),
                $"A copy of this world, with your character, is saved as playline{pl}/{name}. Load it from the menu any time; your host is not affected.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return HomeFail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally { Volatile.Write(ref _w157Busy, 0); }
    }

    // ---------------------------------------------------------------- send home

    /// <summary>
    /// Build and place the home save. Returns the new save (so a leave can load it) and a sentence for the player.
    /// Never touches an existing file; every failure leaves the player's saves exactly as they were.
    /// </summary>
    private async Task<HomeResult> Wo157SendHomeAsync(string? explicitTarget, string why)
    {
        if (Interlocked.CompareExchange(ref _w157Busy, 1, 0) != 0) return new(false, null, "Already sending your character home.");
        var t0 = DateTime.UtcNow;
        try
        {
            string? saves = ResolveSavesDirForJoin(), tables = TablesPakPath();
            if (saves is null || tables is null) return HomeFail(saves is null ? "no saves folder" : "Tables.pak not found beside kcd.log");
            _questClasses ??= WhsSave.QuestClasses(tables);

            // 1. the Henry
            bool live = _joinedWorld && _where == GameWhere.World && _jj is null && !_rewinding && _joinedTag is not null && _peerHenryWorld;
            string? tag = live ? _joinedTag : (_w157LastTag ?? _henry.MostRecentWorld());
            WhsSave.HenryParts? parts = null; string how = "";
            if (live)
            {
                var (p, w) = await Wo157LiveSnapshotAsync();
                parts = p;
                how = "as he is now";
                if (parts is null) Console.WriteLine($"MP-HOME the live snapshot failed ({w}); using the last stored one");
            }
            if (parts is null && tag is not null && _henry.Snapshots(tag).FirstOrDefault() is { } snap && _henry.Load(snap) is { } stored)
            {
                parts = stored;
                how = $"as of {snap.TakenUtc:yyyy-MM-dd HH:mm} UTC (the last host save)";
            }
            if (parts is null || tag is null) return HomeFail("No character of yours is stored from a shared world yet.");
            if (parts.Soul != WhsSave.HenrySoul) return HomeFail("Your character in this part of the story is Godwin, who exists only in these scenes: there is nothing to send home. Your Henry in your own world is as you left him.");

            // 2. his home world
            var (homeTag, homeNote) = _henry.HomeOf(tag);
            var cands = Wo157Candidates(saves, parts.Build, homeTag, explicitTarget, out string cwhy);
            if (cands.Count == 0) return HomeFail(cwhy);

            // 3. build into the first candidate that takes him
            string lastWhy = cwhy;
            foreach (var cand in cands.Take(8))
            {
                string dir = Path.GetDirectoryName(cand.Save.FullPath)!;
                var headerIds = Directory.EnumerateFiles(dir, "*.whs")
                    .Where(f => WorldSaved.ParsePath(f) is null || Path.GetFileName(f).Equals("exit.whs", StringComparison.OrdinalIgnoreCase))
                    .Select(f => int.TryParse(Wo157ReadAttr(f, "SaveId"), NumberStyles.None, CultureInfo.InvariantCulture, out int i) ? i : 0).ToList();
                int id = HenryHome.NextSaveId(Directory.EnumerateFiles(dir, "*.whs").Select(Path.GetFileName)!, headerIds);
                if (id > 999) { lastWhy = $"playline{cand.Save.Playline} has no free save number (the engine uses three digits)"; continue; }

                HenryHome.Built built;
                try { built = HenryHome.Build(WhsSave.ReadShared(cand.Save.FullPath), parts, _questClasses, id, DateTimeOffset.UtcNow); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
                { lastWhy = $"{cand.Save.Display}: {ex.Message}"; Console.WriteLine("MP-HOME " + lastWhy); continue; }
                if (built.Check.Count > 0)
                {
                    foreach (var f in built.Check.Take(6)) Console.WriteLine("MP-HOME   check FAIL " + f);
                    lastWhy = $"{cand.Save.Display}: the result did not pass its checks"; continue;
                }

                // 4. place it: a new file, never over one; the saves watcher is told to leave it alone
                string final = Path.Combine(dir, built.Name + ".whs"), part = final + ".part";
                _w157Placed[final] = 0;
                try
                {
                    using (var fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write)) fs.Write(built.File);
                    File.Move(part, final);   // throws if the name appeared meanwhile: no overwrite
                }
                catch (IOException ex) { try { File.Delete(part); } catch { } _w157Placed.TryRemove(final, out _); lastWhy = $"could not write {built.Name}: {ex.Message}"; continue; }
                var back = WhsSave.ReadShared(final);
                if (!SHA256.HashData(back).AsSpan().SequenceEqual(SHA256.HashData(built.File)) || !WhsSave.Verify(back).Ok)
                {
                    try { File.Delete(final); } catch { }
                    _w157Placed.TryRemove(final, out _);
                    lastWhy = $"{built.Name} did not read back as written"; continue;
                }
                var listed = await _combat.SaveListAsync(1, cand.Save.Playline, built.Name);
                string display = $"playline{cand.Save.Playline}/{built.Name}";
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-HOME DONE ({why}): the character of world {tag} ({how}) is in your own world {(homeTag ?? "(named)")} as {display}, built from {cand.Save.Display} ({back.Length} B, verify ok, quest items left behind {built.Report.QuestItemsRemoved.Count}, kept {built.Report.QuestItemsAdded.Count}) in {(DateTime.UtcNow - t0).TotalSeconds:F1} s; rescan listed={(listed is null ? "?" : On(listed.Listed))}"));
                var res = new HenrySource(cand.Save.Playline, built.Name + ".whs", final, ReadSaveTime(final) ?? 0);
                return new(true, res, $"Your character is back in your own world as {display} ({how}). Load it from the menu to continue.");
            }
            return HomeFail(lastWhy);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return HomeFail($"{ex.GetType().Name}: {ex.Message}");
        }
        finally { Volatile.Write(ref _w157Busy, 0); }
    }
}
