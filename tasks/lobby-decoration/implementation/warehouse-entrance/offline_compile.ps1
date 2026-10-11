$ErrorActionPreference = 'Stop'
# Офлайн-компиляция черновика LobbyWarehouseEntranceBuilder (.cs.txt) против установленного Unity 6000.4.1f1.
# Не Unity-импорт, не Android/Quest и не нативная проверка раскладки.
$taskRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdkPath = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reportPath = Join-Path $taskRoot 'tasks/lobby-decoration/reports/implementation/warehouse-entrance'
New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
$referenceFiles = @()
$referenceFiles += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
# GameLog (VrBattlegrounds.Core) — из последней сборки проекта, только чтение.
$referenceFiles += Get-Item "$taskRoot/Library/ScriptAssemblies/VrBattlegrounds.dll"
$argsList = @('/nologo','/target:library','/nostdlib','/langversion:latest','/warn:4',"/out:$reportPath/WarehouseEntranceOffline.dll")
$argsList += $referenceFiles | Where-Object { $_.Name -notin @('UnityEditor.dll','UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$argsList += "$taskRoot/tasks/lobby-decoration/implementation/warehouse-entrance/LobbyWarehouseEntranceBuilder.cs.txt"
$argsList += "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyMilitaryCampSources.cs"
$argsList | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reportPath/offline-compile.rsp"
& dotnet "$sdkPath/Roslyn/bincore/csc.dll" "@$reportPath/offline-compile.rsp" 2>&1 | Out-File -Encoding utf8 "$reportPath/offline-compile.txt"
$compileExit = $LASTEXITCODE
"WarehouseEntrance draft + LobbyMilitaryCampSources against Unity 6000.4.1f1: exit $compileExit; not Unity import/native layout/Quest verification." | Out-File -Append -Encoding utf8 "$reportPath/offline-compile.txt"
Get-Content "$reportPath/offline-compile.txt"
exit $compileExit
