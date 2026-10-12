"""Minimal client for the KCDMP plugin's named pipe (\\\\.\\pipe\\kcdmp): frames [type:1][len:2 LE][body]. Developer tool for the live dev build, throwaway saves only.
    python tools/k2pipe.py status
    python tools/k2pipe.py readquest <Barbora.quest.path>
    python tools/k2pipe.py apply <path> <port>        one Set<Value> pulse on a quest State (real consequences in the loaded world)
    python tools/k2pipe.py readstate <path> ...       found/runtime/ok/value of quest State nodes
    python tools/k2pipe.py config <detect 0|1> <role 0|1|2> [flags]   the quest detector (role 1 host, 2 joiner)
    python tools/k2pipe.py xp [cursor]                the native XP observer's rows since a cursor (op 9)
"""
import struct
import sys
import time

WO137, WO137_REPLY = 0x23, 0x9D
SIDE = []          # unsolicited frames (e.g. 0x9E quest changes) that arrived while a reply was awaited


def open_pipe(timeout=10):
    end = time.time() + timeout
    while True:
        try:
            return open(r'\\.\pipe\kcdmp', 'r+b', buffering=0)
        except OSError:
            if time.time() > end:
                raise
            time.sleep(0.3)


def frame(pipe, ftype, body, want=None, wait=6.0):
    pipe.write(bytes([ftype]) + struct.pack('<H', len(body)) + body)
    end = time.time() + wait
    while time.time() < end:
        head = pipe.read(3)
        if len(head) < 3:
            continue
        t, n = head[0], struct.unpack('<H', head[1:3])[0]
        b = pipe.read(n) if n else b''
        if want is None or t == want:
            return t, b
        SIDE.append((t, b))
    return None, b''


def wo137(pipe, op, args=b''):
    t, b = frame(pipe, WO137, bytes([op]) + args, WO137_REPLY)
    if t is None or len(b) < 4:
        return None
    return b[0] == 1, b[3], b[4:]


def xp_rows(pipe, cursor):
    """Rows of the XP observer since `cursor`: (now, lost, [dict]) or None."""
    r = wo137(pipe, 9, struct.pack('<I', cursor))
    if not r or not r[0]:
        return r
    b = r[2]
    now, lost, count = struct.unpack('<IIB', b[:9])
    at, rows = 9, []
    for _ in range(count):
        seq, kind, soul, idx, req, app, before, age, cause, root, tag = struct.unpack('<IBQIIIIIBII', b[at:at + 42])
        at += 42
        rows.append(dict(seq=seq, kind='skill' if kind else 'stat', soul='%016X' % soul, index=idx, requested=req,
                         applied=app, before=before, age=age, cause=cause, root=root, tag=tag))
    return now, lost, rows


if __name__ == '__main__':
    p = open_pipe()
    cmd = sys.argv[1]
    if cmd == 'status':
        print(wo137(p, 5))
    elif cmd == 'readquest':
        path = sys.argv[2].encode()
        r = wo137(p, 4, struct.pack('<H', len(path)) + path)
        print(r)
    elif cmd == 'readstate':
        paths = [a.encode() for a in sys.argv[2:]]
        body = bytes([len(paths)]) + b''.join(struct.pack('<H', len(x)) + x for x in paths)
        r = wo137(p, 3, body)
        if r:
            n, b = r[2][0], r[2][1:]
            for i in range(n):
                f, rt, ok = b[i * 7], b[i * 7 + 1], b[i * 7 + 2]
                print(paths[i].decode(), 'found' if f else 'MISSING', 'runtime' if rt else 'cold', struct.unpack('<i', b[i * 7 + 3:i * 7 + 7])[0] if ok else None)
        else:
            print(r)
    elif cmd == 'config':
        print(wo137(p, 1, bytes([int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]) if len(sys.argv) > 4 else 0])))
    elif cmd == 'xp':
        print(xp_rows(p, int(sys.argv[2]) if len(sys.argv) > 2 else 0))
    elif cmd == 'apply':
        path, port = sys.argv[2].encode(), sys.argv[3].encode()
        r = wo137(p, 2, struct.pack('<I', 1) + struct.pack('<H', len(path)) + path + bytes([len(port)]) + port)
        print(r)
