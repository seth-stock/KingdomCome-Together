# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
<#
.SYNOPSIS
    The Modding Tools' one-time data setup, without administrator rights (WO-154).

.DESCRIPTION
    Steam's Play button on the "Kingdom Come: Deliverance II Modding tools" entry runs
    Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe, which needs administrator rights and copies or symlinks the retail
    game's data packs into the Modding Tools folder. Without them the game starts and stops at once with
    "Database system error - 114 tables are not loaded".

    This script does the same thing with NTFS hard links: every file of the retail game's Data\ and Localization\ folders
    appears in the same place under the Modding Tools folder, taking no extra disk space and needing no elevation. Hard
    links only work on one drive; with the two games on different drives it copies instead (about 90 GB), or pass
    -Mode Copy / -Mode HardLink yourself. A file that is already there with the same size is left alone, so it is safe to
    run again (do, after the retail game updates: Steam replaces its files, and the links keep pointing at the old ones).

    Nothing of the game is changed or deleted. Nothing is downloaded. Both folders are found through Steam's libraries
    unless given.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Link-GameData.ps1 -WhatIf     # what it would do
    powershell -ExecutionPolicy Bypass -File tools\Link-GameData.ps1             # do it
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $GameDir,
    [string] $ModdingToolsDir,
    [ValidateSet('Auto', 'HardLink', 'Copy')] [string] $Mode = 'Auto'
)
$ErrorActionPreference = 'Stop'

function Get-SteamLibraries {
    $steam = $null
    foreach ($k in 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        $v = (Get-ItemProperty $k -ErrorAction SilentlyContinue).InstallPath
        if ($v) { $steam = $v; break }
    }
    if (-not $steam) { $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath }
    if (-not $steam) { return @() }
    $libs = @($steam)
    $vdf = Join-Path $steam 'config\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($line in Get-Content $vdf) {
            if ($line -match '"path"\s+"([^"]+)"') { $libs += ($Matches[1] -replace '\\\\', '\') }
        }
    }
    $libs | Select-Object -Unique
}

function Find-Install([string] $folderName) {
    foreach ($lib in Get-SteamLibraries) {
        $p = Join-Path $lib "steamapps\common\$folderName"
        if (Test-Path $p) { return $p }
    }
    return $null
}

if (-not $GameDir)         { $GameDir = Find-Install 'KingdomComeDeliverance2' }
if (-not $ModdingToolsDir) { $ModdingToolsDir = Find-Install 'KCD2Mod' }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'Data\Tables.pak'))) {
    throw "Kingdom Come: Deliverance II was not found (looked for ...\steamapps\common\KingdomComeDeliverance2\Data\Tables.pak). Install it in Steam, or pass -GameDir."
}
if (-not $ModdingToolsDir -or -not (Test-Path (Join-Path $ModdingToolsDir 'Bin'))) {
    throw "The Kingdom Come: Deliverance II Modding tools were not found (...\steamapps\common\KCD2Mod). Install them in Steam (a separate library item, free), or pass -ModdingToolsDir."
}
Write-Host "Game:           $GameDir"
Write-Host "Modding Tools:  $ModdingToolsDir"

$sameDrive = ([IO.Path]::GetPathRoot((Resolve-Path $GameDir).Path) -eq [IO.Path]::GetPathRoot((Resolve-Path $ModdingToolsDir).Path))
$use = switch ($Mode) { 'Auto' { if ($sameDrive) { 'HardLink' } else { 'Copy' } } default { $Mode } }
if ($use -eq 'HardLink' -and -not $sameDrive) { throw 'Hard links need both folders on one drive. Use -Mode Copy (about 90 GB), or run WorkspaceSetup.exe as administrator and answer S.' }
Write-Host "Mode:           $use$(if ($Mode -eq 'Auto') { ' (chosen: ' + $(if ($sameDrive) { 'both folders are on one drive' } else { 'the folders are on different drives' }) + ')' })"

$files = @()
foreach ($sub in 'Data', 'Localization') {
    $d = Join-Path $GameDir $sub
    if (Test-Path $d) { $files += Get-ChildItem $d -Recurse -File }
}
$gb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1GB, 1)
Write-Host ("Files in the game's Data and Localization folders: {0} ({1} GB)" -f $files.Count, $gb)
if ($use -eq 'Copy') {
    $free = (Get-PSDrive ([IO.Path]::GetPathRoot((Resolve-Path $ModdingToolsDir).Path).Substring(0, 1))).Free / 1GB
    if ($free -lt ($gb + 5)) { throw ("Not enough disk space for a copy: {0} GB free, about {1} GB needed." -f [math]::Round($free, 1), [math]::Round($gb + 5, 1)) }
}

$made = 0; $same = 0; $failed = @()
foreach ($f in $files) {
    $rel = $f.FullName.Substring($GameDir.TrimEnd('\').Length).TrimStart('\')
    $dst = Join-Path $ModdingToolsDir $rel
    if ((Test-Path $dst) -and (Get-Item $dst).Length -eq $f.Length) { $same++; continue }
    if ($PSCmdlet.ShouldProcess($dst, "$use from the game")) {
        try {
            $dir = Split-Path $dst
            if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
            if (Test-Path $dst) { Remove-Item $dst -Force }   # a stale copy of another size
            if ($use -eq 'HardLink') { New-Item -ItemType HardLink -Path $dst -Value $f.FullName -ErrorAction Stop | Out-Null }
            else { Copy-Item $f.FullName $dst -Force }
            $made++
        } catch { $failed += "$rel : $($_.Exception.Message)" }
    }
}
Write-Host ("Done: {0} placed, {1} already there, {2} failed." -f $made, $same, $failed.Count)
$failed | Select-Object -First 8 | ForEach-Object { Write-Host "  FAILED $_" -ForegroundColor Yellow }
if ($failed.Count -gt 0) { exit 1 }
$ok = Test-Path (Join-Path $ModdingToolsDir 'Data\Tables.pak')
Write-Host ("Data\Tables.pak in the Modding Tools folder: {0}" -f $(if ($ok) { 'present -- the one-time setup is done' } else { 'MISSING' }))
if (-not $ok -and -not $WhatIfPreference) { exit 1 }
