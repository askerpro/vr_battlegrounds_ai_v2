param([switch]$RuntimeOnly)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$unityData = 'C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data'
$sdk = (Get-ChildItem 'C:/Program Files/dotnet/sdk' -Directory | Sort-Object Name | Select-Object -Last 1).FullName
$reports = Join-Path $project 'tasks/lobby-decoration/reports/implementation/zone-alignment'
New-Item -ItemType Directory -Force -Path $reports | Out-Null
$references = @()
$references += Get-ChildItem "$unityData/Managed" -Filter '*.dll' -Recurse
$references += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api" -Filter '*.dll'
$references += Get-ChildItem "$unityData/MonoBleedingEdge/lib/mono/4.8-api/Facades" -Filter '*.dll'
$references += Get-ChildItem "$project/Library/ScriptAssemblies" -Filter '*.dll'
$label = if ($RuntimeOnly) { 'runtime-data' } else { 'editor' }
$argsList = @('/nologo', '/target:library', '/nostdlib', '/langversion:latest', "/out:$reports/LobbyZoneOffline-$label.dll")
if (!$RuntimeOnly) { $argsList += '/define:UNITY_EDITOR' }
$argsList += $references | Where-Object { $_.Name -notin @('UnityEditor.dll', 'UnityEngine.dll') } | Sort-Object Name -Unique | ForEach-Object { '/reference:' + $_.FullName }
$argsList += "$project/Assets/Scripts/Environment/LobbyZoneLayoutBinding.cs"
if (!$RuntimeOnly) {
    $argsList += "$project/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyZoneLayoutEditor.cs"
    $argsList += "$project/Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyZoneHierarchyBuilder.cs"
}
$argsList | ForEach-Object { '"' + $_ + '"' } | Set-Content -Encoding UTF8 "$reports/offline-compile-$label.rsp"
& dotnet "$sdk/Roslyn/bincore/csc.dll" "@$reports/offline-compile-$label.rsp" 2>&1 | Tee-Object "$reports/offline-compile-$label.txt"
$result = $LASTEXITCODE
"Offline $label Unity 6000.4.1f1 API/project DLL compilation: exit $result. This is not Unity/Android or runtime acceptance." | Tee-Object -Append "$reports/offline-compile-$label.txt"
exit $result
