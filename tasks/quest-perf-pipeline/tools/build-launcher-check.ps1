# Изолированная проверка launcher: реальные lock-файлы, spy внешних команд и JSON сборщика.
[CmdletBinding()]
param(
    [string] $ReportDir = '',
    [string] $Label = 'check'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $ReportDir) { $ReportDir = Join-Path $repo 'tasks\quest-perf-pipeline\reports\2026-10-14-status\build-fixes' }
$null = New-Item -ItemType Directory -Force -Path $ReportDir
$fixture = Join-Path $ReportDir ($Label + '-fixture')
$null = New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'Tools\release'), (Join-Path $fixture 'Tools\agents')
Copy-Item -LiteralPath (Join-Path $repo 'Tools\release\Build-Game.ps1') -Destination (Join-Path $fixture 'Tools\release\Build-Game.ps1')
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $fixture 'Tools\agents\configure_lfs_worktree.py'), '# fixture', $utf8)
[IO.File]::WriteAllText((Join-Path $fixture 'Unity.exe'), 'fixture; never execute', $utf8)
foreach ($platform in @('android', 'win64')) {
    $null = New-Item -ItemType Directory -Force -Path (Join-Path $fixture ($platform + '\Temp'))
    [IO.File]::WriteAllText((Join-Path $fixture ($platform + '\Temp\UnityLockfile')), '', $utf8)
}

# Подменены только внешние процессы. Логика launcher и файловая блокировка настоящие.
$wrapper = @'
param([string] $Fixture, [string] $Spy, [string] $Mode, [switch] $WithRef)
$ErrorActionPreference = 'Stop'
function global:git {
    [IO.File]::AppendAllText($Spy, ('git ' + ($args -join ' ') + [Environment]::NewLine))
    $global:LASTEXITCODE = 0
    if ($args -contains 'ls-files') {
        if ($Mode -eq 'lfs-failure') { $global:LASTEXITCODE = 71 }
        return
    }
    if ($args -contains 'rev-parse') { return '1111111111111111111111111111111111111111' }
    if ($args -contains 'config') { return 'build' }
}
function global:python {
    [IO.File]::AppendAllText($Spy, ('python ' + ($args -join ' ') + [Environment]::NewLine))
    $global:LASTEXITCODE = 0
}
$parameters = @{
    Targets = 'quest,server'; Unity = (Join-Path $Fixture 'Unity.exe')
    AndroidWorktree = (Join-Path $Fixture 'android'); Win64Worktree = (Join-Path $Fixture 'win64')
}
if ($WithRef) { $parameters.Ref = 'fixture-ref' } else { $parameters.DryRun = $true }
& (Join-Path $Fixture 'Tools\release\Build-Game.ps1') @parameters
exit $LASTEXITCODE
'@
$wrapperPath = Join-Path $fixture 'invoke.ps1'
[IO.File]::WriteAllText($wrapperPath, $wrapper.Replace("`r`n", "`n"), $utf8)
$results = New-Object System.Collections.Generic.List[object]

function Invoke-BuildCase([string] $Name, [string] $LockedPlatform, [string] $Mode, [bool] $WithRef, [int] $ExpectedCode) {
    $spy = Join-Path $ReportDir ($Label + '-' + $Name + '-spy.txt')
    $log = Join-Path $ReportDir ($Label + '-' + $Name + '.txt')
    [IO.File]::WriteAllText($spy, '', $utf8)
    $held = $null
    try {
        if ($LockedPlatform) {
            $held = [IO.File]::Open((Join-Path $fixture ($LockedPlatform + '\Temp\UnityLockfile')), 'Open', 'ReadWrite', 'None')
        }
        $argv = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapperPath, '-Fixture', $fixture, '-Spy', $spy, '-Mode', $Mode)
        if ($WithRef) { $argv += '-WithRef' }
        $output = @(& powershell.exe @argv 2>&1)
        $code = $LASTEXITCODE
    } finally {
        if ($null -ne $held) { $held.Dispose() }
    }
    [IO.File]::WriteAllText($log, ($output -join "`n"), $utf8)
    $commands = @([IO.File]::ReadAllLines($spy))
    $mutations = @($commands | Where-Object { $_ -match 'checkout|hydrate' })
    $passed = ($code -eq $ExpectedCode -and $mutations.Count -eq 0)
    $results.Add([pscustomobject]@{ name = $Name; passed = $passed; exitCode = $code; expectedCode = $ExpectedCode; mutations = $mutations; log = $log; spy = $spy })
}

Invoke-BuildCase 'locked-second-ref' 'win64' 'normal' $true 2
Invoke-BuildCase 'locked-first-ref' 'android' 'normal' $true 2
Invoke-BuildCase 'locked-no-ref' 'win64' 'normal' $false 2
Invoke-BuildCase 'lfs-failure' '' 'lfs-failure' $false 2
Invoke-BuildCase 'empty-lfs-dryrun' '' 'normal' $false 0

