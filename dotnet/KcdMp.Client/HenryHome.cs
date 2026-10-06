// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-157: send a Henry home. The joiner's Henry, as he is in the host's world, is put back into the joiner's own world as a new,
/// ordinary manual save (<c>saveNNN.whs</c>): the OWN world's save is the "world" of the splice, the shared-world Henry is the
/// source. It is the same splice WO-124 does the other way round, so the same rules hold, mirrored:
///   * the home world keeps its own story progress, renown, soul list and every other block (CheckParts proves it);
///   * the Henry arrives with his skills, stats, perks, money, equipment and items;
///   * quest items of the SHARED world's quests never come along (they mean nothing at home); the home Henry's own quest items
///     stay (QuestItemMode.Host), so a home quest is not broken by the trip;
///   * the home save's file is never touched: this writes a NEW file, never over an existing one.
/// The pure half (this file) is tested without a game; the game half is GameBridge.Wo157.cs.
/// </summary>
public static class HenryHome
{
    public sealed record Built(byte[] File, WhsSave.SpliceReport Report, int SaveId, string Name, List<string> Check);

    private static readonly Regex NumberedRx = new(@"^(autosave|quicksave|permanent|save)(\d+)\.whs$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The next id in a playline: the engine numbers autosaves, quicksaves, permanent and manual saves from one counter
    /// (observed: <c>autosave527 permanent526 save543</c> in one playline, an exit save carrying 582 in its header). One more than
    /// the highest seen in the file names and in <paramref name="headerIds"/> (the SaveId of files that carry no number, e.g. exit.whs).
    /// </summary>
    public static int NextSaveId(IEnumerable<string> fileNames, IEnumerable<int>? headerIds = null)
    {
        int max = 0;
        foreach (var n in fileNames)
            if (NumberedRx.Match(Path.GetFileName(n)) is { Success: true } m && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v > max) max = v;
        foreach (var h in headerIds ?? []) if (h > max) max = h;
        return max + 1;
    }

    /// <summary><c>save</c> + the id, three digits at least, as the engine writes it (save543, save059).</summary>
    public static string ManualName(int saveId) => "save" + saveId.ToString("D3", CultureInfo.InvariantCulture);

    /// <summary>
    /// The header of the home world's save, turned into the header of a new manual save: SaveType, SaveId, SaveTime and the
    /// UIDescription that repeats them (<c>type|id|quest|objective|location|time|dd/MM/yyyy HH:mm|hours|</c>) -- everything else
    /// (the level, the quest label, the build, the DLC list) is the home save's, so the new save reads as "the same moment, saved by hand".
    /// </summary>
    public static byte[] RewriteDescription(byte[] descBytes, int saveId, DateTimeOffset when, TimeZoneInfo? local = null)
    {
        string desc = Encoding.UTF8.GetString(descBytes);
        long unix = when.ToUnixTimeSeconds();
        string text = TimeZoneInfo.ConvertTime(when, local ?? TimeZoneInfo.Local).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        string id = saveId.ToString(CultureInfo.InvariantCulture), t = unix.ToString(CultureInfo.InvariantCulture);
        desc = ReplaceAttr(desc, "SaveType", "ManualSave");
        desc = ReplaceAttr(desc, "SaveId", id);
        desc = ReplaceAttr(desc, "SaveTime", t);
        desc = Regex.Replace(desc, @"\bUIDescription=""([^""]*)""", m =>
        {
            var f = m.Groups[1].Value.Split('|');
            if (f.Length < 8) return m.Value;   // not the shape this knows: left as it is
            f[0] = "2";     // 2 = manual (0 permanent, 1 auto, 5 exit)
            f[1] = id;
            f[5] = t;
            f[6] = text;
            return $"UIDescription=\"{string.Join("|", f)}\"";
        });
        return Encoding.UTF8.GetBytes(desc);
    }

    private static string ReplaceAttr(string desc, string name, string value) =>
        Regex.Replace(desc, $@"\b{name}=""[^""]*""", $"{name}=\"{value}\"", RegexOptions.CultureInvariant);

    /// <summary>
    /// Home world file + the shared-world Henry -> the new save. Throws <see cref="InvalidDataException"/> (with a sentence for the player's log)
    /// when the home save does not verify, is not a Henry world, differs in game build, or the check against the inputs fails.
    /// </summary>
    public static Built Build(byte[] homeFile, WhsSave.HenryParts parts, Dictionary<string, string> questClasses, int saveId, DateTimeOffset when, TimeZoneInfo? local = null)
    {
        var v = WhsSave.Verify(homeFile);
        if (!v.Ok) throw new InvalidDataException("the home save does not verify: " + v.Reason);
        var home = WhsSave.Inflate(homeFile);
        var homePlayer = WhsSave.PlayerOf(home.Raw);
        if (homePlayer.Soul != parts.Soul || !homePlayer.IsKnown)
            throw new InvalidDataException(parts.Soul == WhsSave.HenrySoul
                ? "the home save is not a Henry world (a prologue or Godwin save): Henry cannot be put into it"
                : $"the home save's player is {homePlayer.Player}, not the character being sent home");

        var res = WhsSave.SpliceParts(homeFile, parts, questClasses, WhsSave.QuestItemMode.Host);
        var fails = WhsSave.CheckParts(homeFile, parts, res.File, questClasses, WhsSave.QuestItemMode.Host);
        if (fails.Count > 0) return new Built([], res.Report, saveId, ManualName(saveId), fails);

        // the header: same world, a new manual save. Re-framed and re-signed by Deflate (the stream is the spliced one, untouched).
        var spliced = WhsSave.Inflate(res.File);
        byte[] file = WhsSave.Deflate(RewriteDescription(home.DescBytes, saveId, when, local), spliced.Raw, spliced.FooterTail);
        var ver = WhsSave.Verify(file);
        if (!ver.Ok) throw new InvalidDataException("the new save does not verify: " + ver.Reason);
        return new Built(file, res.Report, saveId, ManualName(saveId), []);
    }
}
