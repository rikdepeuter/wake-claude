# Bouwt bin\wake-claude.exe met de Roslyn-compiler van Visual Studio Build Tools, tegen .NET Framework 4.x.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Get-ChildItem "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\Roslyn\csc.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $csc) { throw 'Roslyn csc.exe niet gevonden (Visual Studio Build Tools).' }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $out | Out-Null

& $csc.FullName /nologo /target:exe /platform:anycpu /optimize+ /langversion:7.3 `
    "/out:$out\wake-claude.exe" `
    "/r:$fw\System.dll" "/r:$fw\System.Core.dll" "/r:$fw\System.Web.Extensions.dll" `
    (Get-ChildItem "$root\src\WakeClaude\*.cs").FullName
if ($LASTEXITCODE -ne 0) { throw "Compileren mislukt ($LASTEXITCODE)." }
"Gebouwd: $out\wake-claude.exe"
