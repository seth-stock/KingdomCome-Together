using KcdMp.Wire;
using MoonSharp.Interpreter;

public sealed class LootOperationTests
{
    private static Script Lua()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "kdcmp"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var lua = new Script(CoreModules.Preset_Default);
        lua.DoString(File.ReadAllText(Path.Combine(dir!.FullName, "kdcmp", "Data", "Scripts", "Startup", "kdcmp_loot_operations.lua")));
        return lua;
    }
    private const string Scope = "0123456789abcdef0123456789abcdef";
    [Fact] public void RetryReplaysResultWithoutMutatingInventoryTwice()
    {
        var l = Lua(); l.DoString($"scope='{Scope}'; inventory=5; apply=function() inventory=inventory-1; return 'ok' end");
        l.DoString("a,sa=KCD2MP_LootOperations.execute(scope,1,7,'take:sword',apply); b,sb=KCD2MP_LootOperations.execute(scope,1,7,'take:sword',apply)");
        Assert.Equal(4, l.Globals.Get("inventory").Number); Assert.Equal("ok", l.Globals.Get("b").String); Assert.Equal("replay", l.Globals.Get("sb").String);
    }
    [Fact] public void ConflictingRetryAndUncertainMutationAreNeverAppliedAgain()
    {
        var l = Lua(); l.DoString($"scope='{Scope}'; calls=0; bad=function() calls=calls+1; error('interrupted') end");
        l.DoString("a,sa=KCD2MP_LootOperations.execute(scope,1,7,'take:sword',bad); b,sb=KCD2MP_LootOperations.execute(scope,1,7,'take:sword',bad); c,sc=KCD2MP_LootOperations.execute(scope,1,7,'put:sword',bad)");
        Assert.Equal(1, l.Globals.Get("calls").Number); Assert.Equal("uncertain", l.Globals.Get("sb").String); Assert.Equal("conflict", l.Globals.Get("sc").String);
    }
    [Fact] public void NewConnectionScopeDoesNotReuseAnEarlierPlayersToken()
    {
        var l = Lua(); l.DoString($"calls=0; apply=function() calls=calls+1; return 'ok' end; KCD2MP_LootOperations.execute('{Scope}',1,1,'take',apply); KCD2MP_LootOperations.execute('ffffffffffffffffffffffffffffffff',1,1,'take',apply)");
        Assert.Equal(2, l.Globals.Get("calls").Number);
    }
    [Fact] public void CapacityStopsMutationsWithoutEvictingCommittedResults()
    {
        var l = Lua(); l.DoString($"KCD2MP_LootOperations.limit=1; calls=0; apply=function() calls=calls+1; return 'ok' end; KCD2MP_LootOperations.execute('{Scope}',1,1,'take',apply); r,s=KCD2MP_LootOperations.execute('{Scope}',1,2,'take',apply); KCD2MP_LootOperations.execute('{Scope}',1,1,'take',apply)");
        Assert.Equal(1, l.Globals.Get("calls").Number); Assert.Equal("capacity", l.Globals.Get("s").String);
    }
    [Fact] public void ScopedWireRoundTripsAndRejectsLegacyOrInjectedScopes()
    {
        string text = LootMsg.ScopedText(Scope, "body sword 1 0.5");
        Assert.True(LootMsg.TryUnscope(text, out string scope, out string payload));
        Assert.Equal(Scope, scope); Assert.Equal("body sword 1 0.5", payload);
        Assert.False(LootMsg.TryUnscope("body sword 1 0.5", out _, out _));
        Assert.Throws<ArgumentException>(() => LootMsg.ScopedText("../../scope", "take"));
    }
}
