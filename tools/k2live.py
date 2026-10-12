"""KCD2 dev-build live helper (HTTP console on :1403). Developer tool; run only against a throwaway save slot.
    python tools/k2live.py console "wh_sys_LoadGame 4 permanent020"
    python tools/k2live.py lua "print('x')" [tag]      sends '#'+code; prints new log lines containing [tag]
"""
import re
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

LOG = Path(r'E:\SteamLibrary\steamapps\common\KCD2Mod\kcd.log')
API = 'http://localhost:1403/api/System/Console/ExecuteString?command='


def call(command, timeout=15, tries=6):
    for n in range(tries):
        try:
            with urllib.request.urlopen(API + urllib.parse.quote(command, safe=''), timeout=timeout) as r:
                return r.read().decode('utf-8', 'replace')
        except (ConnectionError, OSError):          # the game is busy streaming: try again
            if n == tries - 1:
                raise
            time.sleep(3)


def lua(code, tag=None, wait=1.5):
    size = LOG.stat().st_size
    if len(urllib.parse.quote('#' + code, safe='')) > 1700:
        raise ValueError('chunk too long')
    call('#' + code)
    time.sleep(wait)
    with LOG.open('rb') as f:
        f.seek(size)
        text = f.read().decode('utf-8', 'replace')
    lines = [l for l in text.splitlines() if (tag is None or tag in l)]
    return lines


if __name__ == '__main__':
    if sys.argv[1] == 'console':
        print(call(sys.argv[2])[:300])
    else:
        for l in lua(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else None):
            print(l[:300])
