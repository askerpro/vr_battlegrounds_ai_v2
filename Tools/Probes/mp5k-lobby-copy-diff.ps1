param([string]$Candidate = 'tmp/mp5k-audit/Lobby-recovered-after-repeat.unity')
$ErrorActionPreference = 'Stop'
# Только чтение исходной сцены и forensic copy; результат вне Assets.
function Read-Documents([string]$Path) {
    $taskText = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $Path)).Replace("`r`n", "`n")
    $taskDocs = @{}
    foreach ($taskMatch in [regex]::Matches($taskText, '(?ms)^--- !u!\d+ &-?\d+[^\n]*\n.*?(?=^--- !u!|\z)')) {
        $taskHeader = $taskMatch.Value.Split("`n")[0]
        $taskDocs.Add($taskHeader, $taskMatch.Value)
    }
    return $taskDocs
}
$taskOriginal = Read-Documents 'Assets/Scenes/Lobby.unity'
$taskCandidate = Read-Documents $Candidate
$taskAllowed = @('--- !u!1001 &1216357948', '--- !u!1001 &1654449988', '--- !u!1001 &837297328', '--- !u!1001 &945201569')
$taskModified = @($taskOriginal.Keys | Where-Object { $taskCandidate.ContainsKey($_) -and $taskOriginal[$_] -ne $taskCandidate[$_] } | Sort-Object)
$taskAdded = @($taskCandidate.Keys | Where-Object { !$taskOriginal.ContainsKey($_) } | Sort-Object)
$taskRemoved = @($taskOriginal.Keys | Where-Object { !$taskCandidate.ContainsKey($_) } | Sort-Object)
$taskUnexpected = @($taskModified | Where-Object { $_ -notin $taskAllowed })
foreach ($taskHeader in $taskModified) {
    if ($taskCandidate[$taskHeader] -notmatch 'm_SourcePrefab: \{fileID: 100100000, guid: 88273a27f53bfdd4697ec9e56448a068, type: 3\}') {
        $taskUnexpected += "Not canonical Demo: $taskHeader"
    }
}
$taskResult = [ordered]@{candidate=$Candidate;originalDocuments=$taskOriginal.Count;candidateDocuments=$taskCandidate.Count;modified=$taskModified;added=$taskAdded;removed=$taskRemoved;unexpected=$taskUnexpected;passed=($taskAdded.Count -eq 0 -and $taskRemoved.Count -eq 0 -and $taskUnexpected.Count -eq 0)}
$taskResult | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath 'tmp/mp5k-audit/recovered-scene-document-delta.json' -Encoding utf8
$taskResult | ConvertTo-Json -Depth 4
if (!$taskResult.passed) { throw 'Forensic copy changes exceed the four canonical Demo PrefabInstance documents.' }
