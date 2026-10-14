# Офлайн-компиляция пакета cc0-match (clip-cutover cc-0: BotStandMatchProbe + payload-ы) против ТЕКУЩЕГО worktree
# (без Unity, без записи в repo). Копия net-round1/compile-check.ps1 (тот же разбор своих/чужих ошибок).
# Основа — tasks/bots-fix/tools/pf-prototype/compile-check.ps1 (7 сборок, свои/чужие ошибки раздельно) и
# eq-round3/compile-check.ps1 (payload-ы b*.cs в обёртке MCP execute_code, C# 7.3).
# Отличие: ссылки asmdef по имени, которых нет в csproj основного checkout (новая ссылка worktree, например
# Mirror.Components у VrBattlegrounds.BotCombatStand), добавляются из Library/ScriptAssemblies основного checkout.
# Все выходные файлы — в $Work (по умолчанию %TEMP%).
param(
    [string]$Work = (Join-Path $env:TEMP 'bots-cc0-match-compile'),
    [string]$LangVersion = '7.3',
    [string[]]$Own = @(
        'Assets/Scripts/Bots/',
        'Assets/Scripts/Debug/BotCombatStand/',
        'Assets/Scripts/Managers/PlayersManager.cs',
        'Assets/Editor/VR_Battlegrounds/Bots/'
    )
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$payloadRoot = $PSScriptRoot
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
        $ownErrors | Select-Object -First 20 | ForEach-Object { 'OWN ERROR: ' + $_.Line }
        $ownWarnings | Select-Object -First 20 | ForEach-Object { 'OWN WARNING: ' + $_.Line }
        @($errors | Where-Object { -not (Test-Own $_.Line) }) | Select-Object -First 8 | ForEach-Object { 'FOREIGN ERROR: ' + $_.Line }
        if ($code -ne 0 -and $allowForeign -and $ownErrors.Count -eq 0) {
            # Известный офлайн-артефакт: чужие ошибки (Weapons/Sights без вложенных asmdef в Library основного checkout).
            'FOREIGN-ONLY: ' + $assembly + ' — свои ошибки 0; payload-ы берут Library/ScriptAssemblies/' + $assembly + '.dll основного checkout'
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

    # Payload-ы cc0-match в обёртке MCP execute_code (MCPForUnity ExecuteCode.WrapUserCode): те же using, static object-метод.
    [xml]$editorProject = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot 'Assembly-CSharp-Editor.csproj')
    $refs = @(Get-ProjectRefs $editorProject)
    $refs += $botsBuiltAssemblies.Values | ForEach-Object { '/reference:"' + $_ + '"' }
    if (-not $botsBuiltAssemblies.ContainsKey('Assembly-CSharp-Editor')) {
        $refs += '/reference:"' + (Join-Path $botsMetadataRoot 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll') + '"'
    }
    $payloadSources = @()
    foreach ($file in Get-ChildItem -LiteralPath $payloadRoot -Filter 'b*.cs' | Sort-Object Name) {
        $class = 'MCPDynamicCode_' + ($file.BaseName -replace '[^A-Za-z0-9]', '_')
        $body = [IO.File]::ReadAllText($file.FullName)
        $wrapper = "using System;`nusing System.Collections.Generic;`nusing System.Linq;`nusing System.Reflection;`nusing UnityEngine;`nusing UnityEditor;`n" +
            "public static class $class`n{`n    public static object Execute()`n    {`n$body`n    }`n}`n"
        $generated = Join-Path $botsOutputRoot ($file.BaseName + '.wrapped.cs')
        [IO.File]::WriteAllText($generated, $wrapper, [Text.UTF8Encoding]::new($false))
        $payloadSources += '"' + $generated + '"'
    }
    $response = Join-Path $botsOutputRoot 'Payloads.rsp'
    $options = @('/nologo','/target:library',"/langversion:$LangVersion",'/nostdlib+','/warn:1',('/out:"' + (Join-Path $botsOutputRoot 'Payloads.dll') + '"'))
    [IO.File]::WriteAllLines($response, @($options + ($refs | Select-Object -Unique) + $payloadSources), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $botsOutputRoot 'Payloads.log'
    & dotnet $csc ('@' + $response) *> $log
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
    $row = [pscustomobject]@{assembly='Payloads'; langVersion=$LangVersion; exitCode=$code; errors=$errors.Count; payloads=$payloadSources.Count; log=$log}
    $row | ConvertTo-Json -Compress
    $script:summary += $row
    if ($code -ne 0) { $errors | Select-Object -First 15 | ForEach-Object { 'PAYLOAD ERROR: ' + $_.Line }; throw 'Offline compilation failed: Payloads' }
    'RESULT: payloads compiled'
}
finally {
    Pop-Location
    $summary | ConvertTo-Json -Compress | Set-Content -Encoding utf8 -LiteralPath (Join-Path $Work 'summary.json')
}
