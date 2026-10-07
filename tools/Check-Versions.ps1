# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
<#
.SYNOPSIS
    Reports whether every layer of a build or an install says the same version, and prints the payload hashes a bug report needs.

.DESCRIPTION
    VERSION is the one source (this script never edits it). The .NET programs stamp it into their file and product versions. The Lua pak and the native plugin carry no version:
    they are identified by SHA-256, which is also what the room handshake enforces (the pak) or reports (the plugin).
    Exit 0 when every program agrees with VERSION; 1 otherwise.

.PARAMETER Install
    Also check the installed copy (%LocalAppData%\KCDMP and the Modding Tools mod folder).
#>
param([switch]$Install, [string]$GameMods)

$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -TotalCount 1).Trim()
$bad = 0
function Check($label, $path) {
    if (-not (Test-Path $path)) { Write-Host ("  MISSING {0}  ({1})" -f $label, $path) -ForegroundColor Yellow; return }
    $v = (Get-Item $path).VersionInfo.ProductVersion
    $ok = $v -eq $version
    if (-not $ok) { $script:bad++ }
    Write-Host ("  {0} {1,-28} {2}" -f $(if ($ok) { 'ok     ' } else { 'DIFFERS' }), $label, $v) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
}
function Hash($label, $path) {
    if (Test-Path $path) { Write-Host ("  sha256  {0,-28} {1}" -f $label, (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()) }
    else { Write-Host ("  MISSING {0}" -f $label) -ForegroundColor Yellow }
}

Write-Host "VERSION file: $version"
$targets = @(@{ Name = 'release payload'; Dir = (Join-Path $root 'release\KCDMP') })
if ($Install) { $targets += @{ Name = 'installed'; Dir = (Join-Path $env:LOCALAPPDATA 'KCDMP') } }
foreach ($t in $targets) {
    Write-Host "== $($t.Name): $($t.Dir)"
    foreach ($exe in 'KCDMP_launcher.exe', 'KcdMpClient.exe', 'KcdMpServer.exe') { Check $exe (Join-Path $t.Dir $exe) }
    Hash 'KCDMP.dll (native plugin)' (Join-Path $t.Dir 'KCDMP.dll')
}
Write-Host '== the mod (Lua pak)'
Hash 'repo kdcmp.pak' (Join-Path $root 'kdcmp\Data\kdcmp.pak')
if ($Install) {
    if (-not $GameMods) {
        $reg = (Get-ItemProperty 'HKCU:\Software\KCDMP' -ErrorAction SilentlyContinue).ModsPath
        if ($reg) { $GameMods = $reg }
    }
    if ($GameMods) { Hash 'installed kdcmp.pak' (Join-Path $GameMods 'Data\kdcmp.pak') } else { Write-Host '  (pass -GameMods <Mods\kdcmp folder> to hash the installed pak)' }
}
$setup = Join-Path $root "release\KingdomComeTogether-Setup-$version.exe"
Hash "Setup-$version.exe" $setup
if ($bad -gt 0) { Write-Host "$bad program(s) disagree with VERSION" -ForegroundColor Red; exit 1 }
Write-Host 'every program agrees with VERSION' -ForegroundColor Green
exit 0
