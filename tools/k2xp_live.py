"""KCD2 dev build, THROWAWAY SAVE ONLY: live proof of the native quest-XP observer (native xpread.h) and the joiner's receipt.

    python tools/k2xp_live.py <quest State path> <port> [--give]

Holds ONE pipe connection open (the plugin drops detection when the pipe closes), turns the quest detector on as the host,
pulses one Set<Value> port on a quest State (a real consequence in the loaded throwaway world), then prints the change the
detector recorded and every XP row the engine's Apply wrote, classified (cause) and attributed (root = the change seq,
tag = the Apply's tag). With --give it then plays the joiner's receipt for what the host paid: KCD2MP_QuestXpGive through the
HTTP console, and shows the rows that produced and the XP read back from the engine.
"""
import struct
import sys
import time

import k2live
import k2pipe

QUEST_CHANGE = 0x9E


def read_frames(pipe, seconds):
    """Everything the plugin sends for `seconds`: (type, body) pairs. Reads are blocking-free via PeekNamedPipe."""
    import ctypes
    from ctypes import wintypes
    import msvcrt
    handle = msvcrt.get_osfhandle(pipe.fileno())
    avail = wintypes.DWORD(0)
    out = []
    end = time.time() + seconds
    while time.time() < end:
        ctypes.windll.kernel32.PeekNamedPipe(handle, None, 0, None, ctypes.byref(avail), None)
        if avail.value >= 3:
            head = pipe.read(3)
            n = struct.unpack('<H', head[1:3])[0]
            out.append((head[0], pipe.read(n) if n else b''))
        else:
            time.sleep(0.05)
    return out


def change(body):
    seq, flags, old, new = struct.unpack('<IBii', body[:13])
    pl = body[13]
    port = body[14:14 + pl].decode()
    at = 14 + pl
    tl = body[at]
    typ = body[at + 1:at + 1 + tl].decode()
    at += 1 + tl
    n = struct.unpack('<H', body[at:at + 2])[0]
    path = body[at + 2:at + 2 + n].decode()
    return dict(seq=seq, flags=flags, old=old, new=new, port=port, type=typ, path=path)


def main():
    path, port = sys.argv[1], sys.argv[2]
    give = '--give' in sys.argv
    pipe = k2pipe.open_pipe()
    print('config:', k2pipe.wo137(pipe, 1, bytes([1, 1, 0])))
    res = k2pipe.xp_rows(pipe, 0xFFFFFFFF)
    cursor = res[0]
    print('observer armed, cursor', cursor)
    tag = 777
    pb, qb = path.encode(), port.encode()
    r = k2pipe.wo137(pipe, 2, struct.pack('<I', tag) + struct.pack('<H', len(pb)) + pb + bytes([len(qb)]) + qb)
    print('apply:', r[:2] if r else r, '(result byte', r[2][0] if r else None, ': 0 changed, 1 unchanged, 6 asleep)')
    frames = list(k2pipe.SIDE) + read_frames(pipe, 1.5)
    changes = [change(b) for t, b in frames if t == QUEST_CHANGE]
    for c in changes:
        print('detector recorded:', c)
    now, lost, rows = k2pipe.xp_rows(pipe, cursor)
    print('xp rows since the pulse:', len(rows), 'lost', lost)
    for row in rows:
        print('  ', row)
    quest = [x for x in rows if x['cause'] in (1, 2)]
    print('quest-reward rows:', len(quest), '| attributed to a change:', sum(1 for x in quest if x['root']), '| carrying the apply tag:', sum(1 for x in quest if x['tag'] == tag))
    if not give:
        return
    names = {(False, 0): 'strength', (False, 1): 'agility', (False, 2): 'vitality', (False, 3): 'speech', (False, 9): 'prestige'}
    totals = {}
    for x in quest:
        if x['index'] == 8 and x['kind'] == 'stat':
            continue                                    # storyProgress never crosses
        k = (x['kind'] == 'skill', x['index'])
        totals[k] = totals.get(k, 0) + x['requested']
    table = '{' + ','.join('{%s,%d,%d}' % ('true' if s else 'false', i, a) for (s, i), a in sorted(totals.items())) + '}'
    print('the receipt for', totals)
    cursor = rows[-1]['seq'] + 1 if rows else cursor
    k2live.lua('KCD2MP_W134JoinerActive = function() return true end', 'x')
    out = k2live.lua('KCD2MP_QuestXpGive("q%d", %s)' % (tag, table), 'x', wait=2.0)
    now, lost, rows2 = k2pipe.xp_rows(pipe, cursor)
    print('receipt produced', len(rows2), 'rows:')
    for row in rows2:
        print('  ', row)
    cursor = rows2[-1]['seq'] + 1 if rows2 else cursor
    ok = all(any(x['kind'] == ('skill' if s else 'stat') and x['index'] == i and x['requested'] == a and x['cause'] == 0 for x in rows2) for (s, i), a in totals.items())
    print('every slot given exactly what the host paid (requested == 128 x XP):', ok)
    k2live.lua('KCD2MP_QuestXpGive("q%d", %s)' % (tag, table), 'x', wait=1.5)
    now, lost, rows3 = k2pipe.xp_rows(pipe, cursor)
    print('the same key again produced', len(rows3), 'rows (must be 0)')


if __name__ == '__main__':
    main()
