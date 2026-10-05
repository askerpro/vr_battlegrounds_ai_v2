param([switch]$CompactExisting)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$reportDir = Join-Path $projectRoot 'Docs/tasks/report/map-runtime-bootstrap'
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$summaryPath = Join-Path $reportDir 'source-baseline.json'
$detailPath = Join-Path $projectRoot 'Docs/tasks/report/map-runtime-bootstrap/details/source-baseline-detail.json'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $detailPath) | Out-Null
if ($CompactExisting) {
    $original = Get-Content -LiteralPath $summaryPath -Raw -Encoding utf8 | ConvertFrom-Json
    Copy-Item -LiteralPath $summaryPath -Destination $detailPath
    $compact = @{
        utc = $original.utc; head = $original.head; branch = $original.branch
        foreignDirty = @($original.foreignDirty | Where-Object { $_ -notmatch 'Tools/Probes/MapRuntimeBootstrap/|Docs/tasks/report/map-runtime-bootstrap/' })
        detailReport = 'Docs/tasks/report/map-runtime-bootstrap/details/source-baseline-detail.json'
        limits = $original.limits
        files = @($original.files | ForEach-Object { @{path = $_.path; sha256 = $_.sha256; selectedYamlCount = $_.selectedYaml.Count; selectedYamlSample = @($_.selectedYaml | Select-Object -First 10)} })
    }
    $compact | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Output ('Compacted original baseline: {0} files, {1} foreign paths; full detail kept in tmp' -f $compact.files.Count, $compact.foreignDirty.Count)
    exit
}
$paths = @(
    'Assets/Scripts/Managers/ManagerBootstrap.cs', 'Assets/Scripts/Managers/MapLoader.cs',
    'Assets/Scripts/Managers/MapReferee.cs', 'Assets/Scripts/Managers/Series.cs',
    'Assets/Scripts/Managers/SessionManager.cs', 'Assets/Scripts/Network/GameNetworkManager.cs',
    'Assets/Scripts/Network/NetworkStateRelay.cs', 'Assets/Scripts/GameModes/GameMode.cs',
    'Assets/Scripts/GameModes/ModeStartCleanup.cs', 'Assets/Prefabs/Managers/SessionContext.prefab',
    'Assets/Prefabs/Managers/--- MANAGERS ---.prefab', 'Assets/Data/Maps/MapRegistry.asset',
    'Assets/Data/GameModes/GameModeRegistry.asset'
)
$paths += @(rg --files Assets/Scenes Assets/Data/Maps | Where-Object { $_ -match '\.(unity|asset)$' })
$records = foreach ($relativePath in $paths) {
    $absolutePath = Join-Path $projectRoot $relativePath
    if (!(Test-Path -LiteralPath $absolutePath)) { continue }
    $lines = @()
    if ($relativePath -match '\.(prefab|unity|asset)$') {
        $lines = @(Select-String -LiteralPath $absolutePath -Pattern 'm_Script:|m_SourcePrefab:|sceneName:|modeId:|_presetId:|m_SceneId:|sceneId:|_uniqueId:|_mapRegistry:|_gameModeRegistry:|_sessionContextPrefab:|m_LocalPosition:|m_LocalRotation:|m_LocalScale:|arsenalPreset:' | ForEach-Object { @{ line = $_.LineNumber; value = $_.Line.Trim() } })
    }
    @{ path = $relativePath; sha256 = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash; selectedYaml = $lines }
}
$report = @{ utc = [DateTime]::UtcNow.ToString('o'); head = (git --no-pager rev-parse HEAD); branch = (git --no-pager branch --show-current); foreignDirty = @(git --no-pager status --porcelain=v1 -uall); files = @($records); limits = 'YAML local transforms and prefab refs captured; effective world transforms/UXR registration require Unity inventory.' }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Output ('Captured {0} source/assets; {1} foreign dirty entries' -f $records.Count, $report.foreignDirty.Count)