# Выполняется существующее AST-присваивание pairObj с JSON той формы, которую пишет PairStatus.
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'Tools\e2e\Run-QuestE2E.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Run-QuestE2E parser error' }
$assignment = $ast.Find({ param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$pairObj' }, $true)
if ($null -eq $assignment) { throw 'pairObj assignment not found' }
$cases = @(
    @{ name = 'builder-paths'; server = '{"revision":"abc","serverPassed":true,"questPassed":false,"serverPath":"C:/emitted/server.exe","apkPath":"","error":""}'; quest = '{"revision":"abc","serverPassed":false,"questPassed":true,"serverPath":"","apkPath":"C:/emitted/quest.apk","error":""}'; wantServer = 'C:/emitted/server.exe'; wantQuest = 'C:/emitted/quest.apk' },
    @{ name = 'blank-path-fallback'; server = '{"serverPassed":true,"serverPath":"","error":""}'; quest = '{"questPassed":true,"apkPath":"   ","error":""}'; wantServer = 'C:/fallback/server.exe'; wantQuest = 'C:/fallback/quest.apk' },
    @{ name = 'missing-path-fallback'; server = '{"serverPassed":true}'; quest = '{"questPassed":true}'; wantServer = 'C:/fallback/server.exe'; wantQuest = 'C:/fallback/quest.apk' },
    @{ name = 'null-status-fallback'; server = 'null'; quest = 'null'; wantServer = 'C:/fallback/server.exe'; wantQuest = 'C:/fallback/quest.apk' }
)
foreach ($case in $cases) {
    $serverData = $case.server | ConvertFrom-Json
    $questData = $case.quest | ConvertFrom-Json
    $serverRun = [pscustomobject]@{ ExitCode = 0 }; $questRun = [pscustomobject]@{ ExitCode = 0 }
    $ServerPlayer = 'C:/fallback/server.exe'; $BuiltApk = 'C:/fallback/quest.apk'; $SharedBuildRevision = 'abc'
    $combinedErrors = New-Object System.Collections.Generic.List[string]
    . ([scriptblock]::Create($assignment.Extent.Text))
    $pairJson = $pairObj | ConvertTo-Json -Depth 4
    $pairPath = Join-Path $ReportDir ($Label + '-' + $case.name + '-pair.json')
    [IO.File]::WriteAllText($pairPath, $pairJson, $utf8)
    $pair = $pairJson | ConvertFrom-Json
    $passed = ($pair.serverOutput -eq $case.wantServer -and $pair.questOutput -eq $case.wantQuest -and $pair.revision -eq 'abc')
    $results.Add([pscustomobject]@{ name = $case.name; passed = $passed; serverOutput = $pair.serverOutput; questOutput = $pair.questOutput; pairJson = $pairPath })
}

# Читаем только реальную функцию из AST: верхний уровень launcher не запускается.
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $repo 'Tools\release\Build-Game.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Build-Game parser error' }
$readers = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Read-UxrIdHash' }, $true))
if ($readers.Count -ne 1) { throw 'Read-UxrIdHash function not found or ambiguous' }
. ([scriptblock]::Create($readers[0].Extent.Text))
$manifestCases = @(
    # Формат настоящего UxrIdFingerprint.WriteTo, Quest Test от 2026-10-15.
    @{ name = 'uxr-producer-manifest'; content = "748f8ac1c2e18298`nprofile: Quest`nids: 9681`nfiles: 359`nbuilt: 2026-10-15 13:34:10`n"; want = '748f8ac1c2e18298' },
    @{ name = 'uxr-trimmed-first-line'; content = "  748F8AC1C2E18298  `nprofile: Quest`n"; want = '748F8AC1C2E18298' },
    @{ name = 'uxr-missing'; content = $null; want = $null },
    @{ name = 'uxr-empty'; content = ''; want = $null },
    @{ name = 'uxr-nonhex'; content = "748f8ac1c2e1829z`n"; want = $null },
    @{ name = 'uxr-wrong-length'; content = "748f8ac1c2e1829`n"; want = $null },
    @{ name = 'uxr-legacy-header'; content = "# sha256: 748f8ac1c2e18298`n"; want = $null },
    @{ name = 'uxr-metadata-first'; content = "profile: Quest`n748f8ac1c2e18298`n"; want = $null },
    @{ name = 'uxr-blank-first-line'; content = "`n748f8ac1c2e18298`n"; want = $null }
)
foreach ($case in $manifestCases) {
    $buildDir = Join-Path $fixture $case.name
    $null = New-Item -ItemType Directory -Path $buildDir -Force
    $manifest = Join-Path $buildDir 'uxr-ids.txt'
    if ($null -ne $case.content) { [IO.File]::WriteAllText($manifest, $case.content, $utf8) }
    elseif (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest }
    $actual = Read-UxrIdHash $buildDir
    $results.Add([pscustomobject]@{ name = $case.name; passed = ($actual -ceq $case.want); actual = $actual; expected = $case.want; manifest = $manifest })
}

$summary = [pscustomobject]@{ label = $Label; passed = @($results | Where-Object passed).Count; failed = @($results | Where-Object { -not $_.passed }).Count; results = $results.ToArray() }
[IO.File]::WriteAllText((Join-Path $ReportDir ($Label + '-summary.json')), ($summary | ConvertTo-Json -Depth 8), $utf8)
$results | ForEach-Object { Write-Host ('{0}: {1}' -f $_.name, $(if ($_.passed) { 'PASS' } else { 'FAIL' })) }
Write-Host ('passed={0}, failed={1}' -f $summary.passed, $summary.failed)
if ($summary.failed) { exit 1 }
