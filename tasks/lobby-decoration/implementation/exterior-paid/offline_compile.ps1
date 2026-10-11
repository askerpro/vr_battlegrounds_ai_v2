$ErrorActionPreference = 'Stop'
$taskRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdkPath = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reportPath = Join-Path $taskRoot 'tasks/lobby-decoration/reports/implementation/exterior-paid'
New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
$referenceFiles = @()
$referenceFiles += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
$argsList = @('/nologo','/target:library','/nostdlib','/langversion:latest',"/out:$reportPath/ExteriorPaidOffline.dll")
$argsList += $referenceFiles | Where-Object { $_.Name -notin @('UnityEditor.dll','UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$argsList += "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyExteriorDecorationBuilder.cs"
$argsList += "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyMilitaryCampSources.cs"
$argsList | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reportPath/offline-compile.rsp"
& dotnet "$sdkPath/Roslyn/bincore/csc.dll" "@$reportPath/offline-compile.rsp" 2>&1 | Tee-Object "$reportPath/offline-compile.txt"
$compileExit = $LASTEXITCODE
"Exterior paid helper + read-only source resolver against Unity6000.4.1f1: exit $compileExit; no native/Android/Quest/material import verification." | Tee-Object -Append "$reportPath/offline-compile.txt"
exit $compileExit
