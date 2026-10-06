-- SPDX-License-Identifier: GPL-3.0-only
-- Scoped retries in one running authority incarnation. Crash recovery is separate.
KCD2MP_LootOperations = { rows = {}, count = 0, limit = 65536 }
local O = KCD2MP_LootOperations
function O.execute(scope, peer, tok, fingerprint, operation)
    if type(scope) ~= 'string' or #scope ~= 32 or string.find(scope, '[^0-9a-f]') then return nil, 'scope' end
    local n = tonumber(tok)
    if not n or n <= 0 or n > 4294967295 or n ~= math.floor(n) then return nil, 'token' end
    local key = tostring(peer) .. ':' .. scope .. ':' .. tostring(tok)
    local row = O.rows[key]
    if row then
        if row.fingerprint ~= fingerprint then return nil, 'conflict' end
        if row.state ~= 'done' then return nil, 'uncertain' end
        return row.result, 'replay'
    end
    if O.count >= O.limit then return nil, 'capacity' end -- never evict and reapply a request
    row = { fingerprint = fingerprint, state = 'pending' }
    O.rows[key] = row; O.count = O.count + 1
    local ok, result = pcall(operation)
    if not ok or result == nil then row.state = 'uncertain'; return nil, 'uncertain' end
    row.result = result; row.state = 'done'
    return result, 'applied'
end
