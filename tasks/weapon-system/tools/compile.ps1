# Офлайн-компиляция оружия без Unity: Core -> UltimateXR -> VrBattlegrounds -> DebugBootstrap -> Editor -> Tests.
# csproj и Library основного checkout — только чтение; исходники — из своего worktree (корень — от этого скрипта).
# Выход — tasks/weapon-system/reports/compile/ (вне Git). Запуск: powershell -File tasks/weapon-system/tools/compile.ps1
param([string]$SourceOverlay = '')
$ErrorActionPreference = 'Stop'
$wt = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$main = 'F:\UnityProjects\Vr_Battlegrounds_ai'
$out = Join-Path $wt 'tasks\weapon-system\reports\compile'
if ($SourceOverlay) {
    $SourceOverlay = (Resolve-Path -LiteralPath $SourceOverlay).Path
    $reportRoot = (Join-Path $wt 'tasks\weapon-system\reports') + '\'
    if (-not $SourceOverlay.StartsWith($reportRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'SourceOverlay must stay within this task reports directory' }
    $out = Join-Path $wt 'tasks\weapon-system\reports\compile-development'
}
[IO.Directory]::CreateDirectory($out) | Out-Null
$csc = 'C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll'
$ns = 'C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Data\NetStandard\ref\2.1.0\netstandard.dll'
$core = Join-Path $out 'VrBattlegrounds.Weapons.Core.dll'
$coreSrc = Get-ChildItem (Join-Path $wt 'Assets\Scripts\Weapons\Core') -Filter *.cs | ForEach-Object { '"' + $_.FullName + '"' }
$rsp = Join-Path $out 'core.rsp'
[IO.File]::WriteAllLines($rsp, @('/nologo','/target:library','/langversion:9','/nostdlib+','/warnaserror+',('/reference:"'+$ns+'"'),('/out:"'+$core+'"')) + $coreSrc, (New-Object Text.UTF8Encoding($false)))
Write-Output "== Core"
& dotnet $csc ('@' + $rsp) | Where-Object { $_ -match 'error' } | Select-Object -First 30
if ($LASTEXITCODE -ne 0) { Write-Output "FAILED Core"; exit 1 }
$ours = @('UltimateXR','VrBattlegrounds','VrBattlegrounds.DebugBootstrap','Assembly-CSharp-Editor','VrBattlegrounds.Tests.EditMode')
$untracked = @(git -C $wt ls-files --others --exclude-standard -- Assets | Where-Object { $_ -like '*.cs' })
$untracked += @(git -C $wt ls-files -- Assets | Where-Object { $_ -like '*.cs' })
$overlaySources = @()
if ($SourceOverlay) {
    $overlaySources = @(Get-ChildItem -LiteralPath $SourceOverlay -Recurse -Filter *.cs | ForEach-Object { $_.FullName.Substring($SourceOverlay.Length + 1).Replace('\','/') })
}
$code = 0
foreach ($asm in $ours) {
    [xml]$p = Get-Content -LiteralPath (Join-Path $main ($asm + '.csproj'))
    $refs = @($p.Project.ItemGroup.Reference | Where-Object { $_.HintPath } | ForEach-Object {
        $h = [string]$_.HintPath
        if (-not [IO.Path]::IsPathRooted($h)) { $h = Join-Path $main $h }
        $n = [IO.Path]::GetFileNameWithoutExtension($h)
        if ($n -in $ours) { $h = Join-Path $out ($n + '.dll') }
        if (Test-Path -LiteralPath $h) { '/reference:"' + $h + '"' }
    })
    $refs += @($p.SelectNodes('//ProjectReference') | ForEach-Object {
        $n = [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include'))
        if ($n -in $ours) { '/reference:"' + (Join-Path $out ($n + '.dll')) + '"' }
        elseif ($n -ne 'VrBattlegrounds.Weapons.Core') { '/reference:"' + (Join-Path $main ('Library/ScriptAssemblies/' + $n + '.dll')) + '"' }
    })
    if ($asm -ne 'UltimateXR') { $refs += '/reference:"' + $core + '"' }
    $rel = @($p.SelectNodes('//Compile') | ForEach-Object { $_.GetAttribute('Include').Replace('\','/') })
    $dirs = @($rel | ForEach-Object { [IO.Path]::GetDirectoryName($_).Replace('\','/') } | Select-Object -Unique)
    $rel += @($untracked | Where-Object { [IO.Path]::GetDirectoryName($_).Replace('\','/') -in $dirs })
    if ($asm -eq 'UltimateXR') { $rel += @($overlaySources | Where-Object { $_ -like 'Assets/ThirdParty/UltimateXR/Runtime/*.cs' }) }
    if ($asm -eq 'VrBattlegrounds') { $rel += @($overlaySources | Where-Object { $_ -like 'Assets/Scripts/Weapons/Equipment/*.cs' -or $_ -like 'Assets/Scripts/Bots/*.cs' -or $_ -eq 'Assets/Scripts/Player/PlayerSession.cs' }) }
    if ($asm -eq 'VrBattlegrounds') { $rel += @($untracked + @(git -C $wt ls-files -- 'Assets/Scripts/Weapons/WeaponSystem') | Where-Object { $_ -like 'Assets/Scripts/Weapons/WeaponSystem/*.cs' }) }
    # Совместный пакет: foundation bots-fix (Weapons/Equipment) лежит в рабочей копии; старый csproj папку не знает.
    if ($asm -eq 'VrBattlegrounds') { $rel += @($untracked | Where-Object { $_ -like 'Assets/Scripts/Weapons/Equipment/*.cs' }) }
    # asmref относит Editor provider к DebugBootstrap; старый csproj может его ещё не содержать.
    if ($asm -eq 'VrBattlegrounds.DebugBootstrap') { $rel += @($untracked | Where-Object { $_ -like 'Assets/Editor/VR_Battlegrounds/Debug/Bootstrap/*.cs' }) }
    if ($asm -eq 'Assembly-CSharp-Editor') { $rel = @($rel | Where-Object { $_ -notlike 'Assets/Editor/VR_Battlegrounds/Debug/Bootstrap/*.cs' }) }
    if ($asm -eq 'Assembly-CSharp-Editor') { $rel += @($untracked | Where-Object { $_ -like 'Assets/Editor/VR_Battlegrounds/Audio/*.cs' }) }
    if ($asm -eq 'VrBattlegrounds.Tests.EditMode') { $rel += @(git -C $wt ls-files -- 'Assets/Tests/EditMode/Weapons' | Where-Object { $_ -like '*.cs' }) }
    $src = @($rel | Select-Object -Unique | Where-Object { $_ -notlike 'Assets/Scripts/Weapons/Core/*' } |
        ForEach-Object {
            $source = Join-Path $wt $_
            if ($SourceOverlay -and (Test-Path -LiteralPath (Join-Path $SourceOverlay $_))) { $source = Join-Path $SourceOverlay $_ }
            if (Test-Path -LiteralPath $source) { '"' + $source + '"' }
        })
    $defs = [string]$p.SelectSingleNode('//DefineConstants').InnerText
    $opt = @('/nologo','/target:library','/langversion:9','/unsafe+','/nostdlib+','/warn:4','/nowarn:0618,0414,0649,0169,0067,0219,0108,0114',
        ('/define:' + $defs), ('/out:"' + (Join-Path $out ($asm + '.dll')) + '"'))
    $rsp = Join-Path $out ($asm + '.rsp')
    [IO.File]::WriteAllLines($rsp, @($opt + $refs + $src), (New-Object Text.UTF8Encoding($false)))
    Write-Output "== $asm : $($src.Count) files"
    $log = Join-Path $out ($asm + '.log')
    & dotnet $csc ('@' + $rsp) *> $log
    $assemblyExit = $LASTEXITCODE
    Get-Content -LiteralPath $log | Where-Object { $_ -match 'error|WeaponSystem.*warning' } | Select-Object -First 30
    if ($assemblyExit -ne 0) { Write-Output "FAILED $asm exit $assemblyExit"; $code = 1; break }
    Write-Output "OK $asm"
}
exit $code
