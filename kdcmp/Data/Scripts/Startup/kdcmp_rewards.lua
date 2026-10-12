-- SPDX-License-Identifier: GPL-3.0-only
-- Candidate reward parity: bounded inventory windows, excluding observed loot. Quest XP (Session 10) is measured natively
-- (plugin xpread: C_StatXPEffect/C_SkillXPEffect Apply), compared in the agent and given here as the difference only.
KCD2MP_Rewards={windows={},applied={},given={},gains={},last=nil,host=false,scope='',seconds=3,roots={},paid={},pending={}}
local R=KCD2MP_Rewards
local function read()
    local a=KCD2MP and KCD2MP.w134 and KCD2MP.w134.api
    if not a or not player then return nil end
    local items,ok=a.items(player); if not ok then return nil end
    local counts,hp={},{}
    for _,it in ipairs(items) do counts[it.cls]=(counts[it.cls] or 0)+it.amt; hp[it.cls]=it.hp end
    return counts,hp
end
local function gains(base,at)
    local current,hp=read(); if not current then return nil end
    local result={}
    for cls,n in pairs(current) do if n>(base[cls] or 0) then result[cls]=n-(base[cls] or 0) end end
    for _,g in ipairs(KCD2MP_Containers and KCD2MP_Containers.gains or {}) do
        if g.at>=at and result[g.class] then result[g.class]=math.max(0,result[g.class]-g.n) end
    end
    return result,hp
end
function KCD2MP_RewardArm(tok,code,key,host)
    local counts=read()
    if not counts then KCD2MP_EmitEvent('wo124_reply',tostring(tok)..' ok=false'); return end
    if host then
        if not R.paid[key] and not R.roots[code] then
            R.roots[code]=key
            R.windows[key]={code=code,key=key,base=R.last or counts,at=os.clock()-0.5}
        end
    elseif not R.applied[key] then R.applied[key]={code=code,base=counts,at=os.clock(),ready=false} end
    KCD2MP_EmitEvent('wo124_reply',tostring(tok)..' ok=true')
end
function KCD2MP_RewardMode(host,scope)
    if R.scope~=scope then R.windows={}; R.applied={}; R.given={}; R.last=nil; R.roots={}; R.paid={}; R.pending={} end
    R.host=host==true; R.scope=scope
end
function KCD2MP_RewardGive(code,key,items)
    if R.host or R.given[key] or not KCD2MP_W134JoinerActive() then return end
    local receipt=R.applied[key]
    if not receipt or receipt.code~=code or not receipt.ready then
        local total=0; for _ in pairs(R.pending) do total=total+1 end
        if total<100 then R.pending[key]={code=code,items=items,at=os.clock()} end
        return -- hold reordering without granting before verified quest application
    end
    R.pending[key]=nil
    local native=gains(receipt.base,receipt.at)
    if not native then return end
    R.given[key]=true -- unknown partial native outcomes cannot be retried automatically
    local a=KCD2MP.w134.api
    for _,it in ipairs(items) do
        local cls,n,hp=it[1],it[2],it[3]
        local give=math.max(0,n-(native[cls] or 0))
        if give>0 then
            local before=read()
            local ok=pcall(function() player.inventory:CreateItem(cls,hp,give) end)
            local after=read()
            if not ok or not after or (after[cls] or 0)-(before[cls] or 0)~=give then
                KCD2MP_EmitEvent('w134_reward_unverified',key); return
            end
        end
    end
    if KCD2MP_Containers then KCD2MP_Containers.cachePack() end
    KCD2MP_EmitEvent('w134_reward_applied',key)
end
function KCD2MP_RewardReady(key)
    if R.applied[key] then R.applied[key].ready=true end
end
-- Session 10: quest XP receipt. The agent decides the amounts (host's native quest-reward XP minus what this machine's own
-- engine paid for the same change); this only gives them, once per key, through the engine's own AddStatXP/AddSkillXP.
-- Ids are the engine's (Libs/Tables/rpg/stat.xml, skill.xml); amounts are 1/128 XP, the engine's fixed-point unit.
R.xpGiven={}
local XP_STAT={[0]='strength',[1]='agility',[2]='vitality',[3]='speech',[9]='prestige'}
local XP_SKILL={[0]='stealth',[1]='horse_riding',[2]='fencing',[3]='bard',[4]='thievery',[6]='alchemy',[7]='cooking',[8]='craftsmanship',
    [10]='fishing',[11]='mining',[12]='first_aid',[13]='drinking',[14]='survival',[15]='defense',[16]='weapon_sword',[17]='heavy_weapons',
    [19]='marksmanship',[20]='weapon_shield',[22]='weapon_dagger',[23]='weapon_large',[24]='weapon_unarmed',[26]='scholarship',
    [27]='tailoring',[28]='armourer',[29]='weaponsmithing',[30]='shoemaking',[31]='gunsmithing',[32]='bowyery',[33]='gambling',[34]='houndmaster'}
function KCD2MP_QuestXpGive(key,rows)
    if type(key)~='string' or type(rows)~='table' or R.xpGiven[key] then return false end
    if not player or not player.soul or not KCD2MP_W134JoinerActive() then return false end
    R.xpGiven[key]=true -- a partly given set is not repeated: the engine cannot tell us which half landed
    local given,failed=0,0
    for _,r in ipairs(rows) do
        local skill,id,amount=r[1],r[2],r[3]
        local name=(skill and XP_SKILL or XP_STAT)[id]
        if name and type(amount)=='number' and amount>0 and amount<=1000*128 then
            local ok=pcall(function()
                if skill then player.soul:AddSkillXP(name,amount/128) else player.soul:AddStatXP(name,amount/128) end
            end)
            if ok then given=given+1 else failed=failed+1 end
        else failed=failed+1 end
    end
    KCD2MP_EmitEvent('w137_xp_given',key..' '..given..' '..failed)
    return failed==0
end
function KCD2MP_QuestXpMode(scope)
    if R.xpScope~=scope then R.xpGiven={}; R.xpScope=scope end
end
function R.step()
    if not KCD2MP or not KCD2MP.w134 or not player then return end
    if R.host and KCD2MP_W134HostActive() then
        for key,w in pairs(R.windows) do
            if os.clock()-w.at>=R.seconds then
                R.windows[key]=nil; R.roots[w.code]=nil; R.paid[key]=true
                local result,hp=gains(w.base,w.at); local classes={}
                for cls,n in pairs(result or {}) do if n>0 then classes[#classes+1]=cls end end
                table.sort(classes)
                if #classes>0 and #classes<=12 then
                    local rows={}; for _,cls in ipairs(classes) do rows[#rows+1]=string.format('%s:%d:%.4f',cls,result[cls],hp[cls] or 1) end
                    KCD2MP_EmitEvent('w134_reward',w.code..' '..key..' '..table.concat(rows,','))
                end
            end
        end
    end
    for key,a in pairs(R.applied) do if os.clock()-a.at>60 then R.applied[key]=nil end end
    if not R.host then
        for key,p in pairs(R.pending) do
            if os.clock()-p.at>60 then R.pending[key]=nil
            elseif R.applied[key] and R.applied[key].ready then KCD2MP_RewardGive(p.code,key,p.items) end
        end
    end
    R.last=read()
end
