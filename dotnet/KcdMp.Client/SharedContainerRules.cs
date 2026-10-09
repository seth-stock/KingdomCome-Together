// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Text.RegularExpressions;

namespace KcdMp.Client;
public static class SharedContainerRules
{
    public static bool Id(string id) => Wo134Rules.ContainerName.IsMatch(id);
    public static bool Category(string category) => category is "chest" or "horse" or "shop";
    public static bool Money(string value, out int amount) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out amount) && amount is >= 0 and <= 1000000;
    public sealed record Request(string Id, Guid Class, int Amount, float Health, string Category, int Charge)
    {
        public string Suffix => FormattableString.Invariant($"{Id} {Class:D} {Amount} {Category} {Charge}");
    }
    public static Request? ParseRequest(string text)
    {
        var f = text.Split(' ');
        if (f.Length != 6 || !Id(f[0]) || !Wo134Rules.TryClass(f[1], out var cls) || !Wo134Rules.TryAmount(f[2], out int n)
            || !Wo134Rules.TryHealth(f[3], out float hp) || !Category(f[4]) || !Money(f[5], out int charge) || (f[4] != "shop" && charge != 0)) return null;
        return new(f[0], cls, n, hp, f[4], charge);
    }
    public static bool Result(string text)
    {
        var f = text.Split(' ');
        return f.Length == 6 && f[0] is "ok" or "gone" or "uncertain" && Id(f[1]) && Wo134Rules.TryClass(f[2], out _)
            && Wo134Rules.TryAmount(f[3], out _) && Category(f[4]) && Money(f[5], out _);
    }
    public static bool Reward(string text)
    {
        var f = text.Split(' ');
        if (f.Length != 3 || !Regex.IsMatch(f[0], @"^[a-zA-Z0-9_/.:\-]{1,300}$") || !Regex.IsMatch(f[1], "^[a-f0-9]{32}$")) return false;
        var items = Wo134Rules.ParseItems(f[2]);
        return items is { Count: >= 1 and <= 12 } && items.Select(i => i.Cls).Distinct().Count() == items.Count;
    }
    public static bool State(string text)
    {
        var f = text.Split(' ');
        return f.Length == 5 && Id(f[0]) && Category(f[1]) && int.TryParse(f[2], out int part) && int.TryParse(f[3], out int count)
            && part >= 1 && count >= part && count <= 64 && Wo134Rules.ParseItems(f[4]) is { Count: <= 10 };
    }
}
