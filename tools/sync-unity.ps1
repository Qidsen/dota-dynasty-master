# Builds the Unity-independent core (Release, netstandard2.1) and copies it into the Unity project,
# together with reference checksums for the runtime determinism check (DECISION_LOG D-029).
# Run after changing anything under src/, before opening or building the Unity project:
#   powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$unity = Join-Path $root 'unity/DotaDynasty'
$plugins = Join-Path $unity 'Assets/Plugins/Dynasty'
$streaming = Join-Path $unity 'Assets/StreamingAssets'

dotnet build (Join-Path $root 'src/Dynasty.Presentation/Dynasty.Presentation.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }

New-Item -ItemType Directory -Force $plugins | Out-Null
New-Item -ItemType Directory -Force $streaming | Out-Null
$output = Join-Path $root 'src/Dynasty.Presentation/bin/Release/netstandard2.1'
foreach ($name in 'Dynasty.Core', 'Dynasty.MatchSimulation', 'Dynasty.HeroAI', 'Dynasty.Presentation') {
    Copy-Item (Join-Path $output "$name.dll") $plugins -Force
    Copy-Item (Join-Path $output "$name.xml") $plugins -Force
}

dotnet run --project (Join-Path $root 'tools/Dynasty.MatchLab/Dynasty.MatchLab.csproj') -c Release -- probe (Join-Path $streaming 'determinism-reference.txt') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Determinism reference run failed.' }

Write-Host "Core copied to $plugins; reference checksums written to $streaming"
