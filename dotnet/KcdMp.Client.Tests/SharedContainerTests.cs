using KcdMp.Wire;
using Coop.Contract;

public sealed class SharedContainerTests
{
    private const string Class="2264f217-590e-4c0f-a4c6-f50c6532b9f6";
    private const string Scope="0123456789abcdef0123456789abcdef";
    [Theory]
    [InlineData("chest",0)] [InlineData("horse",0)] [InlineData("shop",25)]
    public void RequestRoundTripsThroughScopedWireWithCanonicalDecisionCorrelation(string category,int charge)
    {
        string text=$"stash[Chest/test] {Class} 2 0.75 {category} {charge}";
        var request=SharedContainerRules.ParseRequest(text);
        Assert.NotNull(request);
        string scoped=LootMsg.ScopedText(Scope,text);
        Assert.True(LootMsg.TryUnscope(scoped,out var scope,out var payload));
        Assert.Equal(Scope,scope); Assert.Equal(request,SharedContainerRules.ParseRequest(payload));
        Assert.True(SharedContainerRules.Result("ok "+request!.Suffix));
        Assert.True(SharedContainerRules.Result("uncertain "+request.Suffix));
    }
    [Theory]
    [InlineData("name\"lua", "1", "1", "chest", "0")]
    [InlineData("name", "0", "1", "chest", "0")]
    [InlineData("name", "1", "NaN", "chest", "0")]
    [InlineData("name", "1", "1", "player", "0")]
    [InlineData("name", "1", "1", "horse", "25")]
    [InlineData("name", "1", "1", "shop", "-1")]
    [InlineData("name", "1", "1", "shop", "1000001")]
    public void InvalidNativeArgumentsAndInventedRefundsAreRejected(string id,string amount,string hp,string category,string charge)
        => Assert.Null(SharedContainerRules.ParseRequest($"{id} {Class} {amount} {hp} {category} {charge}"));
    [Fact] public void ContainerTakeAndPutUseTheDurableReplayAndCrashQuarantinePolicy()
    {
        string root=Path.Combine(Path.GetTempPath(),"kcd-container-test-"+Guid.NewGuid().ToString("N"));
        string file=Path.Combine(root,"journal.jsonl");
        try
        {
            using (var journal=new DurableLootLedger(file))
            {
                Assert.Equal(BeginKind.New,journal.Begin(1,Scope,7,Protocol.LootAskContainerTake,"take payload").Kind);
                Assert.True(journal.Complete(1,Scope,7,new(Protocol.LootHostContainerResult,$"ok chest {Class} 1 chest 0")));
                Assert.Equal(BeginKind.Replay,journal.Begin(1,Scope,7,Protocol.LootAskContainerTake,"take payload").Kind);
                Assert.Equal(BeginKind.Conflict,journal.Begin(1,Scope,7,Protocol.LootAskContainerPut,"take payload").Kind);
                journal.Begin(1,Scope,8,Protocol.LootAskContainerPut,"put payload");
            }
            using var recovered=new DurableLootLedger(file);
            Assert.Equal(BeginKind.Quarantined,recovered.Begin(1,Scope,8,Protocol.LootAskContainerPut,"put payload").Kind);
            Assert.NotNull(DurableLootLedger.Replay(recovered.Begin(1,Scope,7,Protocol.LootAskContainerTake,"take payload")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact] public void RewardAndStockPayloadsRejectInjectionDuplicateClassesAndUnboundedParts()
    {
        string reward=$"q_test {Scope} {Class}:2:1";
        Assert.True(SharedContainerRules.Reward(reward));
        Assert.False(SharedContainerRules.Reward(reward+","+Class+":2:1"));
        Assert.False(SharedContainerRules.Reward(reward.Replace("q_test","q'bad")));
        Assert.True(SharedContainerRules.State($"chest horse 1 2 {Class}:2:1"));
        Assert.False(SharedContainerRules.State($"chest horse 0 2 {Class}:2:1"));
        Assert.False(SharedContainerRules.State($"chest horse 1 65 {Class}:2:1"));
    }
    [Fact] public void RewardKeysCorrelateTheActualMirroredStepAndDistinctNativeSequence()
    {
        var a=new QuestChange(7,0,0,1,"complete","state","q_test/node",6);
        Assert.Equal(GameBridge.RewardKey(a),GameBridge.RewardKey(a));
        Assert.NotEqual(GameBridge.RewardKey(a),GameBridge.RewardKey(a with { Seq=8 }));
        Assert.NotEqual(GameBridge.RewardKey(a),GameBridge.RewardKey(a with { New=2 }));
    }
}
