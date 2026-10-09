-- SPDX-License-Identifier: GPL-3.0-only
-- Candidate live shared stock. The old personal chest ledger remains available when this mode is off.
-- Native screens move items before this watcher runs; this is not complete pre-transfer escrow.
KCD2MP_Containers = { mode=false, scope='', sessions={}, pending={}, partial={}, received={}, generation=0, gains={} }
local C = KCD2MP_Containers
C.money = '5ef63059-322e-4e1b-abe8-926e100c770e'
local function api() return KCD2MP and KCD2MP.w134 and KCD2MP.w134.api end
local function now() return os.clock() end
local function emit(name, text) KCD2MP_EmitEvent(name, text) end
local function validId(id) return type(id)=='string' and #id>0 and #id<=300 and not string.find(id, '[^%w_%[%]/%.:%-]') end
function C.kind(e)
    if not e or e==player or not e.inventory then return nil end
    local a=api(); local name=a and a.name(e)
    if name and a.isDrop(name) then return nil end
    if e.class=='Horse' then return 'horse' end
    if not (a and a.CHEST_CLASS[e.class]) then return nil end
    if e.class=='Stash' and Shops and Framework and type(Shops.IsLinkedWithShop)=='function' and type(Framework.IsValidWUID)=='function' then
        local ok, linked=pcall(Shops.IsLinkedWithShop, e.id)
        local good, valid=pcall(Framework.IsValidWUID, linked)
        if ok and good and valid==true then return 'shop' end
    end
    return 'chest'
end
function C.id(e)
    local a=api(); local name=a.name(e)
    if name and validId(name) then return name end
    if e.class=='Horse' then return nil end -- no position identity for an independently moving anonymous horse
    local p=a.pos(e)
    if p then return string.format('ct_%s_%d_%d_%d', e.class, math.floor(p.x*10), math.floor(p.y*10), math.floor(p.z*10)) end
end
function C.read(e)
    local items, ok=api().items(e)
    if not ok then return nil end
    local count, hp={},{}
    for _, it in ipairs(items) do count[it.cls]=(count[it.cls] or 0)+it.amt; hp[it.cls]=it.hp end
    return count,hp,items
