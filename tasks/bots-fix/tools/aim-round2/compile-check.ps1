# Офлайн-компиляция payload-ов раунда II наведения против technical input (без Unity, без записи в repo).
# Исходники берутся read-only из commit через git archive (все *.cs/*.asmdef/*.asmref под Assets), поверх кладутся ВСЕ .cs,
# отличающиеся в worktree от commit, из $OverlayRoots (Bots, Weapons/Equipment — отделение binding, Editor Bots);
# все выходные файлы — в $Work (по умолчанию %TEMP%). Затем 7 сборок проекта и payload-ы b*.cs.
# Обёртка повторяет MCP execute_code (MCPForUnity ExecuteCode.WrapUserCode): те же using, static object-метод.
param(
    [string]$Commit = 'c599ac572c592e4f30e39e5f52526119e66fc00e',
    [string]$Work = (Join-Path $env:TEMP 'bots-aim-round2-compile'),
    [string]$LangVersion = '7.3',
    [string[]]$OverlayRoots = @('Assets/Scripts/Bots', 'Assets/Scripts/Weapons/Equipment', 'Assets/Editor/VR_Battlegrounds/Bots')
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$payloadRoot = $PSScriptRoot
$botsMetadataRoot = 'F:/UnityProjects/Vr_Battlegrounds_ai'
$csc = 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll'
# Длинные пути извлечённого дерева мешают Remove-Item: каждый запуск берёт новый каталог.
if (Test-Path -LiteralPath $Work) { $Work = $Work + '-' + [DateTime]::UtcNow.Ticks }
$src = Join-Path $Work 'src'
$botsOutputRoot = Join-Path $Work 'out'
[IO.Directory]::CreateDirectory($src) | Out-Null
[IO.Directory]::CreateDirectory($botsOutputRoot) | Out-Null
$tar = Join-Path $Work 'src.tar'
& git -C $repo archive --format=tar -o $tar $Commit -- ':(glob)Assets/**/*.cs' ':(glob)Assets/**/*.asmdef' ':(glob)Assets/**/*.asmref'
if ($LASTEXITCODE -ne 0) { throw "git archive failed" }
& tar -xf $tar -C $src
if ($LASTEXITCODE -ne 0) { throw "tar extract failed" }
$Overlay = @()
# Сравнение сырых байтов: blob коммита против файла worktree без фильтров (EOL-различия тоже накладываются).
Push-Location $repo
try {
    foreach ($root in $OverlayRoots) {
        foreach ($relative in @(rg --files --path-separator '/' $root --glob '*.cs')) {
            $blob = & git rev-parse --verify --quiet "${Commit}:$relative"
            $actual = & git hash-object --no-filters -- $relative
            if ($blob -ne $actual) {
                $target = Join-Path $src $relative
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target -Force
                $Overlay += $relative
            }
        }
    }
} finally { Pop-Location }
[pscustomobject]@{commit=$Commit; overlay=$Overlay.Count; files=$Overlay} | ConvertTo-Json -Compress
Push-Location $src
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
