$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdkPath = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reportPath = Join-Path $taskRoot 'tasks/lobby-decoration/reports/implementation'
New-Item -ItemType Directory -Force -Path $reportPath | Out-Null
$referenceFiles = @()
$referenceFiles += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$referenceFiles += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
$referenceFiles += Get-Item "$taskRoot/Library/ScriptAssemblies/VrBattlegrounds.dll"
$referenceFiles += Get-Item "$taskRoot/Library/ScriptAssemblies/Assembly-CSharp-Editor.dll"
$referenceFiles += Get-Item "$taskRoot/Library/ScriptAssemblies/UltimateXR.dll"
$referenceFiles += Get-Item "$taskRoot/Library/ScriptAssemblies/Mirror.dll"
$argsList = @('/nologo','/target:library','/nostdlib','/langversion:latest',"/out:$reportPath/LobbyOffline.dll")
$argsList += $referenceFiles | Where-Object { $_.Name -notin @('UnityEditor.dll','UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$argsList += Get-ChildItem "$taskRoot/Assets/Scripts/Environment/Audio" -Filter '*.cs' | ForEach-Object FullName
$argsList += Get-ChildItem "$taskRoot/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration" -Filter '*.cs' | ForEach-Object FullName
$argsList | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reportPath/offline-compile.rsp"
& dotnet "$sdkPath/Roslyn/bincore/csc.dll" "@$reportPath/offline-compile.rsp" 2>&1 | Tee-Object "$reportPath/offline-compile.txt"
$compileExit = $LASTEXITCODE
"Offline C# compilation against installed Unity 6000.4.1f1 and existing project DLLs: exit $compileExit; not Unity project or Android verification." | Tee-Object -Append "$reportPath/offline-compile.txt"
exit $compileExit
