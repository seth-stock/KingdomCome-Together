"""KCD2 dev build (Modding Tools), THROWAWAY SAVE ONLY: restart the game this tool started earlier, load the throwaway slot, inject the plugin.

    python tools/k2restart.py [--slot 4 --save permanent020] [--no-inject]

Only the process whose id this tool wrote to _work/kcd2-live-pid.txt is ever stopped, and only after its command line is checked to
be the dev build's KingdomCome.exe. It never touches a save: it asks the game to load an existing slot. Prints READY when the player
is in the world and the plugin answers on its pipe.
"""
import argparse
import subprocess
import sys
import time
from pathlib import Path

import k2live
import k2pipe

ROOT = Path(__file__).resolve().parent.parent
PIDFILE = ROOT / '_work' / 'kcd2-live-pid.txt'
GAME = Path(r'E:\SteamLibrary\steamapps\common\KCD2Mod')
EXE = GAME / 'Bin' / 'Win64ReleaseSteamLTO_DLL' / 'KingdomCome.exe'
INJECTOR = ROOT / 'native' / 'build' / 'KCDMP_LauncherInjector' / 'KCDMP_LauncherInjector.exe'
DLL = ROOT / 'native' / 'build' / 'KCDMP' / 'KCDMP.dll'


def ps(command):
    return subprocess.run(['powershell', '-NoProfile', '-Command', command], capture_output=True, text=True).stdout.strip()


def stop_ours():
    if not PIDFILE.exists():
        return
    pid = PIDFILE.read_text().strip()
    if not pid.isdigit():
        return
    line = ps("(Get-CimInstance Win32_Process -Filter 'ProcessId=%s').CommandLine" % pid)
    if 'KCD2Mod' in line and 'KingdomCome.exe' in line:
        ps('Stop-Process -Id %s -Force' % pid)
        print('stopped our game', pid)
        time.sleep(6)
    else:
        print('pid', pid, 'is not our dev-build game (command line %r): left alone' % line[:80])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--slot', default='4')
    ap.add_argument('--save', default='permanent020')
    ap.add_argument('--no-inject', action='store_true')
    a = ap.parse_args()
    stop_ours()
    flags = 0x00000008 | 0x00000200            # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP: the game outlives this tool
    try:
        proc = subprocess.Popen([str(EXE)], cwd=str(GAME), creationflags=flags | 0x01000000)   # + CREATE_BREAKAWAY_FROM_JOB
    except OSError:
        proc = subprocess.Popen([str(EXE)], cwd=str(GAME), creationflags=flags)
    PIDFILE.write_text(str(proc.pid))
    print('started', proc.pid)
    while True:
        try:
            k2live.call('echo', timeout=3, tries=1)
            break
        except Exception:
            time.sleep(4)
    time.sleep(5)
    k2live.call('wh_sys_LoadGame %s %s' % (a.slot, a.save))
    print('load issued')
    time.sleep(30)
    while True:
        try:
            out = k2live.lua("System.LogAlways('PLREADY '..tostring(player ~= nil and player.soul ~= nil))", 'PLREADY')
        except Exception:
            out = []
        if any('PLREADY true' in l for l in out):
            break
        time.sleep(8)
    time.sleep(30)                              # let the world settle: an injection during streaming crashed the game once (XGenAIModule, not ours)
    if not a.no_inject:
        subprocess.run([str(INJECTOR), '--pid', str(proc.pid), '--dll', str(DLL)], check=True)
        time.sleep(6)
        print(k2pipe.wo137(k2pipe.open_pipe(), 5)[2].decode()[-120:])
    print('READY', proc.pid)


if __name__ == '__main__':
    main()
