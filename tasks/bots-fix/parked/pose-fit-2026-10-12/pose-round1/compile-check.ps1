# Офлайн-компиляция этапа pose-fit (опорная рука LimbIK) против ТЕКУЩЕГО worktree (без Unity, без записи в repo).
# Новое против aim-round4: сборка RootMotion (Final IK #1, asmdef) и RootMotion.Editor; VrBattlegrounds ссылается на RootMotion;
# Assembly-CSharp (AvatarVrikDriver и прочие скрипты без asmdef) — без файлов RootMotion и со ссылкой на новую сборку.
# RootMotion собирается дважды: со ссылками строго по asmdef (движок + Unity.InputSystem) в Editor-defines и в
# приближении player (без UNITY_EDITOR* и без UnityEditor.dll) — грубая проверка Android; точная — в Unity.
# Метаданные сборок (csproj, Library/ScriptAssemblies, HintPath) — из основного checkout read-only.
param(
    [string]$Work = (Join-Path $env:TEMP 'bots-pose-round1-compile'),
    [string]$LangVersion = '7.3'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$payloadRoot = $PSScriptRoot
$meta = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$csc = 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll'
if (Test-Path -LiteralPath $Work) { $Work = $Work + '-' + [DateTime]::UtcNow.Ticks }
$out = Join-Path $Work 'out'
[IO.Directory]::CreateDirectory($out) | Out-Null
$head = & git -C $repo rev-parse HEAD
[pscustomobject]@{source=$repo; head=$head; work=$Work} | ConvertTo-Json -Compress
Push-Location $repo
$built = @{}
function Load-Project([string]$name) { [xml](Get-Content -Encoding utf8 -LiteralPath (Join-Path $meta "$name.csproj")) }
function Hint-Refs([xml]$project, [bool]$editor = $true) {
    @($project.Project.ItemGroup.Reference | ForEach-Object {
        if ($_.HintPath) {
            $path = [string]$_.HintPath
            if (-not [IO.Path]::IsPathRooted($path)) { $path = Join-Path $meta $path }
            $name = [IO.Path]::GetFileNameWithoutExtension($path)
            if (-not $editor -and ($name -like 'UnityEditor*' -or $path -like '*Managed/UnityEditor*')) { return }
            if ($built.ContainsKey($name)) { $path = $built[$name] }
            if (Test-Path -LiteralPath $path) { '/reference:"' + $path + '"' }
        }
    })
}
function Project-Refs([xml]$project, [string[]]$skip = @()) {
    @($project.SelectNodes('//ProjectReference') | ForEach-Object {
        $name = [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include'))
        if ($skip -contains $name) { return }
        $path = if ($built.ContainsKey($name)) { $built[$name] } else { Join-Path $meta "Library/ScriptAssemblies/$name.dll" }
        '/reference:"' + $path + '"'
    })
}
function Dir-Sources([string]$dir, [string]$assembly, [bool]$excludeNested) {
    $nested = @()
    if ($excludeNested) {
        $nested = @(rg --files $dir --glob '*.asmdef' | Where-Object { [IO.Path]::GetFileName($_) -ne "$assembly.asmdef" } |
            ForEach-Object { (Join-Path $repo ([IO.Path]::GetDirectoryName($_))) + '\' })
        $nested += @(rg --files $dir --glob '*.asmref' | ForEach-Object { (Join-Path $repo ([IO.Path]::GetDirectoryName($_))) + '\' })
    }
    @(rg --files $dir --glob '*.cs' | ForEach-Object {
        $path = Join-Path $repo $_
        if (-not @($nested | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) { '"' + $path + '"' }
    })
}
function Csc([string]$assembly, [string[]]$refs, [string[]]$sources, [string]$defines, [string]$tag = '') {
    $file = if ($tag) { "$assembly.$tag" } else { $assembly }
    $output = Join-Path $out "$file.dll"
    $options = @('/nologo','/target:library','/langversion:9','/unsafe+','/nostdlib+','/warn:0',('/define:' + $defines),('/out:"' + $output + '"'))
    $rsp = Join-Path $out "$file.rsp"
    [IO.File]::WriteAllLines($rsp, @($options + ($refs | Select-Object -Unique) + $sources), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $out "$file.log"
    & dotnet $csc ('@' + $rsp) *> $log
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
    [pscustomobject]@{assembly=$file; exitCode=$code; errors=$errors.Count; sources=$sources.Count} | ConvertTo-Json -Compress
    if ($code -ne 0) { $errors | Select-Object -First 10 | ForEach-Object { $_.Line }; throw "Offline compilation failed: $file" }
    if (-not $tag) { $built[$assembly] = $output }
}
function Slice([string]$assembly, [string]$dir, [bool]$excludeNested = $false, [string[]]$extraRefs = @()) {
    $project = Load-Project $assembly
    $refs = @(Hint-Refs $project) + @(Project-Refs $project) + $extraRefs
    $sources = Dir-Sources $dir $assembly $excludeNested
    if ($assembly -eq 'VrBattlegrounds.DebugBootstrap') {
        $sources += @(rg --files 'Assets/Editor/VR_Battlegrounds/Debug/Bootstrap' --glob '*.cs' | ForEach-Object { '"' + (Join-Path $repo $_) + '"' })
    }
    Csc $assembly $refs $sources ([string]$project.SelectSingleNode('//DefineConstants').InnerText)
}
try {
    $csharp = Load-Project 'Assembly-CSharp'
    $editorDefines = [string]$csharp.SelectSingleNode('//DefineConstants').InnerText
    $playerDefines = (($editorDefines -split ';') | Where-Object { $_ -notlike 'UNITY_EDITOR*' -and $_ -ne 'ENABLE_UNITY_COLLECTIONS_CHECKS' }) -join ';'
    $uxr = Load-Project 'UltimateXR'
    $inputSystem = '/reference:"' + (Join-Path $meta 'Library/ScriptAssemblies/Unity.InputSystem.dll') + '"'
    # RootMotion: строго ссылки asmdef — движок (HintPath UltimateXR.csproj) + Unity.InputSystem.
    $rmSources = Dir-Sources 'Assets/ThirdParty/RootMotion' 'RootMotion' $true
    Csc 'RootMotion' (@(Hint-Refs $uxr) + $inputSystem) $rmSources $editorDefines
    Csc 'RootMotion' (@(Hint-Refs $uxr $false) + $inputSystem) $rmSources $playerDefines 'player'
    $uxrEditor = Load-Project 'UltimateXR.Editor'
    Csc 'RootMotion.Editor' (@(Hint-Refs $uxrEditor) + ('/reference:"' + $built['RootMotion'] + '"')) `
        (Dir-Sources 'Assets/ThirdParty/RootMotion/Editor' 'RootMotion.Editor' $false) $editorDefines
    $rootMotionRef = '/reference:"' + $built['RootMotion'] + '"'
    # Mirror.Components из worktree: в Library основного checkout устаревшая DLL (нет ServerBroadcastListenPort, E2EPlayerPorts.cs).
    Slice 'Mirror.Components' 'Assets/ThirdParty/Mirror/Components' $true
    Slice 'UltimateXR' 'Assets/ThirdParty/UltimateXR/Runtime'
    Slice 'BlazeAI.Runtime' 'Assets/ThirdParty/Blaze AI/Scripts' $true
    Slice 'VrBattlegrounds.Weapons.Core' 'Assets/Scripts/Weapons/Core'
    Slice 'VrBattlegrounds' 'Assets/Scripts' $true @($rootMotionRef)
    Slice 'VrBattlegrounds.BotCombatStand' 'Assets/Scripts/Debug/BotCombatStand'
    Slice 'VrBattlegrounds.DebugBootstrap' 'Assets/Scripts/Debug/Bootstrap'
    # Assembly-CSharp: элементы csproj без RootMotion (они теперь в своей сборке), из файлов worktree.
    $csSources = @($csharp.Project.ItemGroup.Compile | Where-Object { $_.Include } | ForEach-Object { [string]$_.Include } |
        Where-Object { $_ -notlike 'Assets\ThirdParty\RootMotion\*' -and $_ -notlike 'Assets/ThirdParty/RootMotion/*' } |
        ForEach-Object { Join-Path $repo $_ } | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { '"' + $_ + '"' })
    Csc 'Assembly-CSharp' (@(Hint-Refs $csharp) + @(Project-Refs $csharp) + $rootMotionRef) $csSources $editorDefines
    $editor = Load-Project 'Assembly-CSharp-Editor'
    Csc 'Assembly-CSharp-Editor' (@(Hint-Refs $editor) + @(Project-Refs $editor) + $rootMotionRef + ('/reference:"' + $built['Assembly-CSharp'] + '"')) `
        (Dir-Sources 'Assets/Editor' 'Assembly-CSharp-Editor' $true) ([string]$editor.SelectSingleNode('//DefineConstants').InnerText)

    $refs = @(Hint-Refs $editor) + @(Project-Refs $editor) + $rootMotionRef
    $refs += $built.Values | ForEach-Object { '/reference:"' + $_ + '"' }
    $payloads = @()
    foreach ($file in Get-ChildItem -LiteralPath $payloadRoot -Filter 'b*.cs' | Sort-Object Name) {
        $class = 'MCPDynamicCode_' + ($file.BaseName -replace '[^A-Za-z0-9]', '_')
        $body = [IO.File]::ReadAllText($file.FullName)
        $wrapper = "using System;`nusing System.Collections.Generic;`nusing System.Linq;`nusing System.Reflection;`nusing UnityEngine;`nusing UnityEditor;`n" +
            "public static class $class`n{`n    public static object Execute()`n    {`n$body`n    }`n}`n"
        $generated = Join-Path $out ($file.BaseName + '.wrapped.cs')
        [IO.File]::WriteAllText($generated, $wrapper, [Text.UTF8Encoding]::new($false))
        $payloads += '"' + $generated + '"'
    }
    $rsp = Join-Path $out 'Payloads.rsp'
    $options = @('/nologo','/target:library',"/langversion:$LangVersion",'/nostdlib+','/warn:1',('/out:"' + (Join-Path $out 'Payloads.dll') + '"'))
    [IO.File]::WriteAllLines($rsp, @($options + ($refs | Select-Object -Unique) + $payloads), [Text.UTF8Encoding]::new($false))
    $log = Join-Path $out 'Payloads.log'
    & dotnet $csc ('@' + $rsp) *> $log
    $code = $LASTEXITCODE
    $errors = @(Select-String -LiteralPath $log -Pattern ': error ')
    [pscustomobject]@{assembly='Payloads'; langVersion=$LangVersion; exitCode=$code; errors=$errors.Count; payloads=$payloads.Count; log=$log} | ConvertTo-Json -Compress
    if ($code -ne 0) { $errors | Select-Object -First 15 | ForEach-Object { $_.Line }; exit $code }
}
finally { Pop-Location }
