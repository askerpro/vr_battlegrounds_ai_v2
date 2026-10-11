$ErrorActionPreference = 'Stop'
$poseRows = [System.Collections.Generic.List[object]]::new()
function Add-EndBand([string]$bandName, [int]$direction, [int]$treeCount, [int]$seed) {
    $bandRandom = [System.Random]::new($seed)
    $rowCount = $treeCount / 2
    [single]$halfWidth = if ($direction -gt 0) { 42 } else { 44 }
    for ($bandRow = 0; $bandRow -lt 2; $bandRow++) {
        for ($treeIndex = 0; $treeIndex -lt $rowCount; $treeIndex++) {
            [single]$t = $treeIndex / ($rowCount - 1)
            [single]$spacing = $halfWidth * 2 / ($rowCount - 1)
            [single]$xNoise = -.65 + [single]$bandRandom.NextDouble() * 1.3
            [single]$x = -$halfWidth + (2 * $halfWidth) * $t + $bandRow * $spacing * .46 + 1.1 * [single][Math]::Sin($treeIndex * 2.3 + $bandRow * .7) + $xNoise
            [single]$zNoise = -1.2 + [single]$bandRandom.NextDouble() * 2.4
            [single]$z = $direction * ($(if ($bandRow -eq 0) { 52 } else { 64.5 }) + 4.1 * [single][Math]::Sin($t * [single][Math]::PI * 2.3 + $bandRow * 1.4 + .6) + $zNoise)
            if ($direction -gt 0) { $z = [Math]::Max([single]46.2, [Math]::Min([single]73, $z)) }
            [single]$height = 10 + [single]$bandRandom.NextDouble() * 4
            [single]$yaw = [single]$bandRandom.NextDouble() * 360
            $poseRows.Add([pscustomobject]@{ band=$bandName; row=$bandRow; index=$treeIndex; x=$x; z=$z; height=$height; yaw=$yaw })
        }
    }
}
function Add-SideBand([string]$bandName, [int]$side, [int]$treeCount, [int]$seed) {
    $bandRandom = [System.Random]::new($seed)
    for ($treeIndex = 0; $treeIndex -lt $treeCount; $treeIndex++) {
        [single]$t = $treeIndex / ($treeCount - 1)
        [single]$zNoise = -1.2 + [single]$bandRandom.NextDouble() * 2.4
        [single]$z = -51 + 104 * $t + 1.7 * [single][Math]::Sin($treeIndex * 1.9 + $side) + $zNoise
        [single]$xNoise = -1 + [single]$bandRandom.NextDouble() * 2
        [single]$x = $side * (44 + ($treeIndex % 2) * 5.3 + 2.2 * [single][Math]::Sin($t * [single][Math]::PI * 2 + .3 * $side) + $xNoise)
        [single]$height = 10 + [single]$bandRandom.NextDouble() * 4
        [single]$yaw = [single]$bandRandom.NextDouble() * 360
        $poseRows.Add([pscustomobject]@{ band=$bandName; index=$treeIndex; x=$x; z=$z; height=$height; yaw=$yaw })
    }
}
Add-EndBand 'NorthBehindTargets' 1 24 311
Add-EndBand 'SouthBehindCamp' -1 22 419
Add-SideBand 'EastForestLayer' 1 10 521
Add-SideBand 'WestForestLayer' -1 10 631
$ranges = @($poseRows | Group-Object band | ForEach-Object {
    [pscustomobject]@{ band=$_.Name; count=$_.Count; minX=($_.Group.x | Measure-Object -Minimum).Minimum; maxX=($_.Group.x | Measure-Object -Maximum).Maximum; minZ=($_.Group.z | Measure-Object -Minimum).Minimum; maxZ=($_.Group.z | Measure-Object -Maximum).Maximum }
})
$output = 'F:/UnityProjects/Vr_Battlegrounds_ai/tasks/lobby-decoration/reports/implementation/exterior-paid'
$report = [pscustomobject]@{ scope='Offline recipe using seeded System.Random and single-precision casts; native geometry/bounds not measured'; treeCount=$poseRows.Count; bands=$ranges; poses=$poseRows }
$report | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 "$output/forest-band-source-poses.json"
$ranges | ConvertTo-Json -Depth 3
