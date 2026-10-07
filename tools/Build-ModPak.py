"""Build the same deterministic, stored mod pak on Windows and Linux; no game assets."""
from pathlib import Path
import zipfile
import hashlib
root = Path(__file__).resolve().parents[1] / 'kdcmp' / 'Data'
files = ('Scripts/Startup/kdcmp.lua', 'Scripts/Startup/kdcmp_menu.lua', 'Scripts/Startup/kdcmp_loot_operations.lua',
         'Libs/Tables/item/clothing_preset__kdcmp.xml', 'Libs/Tables/rpg/buff__kcdmp.xml')
contents = {name: (root / name).read_bytes() for name in files}
for name, data in contents.items():
    if data.startswith(b'\xef\xbb\xbf'): raise RuntimeError('UTF-8 BOM is forbidden: ' + name)
    data.decode('utf-8')
target = root / 'kdcmp.pak'
staging = target.with_suffix('.pak.part')
with zipfile.ZipFile(staging, 'w', compression=zipfile.ZIP_STORED) as archive:
    for name in files:
        entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
        entry.create_system = 0
        entry.external_attr = 0x20
        archive.writestr(entry, contents[name])
staging.replace(target)
print('Built kdcmp.pak SHA-256 ' + hashlib.sha256(target.read_bytes()).hexdigest())
