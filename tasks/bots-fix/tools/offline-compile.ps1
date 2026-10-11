$ErrorActionPreference = 'Stop'
$botsSourceRoot = (Get-Location).Path
$botsMetadataRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$botsOutputRoot = Join-Path $botsSourceRoot 'tasks/bots-fix/reports/offline-compile'
[IO.Directory]::CreateDirectory($botsOutputRoot) | Out-Null
$botsBuiltAssemblies = @{}
function Compile-BotsSlice([string]$assembly, [string]$sourceDirectory, [bool]$excludeNested = $false) {
    [xml]$project = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot "$assembly.csproj")
    $refs = @($project.Project.ItemGroup.Reference | ForEach-Object {
        if ($_.HintPath) {
            $path = [string]$_.HintPath
            if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $botsMetadataRoot $path }
            $name = [IO.Path]::GetFileNameWithoutExtension($path)
            if ($botsBuiltAssemblies.ContainsKey($name)) { $path = $botsBuiltAssemblies[$name] }
            if (Test-Path -LiteralPath $path) { '/reference:"' + $path + '"' }
        }
    })
    $refs += @($project.SelectNodes('//ProjectReference') | ForEach-Object {
        $name = [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include'))
        $path = if ($botsBuiltAssemblies.ContainsKey($name)) { $botsBuiltAssemblies[$name] }
            else { Join-Path $botsMetadataRoot "Library/ScriptAssemblies/$name.dll" }
        '/reference:"' + $path + '"'
    })
    $nested = @()
    if ($excludeNested) {
        $nested = @(rg --files $sourceDirectory --glob '*.asmdef' | Where-Object { [IO.Path]::GetFileName($_) -ne "$assembly.asmdef" } |
            ForEach-Object { (Join-Path $botsSourceRoot ([IO.Path]::GetDirectoryName($_))) + '\' })
    }
    $sources = @(rg --files $sourceDirectory --glob '*.cs' | ForEach-Object {
        $path = Join-Path $botsSourceRoot $_
        if (-not @($nested | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) { '"' + $path + '"' }
    })
    $defines = [string]$project.SelectSingleNode('//DefineConstants').InnerText
    $output = Join-Path $botsOutputRoot "$assembly.dll"
    $options = @('/nologo','/target:library','/langversion:9','/unsafe+','/nostdlib+','/warn:4',('/define:' + $defines),('/out:"' + $output + '"'))
    $response = Join-Path $botsOutputRoot "$assembly.rsp"
    [IO.File]::WriteAllLines($response, @($options + ($refs | Select-Object -Unique) + $sources), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $botsOutputRoot "$assembly.log"
    & dotnet 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll' ('@' + $response) *> $log
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
    [pscustomobject]@{assembly=$assembly; exitCode=$code; errors=$errors.Count; sources=$sources.Count; log=$log} | ConvertTo-Json -Compress
    if ($code -ne 0) { $errors | Select-Object -First 8 | ForEach-Object { $_.Line }; throw "Offline compilation failed: $assembly" }
    $botsBuiltAssemblies[$assembly] = $output
}
Compile-BotsSlice 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
Compile-BotsSlice 'BlazeAI.Runtime' 'Assets/ThirdParty/Blaze AI/Scripts' $true
Compile-BotsSlice 'VrBattlegrounds.Weapons.Core' 'Assets/Scripts/Weapons/Core'
Compile-BotsSlice 'VrBattlegrounds' 'Assets/Scripts' $true
Compile-BotsSlice 'VrBattlegrounds.BotCombatStand' 'Assets/Scripts/Debug/BotCombatStand'
Compile-BotsSlice 'Assembly-CSharp-Editor' 'Assets/Editor' $true

# Проверка execute_code payload без Unity и без постоянных тестов игровой механики.
[xml]$botsEditorProject = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot 'Assembly-CSharp-Editor.csproj')
$botsProbeRefs = @($botsEditorProject.Project.ItemGroup.Reference | ForEach-Object {
    if ($_.HintPath) {
        $path = [string]$_.HintPath
        if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $botsMetadataRoot $path }
        $name = [IO.Path]::GetFileNameWithoutExtension($path)
        if ($botsBuiltAssemblies.ContainsKey($name)) { $path = $botsBuiltAssemblies[$name] }
        if (Test-Path -LiteralPath $path) { '/reference:"' + $path + '"' }
    }
})
$botsProbeRefs += @($botsEditorProject.SelectNodes('//ProjectReference') | ForEach-Object {
    $name = [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include'))
    $path = if ($botsBuiltAssemblies.ContainsKey($name)) { $botsBuiltAssemblies[$name] }
        else { Join-Path $botsMetadataRoot "Library/ScriptAssemblies/$name.dll" }
    '/reference:"' + $path + '"'
})
$botsProbeRefs += $botsBuiltAssemblies.Values | ForEach-Object { '/reference:"' + $_ + '"' }
$botsProbeResponse = Join-Path $botsOutputRoot 'Probes.rsp'
$botsProbeArgs = @('/nologo','/target:library','/langversion:9','/nostdlib+',
    ('/out:"' + (Join-Path $botsOutputRoot 'Probes.dll') + '"'))
$botsHistoricalProbeRoot = Join-Path $botsSourceRoot 'tmp/bots-refactor/offline-compile'
$botsProbeSources = @('ColdIntegration.cs','BodyLifecycle.cs') | ForEach-Object { '"' + (Join-Path $botsHistoricalProbeRoot $_) + '"' }
foreach ($botsProbeName in @('equipment-build', 'equipment-runtime', 'controller-repeatability', 'human-preview', 'clip-authoring-probe', 'cyborg-wrist-probe', 'calibration-build', 'clip-calibration-preview', 'two-hand-fit-probe')) {
    $botsProbeBody = [IO.File]::ReadAllText((Join-Path $botsSourceRoot "tasks/bots-fix/tools/$botsProbeName.cs"))
    $botsProbeGenerated = Join-Path $botsOutputRoot "$botsProbeName.cs"
    $botsProbeClass = $botsProbeName.Replace('-', '_')
    $botsProbeWrapper = "using System.Linq; using UnityEngine; using UnityEditor; public sealed class $botsProbeClass { public object Run() {`n$botsProbeBody`n} }"
    [IO.File]::WriteAllText($botsProbeGenerated, $botsProbeWrapper, [Text.UTF8Encoding]::new($false))
    $botsProbeSources += '"' + $botsProbeGenerated + '"'
}
[IO.File]::WriteAllLines($botsProbeResponse, @($botsProbeArgs + ($botsProbeRefs | Select-Object -Unique) + $botsProbeSources), [Text.UTF8Encoding]::new($false))
$botsProbeLog = Join-Path $botsOutputRoot 'Probes.log'
& dotnet 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll' ('@' + $botsProbeResponse) *> $botsProbeLog
$botsProbeCode = $LASTEXITCODE
[pscustomobject]@{assembly='Temporary probes'; exitCode=$botsProbeCode; log=$botsProbeLog} | ConvertTo-Json -Compress
if ($botsProbeCode -ne 0) { Get-Content -LiteralPath $botsProbeLog | Select-Object -First 10; exit $botsProbeCode }
