param([string]$Config = "Release")

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$jsonPath   = Join-Path $PSScriptRoot "TreasureLedger.json"
$csprojPath = Join-Path $PSScriptRoot "TreasureLedger.csproj"

$json = Get-Content $jsonPath -Raw -Encoding UTF8
$m = [regex]::Match($json, 'AssemblyVersion"[^"]*"([^"]+)"')
if (-not $m.Success) { throw "cannot find AssemblyVersion" }

$current = $m.Groups[1].Value
$parts = $current.Split('.')
if ($parts.Length -ne 4) { throw "bad version: $current" }

$parts[3] = [string]([int]$parts[3] + 1)
$newVersion = $parts -join '.'

Write-Host "[TL] current: $current"
Write-Host "[TL] new:     $newVersion"

$json = $json -replace '"AssemblyVersion": "[^"]+"', ('"AssemblyVersion": "' + $newVersion + '"')
[IO.File]::WriteAllText($jsonPath, $json, (New-Object System.Text.UTF8Encoding($false)))

$csproj = Get-Content $csprojPath -Raw -Encoding UTF8
$csproj = $csproj -replace '<Version>[^<]*</Version>', ('<Version>' + $newVersion + '</Version>')
[IO.File]::WriteAllText($csprojPath, $csproj, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "[TL] building..."
& dotnet build -c $Config --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[TL] build FAILED, not deployed" -ForegroundColor Red
    exit 1
}

$outDir = Join-Path $PSScriptRoot ('bin\' + $Config)
$devDir = Join-Path $env:APPDATA 'XIVLauncherCN\devPlugins\TreasureLedger'
if (-not (Test-Path $devDir)) { New-Item -ItemType Directory -Path $devDir -Force | Out-Null }

Copy-Item (Join-Path $outDir 'TreasureLedger.dll') (Join-Path $devDir 'TreasureLedger.dll') -Force
Copy-Item $jsonPath (Join-Path $devDir 'TreasureLedger.json') -Force

Write-Host "[TL] deployed v$newVersion" -ForegroundColor Green
Write-Host "[TL] Dalamud will auto-reload"