end
function C.gain(cls,n)
    C.gains[#C.gains+1]={class=cls,n=n,at=now()}
    while #C.gains>500 or (#C.gains>0 and now()-C.gains[1].at>65) do table.remove(C.gains,1) end
end
function C.cachePack()
    local pack,hp,items=C.read(player)
    C.pack=pack; C.packItems=items
end
function C.active()
    return C.mode and not C.suspended and api() and KCD2MP.w134.chests and (KCD2MP_W134JoinerActive() or KCD2MP_W134HostActive())
end
function C.find(id, category)
    local direct=System.GetEntityByName(id)
    if direct and C.kind(direct)==category and C.id(direct)==id then return direct end
    local anchors={api().pos(player)}
    for _, g in pairs(KCD2MP.ghosts or {}) do if g.entity then anchors[#anchors+1]=api().pos(g.entity) end end
    local found
    for _, p in ipairs(anchors) do
        for _, e in pairs(System.GetEntitiesInSphere(p,80) or {}) do
            if C.kind(e)==category and C.id(e)==id then
                if found and found.id~=e.id then return nil end -- ambiguous identity is never guessed
                found=e
            end
        end
    end
    return found
end
function C.send(e, target)
    local id, category=C.id(e), C.kind(e)
    local counts,hp=C.read(e)
    if not id or not counts then return end
    local classes={}; for cls in pairs(counts) do classes[#classes+1]=cls end; table.sort(classes)
    if #classes>640 then return end
    C.generation=C.generation%4294967295+1
    local parts=math.max(1,math.ceil(#classes/10))
    for part=1,parts do
        local rows={}
        for i=(part-1)*10+1,math.min(part*10,#classes) do
            local cls=classes[i]; rows[#rows+1]=string.format('%s:%d:%.4f',cls,counts[cls],hp[cls] or 1)
        end
        emit('w134_cstate', string.format('%d %d %s %s %d %d %s',target or 0,C.generation,id,category,part,parts,#rows>0 and table.concat(rows,',') or '-'))
    end
end
function C.reset()
    C.sessions={}; C.pending={}; C.partial={}; C.received={}; C.pack=nil; C.packItems=nil; C.gains={}; C.suspended=false
end
function KCD2MP_ContainerMode(enabled, scope)
    if C.scope~=scope or C.mode~=(enabled==true) then
        if api() and C.settle(0)>0 then C.suspended=true; return false end -- stop new asks while preserving unresolved rollback evidence
        C.reset()
    end
    C.scope=scope or ''; C.mode=enabled==true
    if C.suspended and not next(C.pending) then C.suspended=false end
end
function C.setShared(value)
    if value=='on' or value=='off' then emit('w134_container_mode',value)
    else KCD2MP_ShowNativeToast('Shared live containers: mp_shared_containers on|off (off keeps personal chest ledgers).') end
end
function C.ask(e,id,category,cls,n,hp,put,charge,original)
    local tok=api().tok()
    C.pending[tok]={id=id,category=category,class=cls,n=n,hp=hp,put=put,charge=charge,at=now(),original=original}
    emit(put and 'w134_cput' or 'w134_ctake', string.format('%s %s %s %d %.4f %s %d',tok,id,cls,n,hp,category,charge))
end
function C.pass()
    if not C.active() or KCD2MP_CheckpointBlocked() then return end
    local pp=api().pos(player); local pack,packHp,packItems=C.read(player)
    if not pp or not pack then return end
    local before=C.pack or pack; local gains,loss={},{}
    for cls,n in pairs(pack) do gains[cls]=math.max(0,n-(before[cls] or 0)) end
    for cls,n in pairs(before) do loss[cls]=math.max(0,n-(pack[cls] or 0)) end
    local spent=loss[C.money] or 0
    local reached={}
    for _, e in pairs(System.GetEntitiesInSphere(pp,40) or {}) do
        local category=C.kind(e); local id=category and C.id(e)
        local p=category and api().pos(e)
        local radius=category=='shop' and 40 or 9
        if id and p and (p.x-pp.x)^2+(p.y-pp.y)^2+(p.z-pp.z)^2<=radius*radius then
            local current,hp=C.read(e)
            if current then
                reached[id]=true
                local previous=C.sessions[id]
                if previous then
                    for cls,n in pairs(previous.count) do
                        local taken=math.min(math.max(0,n-(current[cls] or 0)),gains[cls] or 0)
                        if taken>0 then
                            gains[cls]=gains[cls]-taken
                            if KCD2MP_W134JoinerActive() then C.ask(e,id,category,cls,taken,previous.hp[cls] or 1,false,category=='shop' and spent or 0) end
                            if category=='shop' then spent=0 end -- observed payment budget consumed once, never refunded per class
                            C.gain(cls,taken)
                        end
                    end
                    for cls,n in pairs(current) do
                        local put=math.min(math.max(0,n-(previous.count[cls] or 0)),loss[cls] or 0)
                        if put>0 then
                            loss[cls]=loss[cls]-put
                            if KCD2MP_W134JoinerActive() then
                                -- A whole original item may be recoverable exactly. Never synthesize class-only refunds.
                                local present={}; for _,it in ipairs(packItems or {}) do present[tostring(it.w)]=true end
                                local stock={}; local _,_,items=C.read(e)
                                for _,it in ipairs(items or {}) do stock[tostring(it.w)]=it end
                                local original,total={},0
                                for _,it in ipairs(C.packItems or {}) do
                                    local moved=stock[tostring(it.w)]
                                    if it.cls==cls and not present[tostring(it.w)] and moved and moved.cls==it.cls and moved.amt==it.amt and moved.hp==it.hp then
                                        original[#original+1]={w=moved.w,cls=it.cls,amt=it.amt,hp=it.hp}; total=total+it.amt
                                    end
                                end
                                C.ask(e,id,category,cls,put,hp[cls] or 1,true,0,total==put and original or nil)
                            end
                        end
                    end
                end
                local signature={}; for cls,n in pairs(current) do signature[#signature+1]=cls..':'..n..':'..(hp[cls] or 1) end; table.sort(signature)
                signature=table.concat(signature,',')
                if KCD2MP_W134HostActive() and (not previous or previous.signature~=signature) then C.send(e,0) end
                if not previous and KCD2MP_W134JoinerActive() then emit('w134_copen',id..' '..category) end
                C.sessions[id]={count=current,hp=hp,signature=signature}
            end
        end
    end
    for id in pairs(C.sessions) do if not reached[id] then C.sessions[id]=nil end end
    C.pack=pack; C.packItems=packItems
end
function KCD2MP_ContainerOpen(peer,id,category)
    if not C.active() or not KCD2MP_W134HostActive() then return end
    local e=C.find(id,category); if e then C.send(e,peer) end
end
function KCD2MP_ContainerAsk(peer,tok,id,cls,n,hp,category,charge,put,scope)
    if not C.active() or not KCD2MP_W134HostActive() or KCD2MP_CheckpointBlocked() then return end
    local result=KCD2MP_LootOperations.execute(scope,peer,tok,
        table.concat({put and 'cput' or 'ctake',id,cls,tostring(n),tostring(hp),category,tostring(charge)},'|'),function()
        local e=C.find(id,category); if not e then return 'gone' end
        local count=C.read(e); if not count then return nil end
        local before=count[cls] or 0
        if put then
            e.inventory:CreateItem(cls,hp,n)
            local after=C.read(e)
            if not after or (after[cls] or 0)-before~=n then return nil end
        else
            if before<n then return 'gone' end
            if api().deleteClass(e,cls,n,hp,nil)~=n then return nil end
            local after=C.read(e); if not after or before-(after[cls] or 0)~=n then return nil end
        end
        return 'ok'
    end)
    if result then
        emit('w134_cres', string.format('%s %s %s %s %s %d %s %d %s',peer,tok,result,id,cls,n,category,charge,scope))
        local e=C.find(id,category); if e then C.send(e,0) end
    end
end
local function createChecked(e,cls,n,hp)
    local count=C.read(e); if not count then return false end
    e.inventory:CreateItem(cls,hp,n)
    local after=C.read(e)
    return after and (after[cls] or 0)-(count[cls] or 0)==n
end
function C.revoke(t)
    if t.uncertain then return false end
    if t.put then
        if not t.definiteGone or not t.original or t.restoreAttempted then return false end
        local e=C.find(t.id,t.category)
        if not e then return false end
        local source,_,items=C.read(e); local destination,_,pack=C.read(player)
        if not source or not destination then return false end
        local stock,present={},{}
        for _,it in ipairs(items) do stock[tostring(it.w)]=it end
        for _,it in ipairs(pack) do present[tostring(it.w)]=true end
        for _,old in ipairs(t.original) do
            local it=stock[tostring(old.w)]
            if present[tostring(old.w)] or not it or it.cls~=old.cls or it.amt~=old.amt or it.hp~=old.hp then return false end
        end
        t.restoreAttempted=true -- uncertain native moves are never repeated
        for _,it in ipairs(t.original) do
            e.inventory:RemoveItem(it.w)
            player.inventory:AddItem(it.w)
        end
        local afterSource,_,sourceItems=C.read(e); local afterDest,_,destItems=C.read(player)
        if not afterSource or not afterDest or (source[t.class] or 0)-(afterSource[t.class] or 0)~=t.n
            or (afterDest[t.class] or 0)-(destination[t.class] or 0)~=t.n then return false end
        stock={}; present={}
        for _,it in ipairs(sourceItems) do stock[tostring(it.w)]=true end
        for _,it in ipairs(destItems) do present[tostring(it.w)]=it end
        for _,old in ipairs(t.original) do
            local restored=present[tostring(old.w)]
            if stock[tostring(old.w)] or not restored or restored.cls~=old.cls or restored.amt~=old.amt or restored.hp~=old.hp then return false end
        end
        C.gain(t.class,t.n)
        return true
    end
    if t.refundUncertain then return false end
    local removed=t.n>0 and api().deleteClass(player,t.class,t.n,t.hp,nil) or 0
    if removed<t.n then t.n=t.n-removed; return false end
    if t.charge>0 and not createChecked(player,C.money,t.charge,1) then t.n=0; t.refundUncertain=true; return false end
    return true
end
function C.safeRevoke(t)
    local ok,result=pcall(C.revoke,t)
    if not ok then t.uncertain=true; C.suspended=true; return false end
    if not result and (t.restoreAttempted or t.refundUncertain or t.uncertain) then C.suspended=true end
    return result
end
function KCD2MP_ContainerResult(tok,verdict)
    tok=tostring(tok); local t=C.pending[tok]
    if not t then return end
    if verdict=='uncertain' then C.suspended=true end
    if verdict=='gone' then t.definiteGone=true end
    if verdict=='ok' or C.safeRevoke(t) then C.pending[tok]=nil end
    C.cachePack() -- a rollback/refund is not a quest reward or a new player transfer
    local stock=C.partial[t.id]
    if stock and stock.parts[1] then KCD2MP_ContainerState(t.id,t.category,stock.generation,1,stock.count,stock.parts[1]) end
end
function C.settle(age)
    local unresolved,changed=0,false
    for tok,t in pairs(C.pending) do
        if age<=0 or now()-t.at>age then
            local oldN,oldAttempt,oldRefund,oldUncertain=t.n,t.restoreAttempted,t.refundUncertain,t.uncertain
            local settled=C.safeRevoke(t)
            if settled then C.pending[tok]=nil else unresolved=unresolved+1 end
            if settled or oldN~=t.n or oldAttempt~=t.restoreAttempted or oldRefund~=t.refundUncertain or oldUncertain~=t.uncertain then changed=true end
        else unresolved=unresolved+1 end
    end
    if changed then C.cachePack() end
    return unresolved
end
function KCD2MP_ContainerState(id,category,generation,part,count,items)
    if not C.active() or not KCD2MP_W134JoinerActive() or generation<=(C.received[id] or 0) then return end
    local old=C.partial[id]
    if not old or generation>old.generation then old={generation=generation,count=count,parts={},at=now()}; C.partial[id]=old end
    if generation~=old.generation or old.count~=count then return end
    old.parts[part]=items
    local desired,hp={},{}
    for i=1,count do
        if not old.parts[i] then return end
        for _,item in ipairs(old.parts[i]) do desired[item[1]]=(desired[item[1]] or 0)+item[2]; hp[item[1]]=item[3] end
    end
    local e=C.find(id,category); if not e then return end
    local current=C.read(e); if not current then return end
    for _,t in pairs(C.pending) do
        if t.id==id then return end -- never overwrite an optimistic transfer while its outcome is unresolved
    end
    for cls,n in pairs(current) do if n>(desired[cls] or 0) and api().deleteClass(e,cls,n-(desired[cls] or 0),hp[cls] or 1,nil)~=n-(desired[cls] or 0) then return end end
    for cls,n in pairs(desired) do if n>(current[cls] or 0) and not createChecked(e,cls,n-(current[cls] or 0),hp[cls] or 1) then return end end
    C.received[id]=generation; C.partial[id]=nil
    local updated,h=C.read(e); C.sessions[id]={count=updated,hp=h}; C.cachePack()
end
