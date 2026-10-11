# Офлайн-компиляция черновика LobbyNightLighting против DLL Unity 6000.4.1f1 и уже собранных сборок проекта.
# Не трогает Assets/, Library/ и Editor: черновик копируется в reports/, вывод — там же.
# Запуск: powershell -ExecutionPolicy Bypass -File tasks/lobby-decoration/implementation/night-lighting/offline_compile_night.ps1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdkPath = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reportPath = Join-Path $projectRoot 'tasks/lobby-decoration/reports/implementation/night-lighting'
New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
$source = Join-Path $reportPath 'LobbyNightLighting.cs'
Copy-Item -Force (Join-Path $PSScriptRoot 'LobbyNightLighting.cs.txt') $source
$referenceFiles = @()
$referenceFiles += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
$referenceFiles += Get-Item "$projectRoot/Library/ScriptAssemblies/VrBattlegrounds.dll"
$argsList = @('/nologo','/target:library','/nostdlib','/langversion:9.0','/warn:4',"/out:$reportPath/LobbyNightLightingOffline.dll")
$argsList += $referenceFiles | Where-Object { $_.Name -notin @('UnityEditor.dll','UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$argsList += $source
$argsList | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reportPath/offline-compile.rsp"
& dotnet "$sdkPath/Roslyn/bincore/csc.dll" "@$reportPath/offline-compile.rsp" 2>&1 | Tee-Object "$reportPath/offline-compile.txt"
$compileExit = $LASTEXITCODE
"Offline C# compile of LobbyNightLighting draft against Unity 6000.4.1f1 Managed DLLs + Library/ScriptAssemblies/VrBattlegrounds.dll: exit $compileExit. Not a Unity import, not Android, not runtime behavior." | Tee-Object -Append "$reportPath/offline-compile.txt"
exit $compileExit
