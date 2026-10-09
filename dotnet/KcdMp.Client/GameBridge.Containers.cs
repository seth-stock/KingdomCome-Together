// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;
public partial class GameBridge
{
    private bool _sharedContainers = true;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> _w137RewardSeen = new(StringComparer.Ordinal);
    private string _hostContainerScope = "";
    private bool ContainersEffective => Wo134HostRole ? _sharedContainers : _sharedContainers && _hostContainerScope.Length == 32;
    private async Task ContainerModeTickAsync()
    {
        if (!Wo134HostRole) return;
        foreach (byte peer in Wo134Peers())
            await Wo134SendAsync(Protocol.LootHostUp,peer,Protocol.LootHostContainerMode,0,_sharedContainers ? "on" : "off",_w134RequestScope);
    }
    private void ContainerEvent(string name, string? arg)
    {
        var f=(arg ?? "").Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if (name=="w134_container_mode")
        {
            if (Wo134HostRole && arg is "on" or "off") _sharedContainers=arg=="on";
            return;
        }
        if (name=="w134_copen" && Wo134JoinerRole && f.Length==2 && SharedContainerRules.Id(f[0]) && SharedContainerRules.Category(f[1]))
            _=Wo134SendAsync(Protocol.LootAskUp,Protocol.JoinTargetHost,Protocol.LootAskContainerOpen,0,arg!);
        else if (name is "w134_ctake" or "w134_cput" && Wo134JoinerRole && f.Length==7 && uint.TryParse(f[0],out uint token)
            && SharedContainerRules.ParseRequest(string.Join(' ',f.Skip(1))) is { } request)
            _=Wo134SendAsync(Protocol.LootAskUp,Protocol.JoinTargetHost,name=="w134_ctake" ? Protocol.LootAskContainerTake : Protocol.LootAskContainerPut,
                token,FormattableString.Invariant($"{request.Id} {request.Class:D} {request.Amount} {Wo134Rules.F(request.Health)} {request.Category} {request.Charge}"));
        else if (name=="w134_cres" && Wo134HostRole && f.Length==9 && byte.TryParse(f[0],out byte peer)
            && uint.TryParse(f[1],out uint tok) && Guid.TryParseExact(f[8],"N",out _) && SharedContainerRules.Result(string.Join(' ',f.Skip(2).Take(6))))
            _=Wo134DurableResultAsync(peer,tok,f[8],Protocol.LootHostContainerResult,string.Join(' ',f.Skip(2).Take(6)));
        else if (name=="w134_cstate" && Wo134HostRole && f.Length==7 && byte.TryParse(f[0],out byte target)
            && uint.TryParse(f[1],out uint generation) && SharedContainerRules.State(string.Join(' ',f.Skip(2))))
            _=ContainerStateSendAsync(target,generation,string.Join(' ',f.Skip(2)));
        else if (name=="w134_reward" && W137Host && SharedContainerRules.Reward(arg ?? ""))
            _=ContainerRewardSendAsync(arg!);
        else if (name is "w134_reward_applied" or "w134_reward_unverified")
            Console.WriteLine($"MP-REWARD Candidate {name}: {arg}");
    }
    private async Task ContainerStateSendAsync(byte target,uint generation,string text)
    {
        foreach (byte peer in target==0 ? Wo134Peers() : new List<byte>{target})
            await Wo134SendAsync(Protocol.LootHostUp,peer,Protocol.LootHostContainerState,generation,text,_w134RequestScope);
    }
    private async Task ContainerRewardSendAsync(string text)
    {
        foreach (byte peer in Wo134Peers()) await Wo134SendAsync(Protocol.LootHostUp,peer,Protocol.LootHostQuestReward,0,text,_w134RequestScope);
    }
    private async Task<bool> ContainerFrameAsync(int type,byte source,LootMsg msg,string scope)
    {
        if (type==Protocol.LootAskDown && msg.Kind is Protocol.LootAskContainerTake or Protocol.LootAskContainerPut or Protocol.LootAskContainerOpen)
        {
            if (!Wo134HostRole || !ContainersEffective || _where!=GameWhere.World || Wo136Holding || CheckpointHolding) return true;
            if (msg.Kind==Protocol.LootAskContainerOpen)
            {
                var fields=msg.Text.Split(' ');
                if (fields.Length==2 && SharedContainerRules.Id(fields[0]) && SharedContainerRules.Category(fields[1]))
                    await ExecLuaAsync($"if KCD2MP_ContainerOpen then KCD2MP_ContainerOpen({source},\"{EscapeLua(fields[0])}\",\"{fields[1]}\") end");
            }
            else if (SharedContainerRules.ParseRequest(msg.Text) is { } r)
            {
                if (!await Wo134BeginDurableAsync(source,scope,msg,Protocol.LootHostContainerResult,r.Suffix)) return true;
                await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_ContainerAsk then KCD2MP_ContainerAsk({source},{msg.Tok},\"{EscapeLua(r.Id)}\",\"{r.Class:D}\",{r.Amount},{Wo134Rules.F(r.Health)},\"{r.Category}\",{r.Charge},{B(msg.Kind==Protocol.LootAskContainerPut)},\"{scope}\") end"));
            }
            return true;
        }
        if (type!=Protocol.LootHostDown || msg.Kind is not (Protocol.LootHostContainerResult or Protocol.LootHostContainerState or Protocol.LootHostContainerMode or Protocol.LootHostQuestReward)) return false;
        if (!Wo134JoinerRole || !LootMsg.TryUnscope(msg.Text,out string replyScope,out string text)) return true;
        if (msg.Kind==Protocol.LootHostContainerMode && text is "on" or "off")
        {
            _hostContainerScope=replyScope; _sharedContainers=text=="on";
            await ExecLuaAsync($"if KCD2MP_ContainerMode then KCD2MP_ContainerMode({B(_sharedContainers)},\"{replyScope}\") end; if KCD2MP_RewardMode then KCD2MP_RewardMode(false,\"{replyScope}\") end");
        }
        else if (msg.Kind==Protocol.LootHostContainerResult && replyScope==_w134RequestScope && SharedContainerRules.Result(text))
            await ExecLuaAsync($"if KCD2MP_ContainerResult then KCD2MP_ContainerResult(\"{msg.Tok}\",\"{text.Split(' ')[0]}\") end");
        else if (replyScope==_hostContainerScope && msg.Kind==Protocol.LootHostContainerState && SharedContainerRules.State(text))
        {
            var f=text.Split(' '); var items=Wo134Rules.ParseItems(f[4])!;
            await ExecLuaAsync($"if KCD2MP_ContainerState then KCD2MP_ContainerState(\"{EscapeLua(f[0])}\",\"{f[1]}\",{msg.Tok},{f[2]},{f[3]},{Wo134Rules.LuaItems(items)}) end");
        }
        else if (replyScope==_hostContainerScope && msg.Kind==Protocol.LootHostQuestReward && SharedContainerRules.Reward(text))
        {
            var f=text.Split(' ');
            await ExecLuaAsync($"if KCD2MP_RewardGive then KCD2MP_RewardGive(\"{f[0]}\",\"{f[1]}\",{Wo134Rules.LuaItems(Wo134Rules.ParseItems(f[2])!)}) end");
        }
        return true;
    }
    public static string RewardKey(QuestChange c) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"{c.Seq}|{c.Path}|{c.Old}|{c.New}"))).AsSpan(0,16)).ToLowerInvariant();
    private Task<string> RewardArmAsync(QuestChange c,bool host)
    {
        if (c.Seq==0 || c.Old==c.New || c.QuestLen<=0) return Task.FromResult("ok=false");
        return AskModAsync($"(function(tok) if KCD2MP_RewardArm then KCD2MP_RewardArm(tok,\"{EscapeLua(c.Quest)}\",\"{RewardKey(c)}\",{B(host)}) else KCD2MP_EmitEvent('wo124_reply',tok..' ok=false') end end)",1000);
    }
}
