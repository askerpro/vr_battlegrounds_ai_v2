# Офлайн-компиляция подэтапа cc-1 (clip-cutover) против ТЕКУЩЕГО worktree (без Unity, без записи в repo).
# Основа — tasks/bots-fix/tools/net-round1/compile-check.ps1 (7 сборок, свои/чужие ошибки раздельно), без payload-ов
# net-round1: после удаления поля стенда PlayerFlow старые пакеты tools/* не компилируются (решение cc-1).
# Ссылки — csproj и Library/ScriptAssemblies основного checkout (только чтение). Выходные файлы — в $Work (%TEMP%).
param(
    [string]$Work = (Join-Path $env:TEMP 'bots-cc1-compile'),
    [string[]]$Own = @(
        'Assets/Scripts/Bots/',
        'Assets/Scripts/Debug/BotCombatStand/',
        'Assets/Editor/VR_Battlegrounds/Bots/',
        'Assets/Editor/VR_Battlegrounds/Debug/BotMenu.cs'
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
$dirty = @(& git -C $repo status --porcelain -- Assets/Scripts/Bots Assets/Scripts/Debug/BotCombatStand Assets/Editor/VR_Battlegrounds/Bots)
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
    function Compile-Slice([string]$assembly, [string]$sourceDirectory, [bool]$excludeNested = $false, [bool]$allowForeign = $false) {
        [xml]$project = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot "$assembly.csproj")
        $refs = @(Get-ProjectRefs $project)
        # Ссылки asmdef worktree по имени, которых нет в csproj основного checkout.
        $asmdef = @(rg --files $sourceDirectory --glob "$assembly.asmdef") | Select-Object -First 1
        if ($asmdef) {
            $definition = Get-Content -Raw -Encoding utf8 -LiteralPath (Join-Path $botsSourceRoot $asmdef) | ConvertFrom-Json
            foreach ($name in @($definition.references)) {
                if ([string]$name -like 'GUID:*') { continue }
                $dll = if ($botsBuiltAssemblies.ContainsKey($name)) { $botsBuiltAssemblies[$name] } else { Join-Path $botsMetadataRoot "Library/ScriptAssemblies/$name.dll" }
                $line = '/reference:"' + $dll + '"'
                if ((Test-Path -LiteralPath $dll) -and -not ($refs -contains $line) -and -not @($refs | Where-Object { $_ -like "*\$name.dll*" -or $_ -like "*/$name.dll*" }).Count) {
                    'ASMDEF EXTRA REF: ' + $assembly + ' -> ' + $name
                    $refs += $line
                }
            }
        }
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
        $ownErrors | Select-Object -First 30 | ForEach-Object { 'OWN ERROR: ' + $_.Line }
        $ownWarnings | Select-Object -First 20 | ForEach-Object { 'OWN WARNING: ' + $_.Line }
        @($errors | Where-Object { -not (Test-Own $_.Line) }) | Select-Object -First 8 | ForEach-Object { 'FOREIGN ERROR: ' + $_.Line }
        if ($code -ne 0 -and $allowForeign -and $ownErrors.Count -eq 0) {
            # Известный офлайн-артефакт: чужие ошибки (Weapons/Sights без вложенных asmdef в Library основного checkout).
            'FOREIGN-ONLY: ' + $assembly + ' - own errors 0'
            return
        }
        if ($code -ne 0) { throw "Offline compilation failed: $assembly" }
        $botsBuiltAssemblies[$assembly] = $output
    }
    Compile-Slice 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
    Compile-Slice 'BlazeAI.Runtime' 'Assets/ThirdParty/Blaze AI/Scripts' $true
    Compile-Slice 'VrBattlegrounds.Weapons.Core' 'Assets/Scripts/Weapons/Core'
    Compile-Slice 'VrBattlegrounds' 'Assets/Scripts' $true
    Compile-Slice 'VrBattlegrounds.BotCombatStand' 'Assets/Scripts/Debug/BotCombatStand'
    Compile-Slice 'VrBattlegrounds.DebugBootstrap' 'Assets/Scripts/Debug/Bootstrap'
    Compile-Slice 'Assembly-CSharp-Editor' 'Assets/Editor' $true $true
    'RESULT: all slices compiled'
}
finally {
    Pop-Location
    $summary | ConvertTo-Json -Compress | Set-Content -Encoding utf8 -LiteralPath (Join-Path $Work 'summary.json')
}
