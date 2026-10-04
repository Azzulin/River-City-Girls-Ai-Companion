param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\River City Girls"
)
$ErrorActionPreference = "Stop"
$managed = Join-Path $GameDir "RiverCityGirls_Data\Managed"
$bepCore = Join-Path $GameDir "BepInEx\core"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $PSScriptRoot "bin\RCG_AICompanion.dll"
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$refs = @(
    "$managed\mscorlib.dll", "$managed\System.dll", "$managed\System.Core.dll",
    "$managed\Assembly-CSharp.dll", "$managed\Assembly-CSharp-firstpass.dll", "$managed\Rewired_Core.dll",
    "$managed\UnityEngine.dll", "$managed\DOTween.dll", "$managed\TextMeshPro-2017.2-1.0.56-Runtime.dll",
    "$bepCore\BepInEx.dll", "$bepCore\0Harmony.dll"
)
$refs += Get-ChildItem $managed -Filter "UnityEngine.*Module.dll" | ForEach-Object { $_.FullName }
$refs += "$managed\UnityEngine.UI.dll"

$cscArgs = @("/nologo", "/target:library", "/optimize+", "/nostdlib+", "/noconfig", "/out:$out")
$cscArgs += $refs | ForEach-Object { "/r:$_" }
$cscArgs += Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs | ForEach-Object { $_.FullName }

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }
Write-Host "OK: $out"

$plugins = Join-Path $GameDir "BepInEx\plugins"
New-Item -ItemType Directory -Force $plugins | Out-Null
Copy-Item $out $plugins -Force
Write-Host "Instalado em: $plugins"
