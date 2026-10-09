param([string]$KdcmpLua='')
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-WO134Synthetic.lua') -Raw
$fixture = $base.Substring(0,$base.IndexOf('-- ================= B: NPC bodies'))
$cases = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Test-WO134ParityCases.lua') -Raw
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('kcdmp-parity-' + [Guid]::NewGuid().ToString('N') + '.lua')
try {
    [System.IO.File]::WriteAllText($temporary,$fixture + "`n" + $cases,[System.Text.UTF8Encoding]::new($false))
    & (Join-Path $PSScriptRoot 'Test-NpcSmoothSynthetic.ps1') -KdcmpLua $KdcmpLua -Scenario $temporary -Title 'WO134 container and quest reward parity'
    $result=$LASTEXITCODE
} finally { Remove-Item -LiteralPath $temporary -Force }
exit $result
