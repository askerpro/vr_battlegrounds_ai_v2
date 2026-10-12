# Офлайн-компиляция payload-ов раунда pf-round1 (этап pf-prototype) против ТЕКУЩЕГО worktree (без Unity, без git archive, без записи в repo).
# Исходники — сам worktree (все *.cs/*.asmdef/*.asmref под Assets, как их увидит Unity при импорте technical input);
# метаданные сборок (csproj, Library/ScriptAssemblies, HintPath) — из основного checkout read-only.
# Все выходные файлы — в $Work (по умолчанию %TEMP%). Затем 7 сборок проекта и payload-ы b*.cs.
# Обёртка повторяет MCP execute_code (MCPForUnity ExecuteCode.WrapUserCode): те же using, static object-метод.
param(
    [string]$Work = (Join-Path $env:TEMP 'bots-pf-round1-compile'),
    [string]$LangVersion = '7.3'
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
[pscustomobject]@{source=$repo; head=$head; dirtyBotsPaths=$dirty.Count; work=$Work} | ConvertTo-Json -Compress
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
        $options = @('/nologo','/target:library','/langversion:9','/unsafe+','/nostdlib+','/warn:0',('/define:' + $defines),('/out:"' + $output + '"'))
        $response = Join-Path $botsOutputRoot "$assembly.rsp"
        [IO.File]::WriteAllLines($response, @($options + ($refs | Select-Object -Unique) + $sources), [Text.UTF8Encoding]::new($false))
        $log = Join-Path $botsOutputRoot "$assembly.log"
        & dotnet $csc ('@' + $response) *> $log
        $code = $LASTEXITCODE
        $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
        [pscustomobject]@{assembly=$assembly; exitCode=$code; errors=$errors.Count; sources=$sources.Count} | ConvertTo-Json -Compress
        if ($code -ne 0) { $errors | Select-Object -First 8 | ForEach-Object { $_.Line }; throw "Offline compilation failed: $assembly" }
        $botsBuiltAssemblies[$assembly] = $output
    }
    Compile-Slice 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
    Compile-Slice 'BlazeAI.Runtime' 'Assets/ThirdParty/Blaze AI/Scripts' $true
    Compile-Slice 'VrBattlegrounds.Weapons.Core' 'Assets/Scripts/Weapons/Core'
    Compile-Slice 'VrBattlegrounds' 'Assets/Scripts' $true
    Compile-Slice 'VrBattlegrounds.BotCombatStand' 'Assets/Scripts/Debug/BotCombatStand'
    Compile-Slice 'VrBattlegrounds.DebugBootstrap' 'Assets/Scripts/Debug/Bootstrap'
    Compile-Slice 'Assembly-CSharp-Editor' 'Assets/Editor' $true

    [xml]$editorProject = Get-Content -Encoding utf8 -LiteralPath (Join-Path $botsMetadataRoot 'Assembly-CSharp-Editor.csproj')
    $refs = @(Get-ProjectRefs $editorProject)
    $refs += $botsBuiltAssemblies.Values | ForEach-Object { '/reference:"' + $_ + '"' }
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
    [pscustomobject]@{assembly='Payloads'; langVersion=$LangVersion; exitCode=$code; errors=$errors.Count; payloads=$payloadSources.Count; log=$log} | ConvertTo-Json -Compress
    if ($code -ne 0) { $errors | Select-Object -First 15 | ForEach-Object { $_.Line }; exit $code }
}
finally { Pop-Location }
