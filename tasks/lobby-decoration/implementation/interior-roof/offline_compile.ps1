$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdkPath = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reportPath = Join-Path $taskRoot 'tasks/lobby-decoration/reports/implementation/interior-roof'
New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
$references = @()
$references += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$references += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$references += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
$references += Get-Item "$taskRoot/Library/ScriptAssemblies/VrBattlegrounds.dll"
$arguments = @('/nologo','/target:library','/nostdlib','/langversion:latest',"/out:$reportPath/LobbyRoofOffline.dll")
$arguments += $references | Where-Object { $_.Name -notin @('UnityEditor.dll','UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$arguments += "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyTentRoofBuilder.cs"
$arguments += "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyMilitaryCampSources.cs"
$arguments | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reportPath/offline-compile.rsp"
& dotnet "$sdkPath/Roslyn/bincore/csc.dll" "@$reportPath/offline-compile.rsp" 2>&1 | Tee-Object "$reportPath/offline-compile.txt"
$result = $LASTEXITCODE
"Offline roof C# compile against Unity 6000.4.1f1 and existing project DLL: exit $result; not native geometry/UV/scene verification." | Tee-Object -Append "$reportPath/offline-compile.txt"
exit $result
