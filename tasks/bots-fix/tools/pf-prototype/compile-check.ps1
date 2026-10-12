# Офлайн-компиляция этапа pf-prototype против ТЕКУЩЕГО worktree (без Unity, без записи в repo).
# Основа — tasks/bots-fix/tools/aim-round4/compile-check.ps1 (без payload-ов execute_code).
# Исходники — сам worktree (все *.cs под Assets так, как их увидит Unity); метаданные сборок
# (csproj, Library/ScriptAssemblies, HintPath) — из основного checkout read-only.
# Все выходные файлы — в $Work (по умолчанию %TEMP%). Предупреждения считаются отдельно
# для своих файлов (-Own) и для чужих; ошибки — тоже раздельно.
param(
    [string]$Work = (Join-Path $env:TEMP 'bots-pf-prototype-compile'),
    [string[]]$Own = @(
        'Assets/Scripts/Bots/',
        'Assets/Scripts/Debug/BotCombatStand/',
        'Assets/Scripts/Managers/PlayersManager.cs',
        'Assets/Editor/VR_Battlegrounds/Bots/'
    )
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$botsMetadataRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$csc = 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll'
if (Test-Path -LiteralPath $Work) { $Work = $Work + '-' + [DateTime]::UtcNow.Ticks }
$botsOutputRoot = Join-Path $Work 'out'
[IO.Directory]::CreateDirectory($botsOutputRoot) | Out-Null
$head = & git -C $repo rev-parse HEAD
$dirty = @(& git -C $repo status --porcelain -- Assets/Scripts/Bots Assets/Scripts/Debug/BotCombatStand Assets/Scripts/Managers/PlayersManager.cs Assets/Editor/VR_Battlegrounds/Bots)
[pscustomobject]@{source=$repo; head=$head; dirtyOwnPaths=$dirty.Count; work=$Work} | ConvertTo-Json -Compress
$ownPatterns = @($Own | ForEach-Object { ($_ -replace '/', '\').ToLowerInvariant() })
function Test-Own([string]$line) {
    $lower = $line.ToLowerInvariant()
    foreach ($p in $ownPatterns) { if ($lower.Contains($p)) { return $true } }
    return $false
}
$summary = @()
Push-Location $repo
try {
    $botsSourceRoot = (Get-Location).Path
    $botsBuiltAssemblies = @{}
    function Get-ProjectRefs([xml]$project) {
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
        return $refs
    }
    function Compile-Slice([string]$assembly, [string]$sourceDirectory, [bool]$excludeNested = $false) {
        [xml]$project = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot "$assembly.csproj")
        $refs = Get-ProjectRefs $project
        $nested = @()
        if ($excludeNested) {
            $nested = @(rg --files $sourceDirectory --glob '*.asmdef' | Where-Object { [IO.Path]::GetFileName($_) -ne "$assembly.asmdef" } |
                ForEach-Object { (Join-Path $botsSourceRoot ([IO.Path]::GetDirectoryName($_))) + '\' })
            $nested += @(rg --files $sourceDirectory --glob '*.asmref' | ForEach-Object { (Join-Path $botsSourceRoot ([IO.Path]::GetDirectoryName($_))) + '\' })
        }
        $sources = @(rg --files $sourceDirectory --glob '*.cs' | ForEach-Object {
            $path = Join-Path $botsSourceRoot $_
            if (-not @($nested | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) { '"' + $path + '"' }
        })
        if ($assembly -eq 'VrBattlegrounds.DebugBootstrap') {
            $sources += @(rg --files 'Assets/Editor/VR_Battlegrounds/Debug/Bootstrap' --glob '*.cs' |
                ForEach-Object { '"' + (Join-Path $botsSourceRoot $_) + '"' })
        }
        $defines = [string]$project.SelectSingleNode('//DefineConstants').InnerText
        $output = Join-Path $botsOutputRoot "$assembly.dll"
        $options = @('/nologo','/target:library','/langversion:9','/unsafe+','/nostdlib+','/warn:4',('/define:' + $defines),('/out:"' + $output + '"'))
        $response = Join-Path $botsOutputRoot "$assembly.rsp"
        [IO.File]::WriteAllLines($response, @($options + ($refs | Select-Object -Unique) + $sources), [Text.UTF8Encoding]::new($false))
        $log = Join-Path $botsOutputRoot "$assembly.log"
        & dotnet $csc ('@' + $response) *> $log
        $code = $LASTEXITCODE
        $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
        $warnings = @(Select-String -LiteralPath $log -Pattern ': warning ')
        $ownErrors = @($errors | Where-Object { Test-Own $_.Line })
        $ownWarnings = @($warnings | Where-Object { Test-Own $_.Line })
        $row = [pscustomobject]@{assembly=$assembly; exitCode=$code; errors=$errors.Count; ownErrors=$ownErrors.Count;
            warnings=$warnings.Count; ownWarnings=$ownWarnings.Count; sources=$sources.Count; log=$log}
        $row | ConvertTo-Json -Compress
        $script:summary += $row
        $ownErrors | Select-Object -First 20 | ForEach-Object { 'OWN ERROR: ' + $_.Line }
        $ownWarnings | Select-Object -First 20 | ForEach-Object { 'OWN WARNING: ' + $_.Line }
        @($errors | Where-Object { -not (Test-Own $_.Line) }) | Select-Object -First 8 | ForEach-Object { 'FOREIGN ERROR: ' + $_.Line }
        if ($code -ne 0) { throw "Offline compilation failed: $assembly" }
        $botsBuiltAssemblies[$assembly] = $output
    }
    Compile-Slice 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
    Compile-Slice 'BlazeAI.Runtime' 'Assets/ThirdParty/Blaze AI/Scripts' $true
    Compile-Slice 'VrBattlegrounds.Weapons.Core' 'Assets/Scripts/Weapons/Core'
    Compile-Slice 'VrBattlegrounds' 'Assets/Scripts' $true
    Compile-Slice 'VrBattlegrounds.BotCombatStand' 'Assets/Scripts/Debug/BotCombatStand'
    Compile-Slice 'VrBattlegrounds.DebugBootstrap' 'Assets/Scripts/Debug/Bootstrap'
    Compile-Slice 'Assembly-CSharp-Editor' 'Assets/Editor' $true
    'RESULT: all slices compiled'
}
finally {
    Pop-Location
    $summary | ConvertTo-Json -Compress | Set-Content -Encoding utf8 -LiteralPath (Join-Path $Work 'summary.json')
}
