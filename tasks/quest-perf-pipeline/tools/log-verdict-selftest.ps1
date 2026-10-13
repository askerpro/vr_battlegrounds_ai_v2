# Самопроверка Tools/e2e/E2ELogVerdict.psm1 (контракт quest-device-run r4, этап e2e-log-verdict) на фикстурах
# log-verdict-fixtures/: known/unexpected, scope шума каркаса, шум сценария, нативные падения (pid-фильтр logcat,
# повтор в двух файлах), ошибки списка шума, dropped, enforce против report, запись log-verdict.json.
# Устройство, плееры и Unity не нужны.
#
# Запуск: powershell -NoProfile -ExecutionPolicy Bypass -File tasks\quest-perf-pipeline\tools\log-verdict-selftest.ps1
# Код возврата: 0 — все проверки прошли, 1 — есть провал.

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$Fixtures = Join-Path $PSScriptRoot 'log-verdict-fixtures'
Import-Module (Join-Path $RepoRoot 'Tools\e2e\E2ELogVerdict.psm1') -Force

$script:Passed = 0
$script:Failed = 0
function Test-Case([string] $Name, [scriptblock] $Body) {
    try { & $Body; $script:Passed++; Write-Host ('PASS: ' + $Name) }
    catch { $script:Failed++; Write-Host ('FAIL: ' + $Name + ': ' + $_.Exception.Message) -ForegroundColor Red }
}
function Assert-True([bool] $Condition, [string] $Message) { if (-not $Condition) { throw $Message } }
function Assert-Throws([scriptblock] $Body, [string[]] $Expected) {
    $text = $null
    try { & $Body } catch { $text = $_.Exception.Message }
    if ($null -eq $text) { throw 'ожидалось исключение' }
    foreach ($part in $Expected) {
        if ($text.IndexOf($part, [StringComparison]::Ordinal) -lt 0) { throw ('в тексте ошибки нет "' + $part + '": ' + $text) }
    }
}
function Read-Fixture([string] $Name) {
    return ([System.IO.File]::ReadAllText((Join-Path $Fixtures $Name), [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}

$noise = Join-Path $Fixtures 'known-noise.json'
$outDir = Join-Path ([System.IO.Path]::GetTempPath()) ('vrbg-log-verdict-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$processes = @(
    @{ process = 'server'; data = (Read-Fixture 'server.json') },
    @{ process = 'client-1'; data = (Read-Fixture 'client-1.json') },
    @{ process = 'client-2'; data = ([pscustomobject]@{ status = 'completed'; passed = $true; checks = @() }) }
)
$rawLogs = @(
    @{ process = 'server'; path = (Join-Path $Fixtures 'server-player-log.txt'); pid = $null },
    @{ process = 'client-1'; path = (Join-Path $Fixtures 'logcat-stream.txt'); pid = 1111 },
    @{ process = 'client-1'; path = (Join-Path $Fixtures 'client-1.logcat.txt'); pid = 1111 },
    @{ process = 'client-2'; path = (Join-Path $Fixtures 'missing.log'); pid = $null }
)

try {
    $outPath = Join-Path $outDir 'log-verdict.json'
    $red = Invoke-E2ELogVerdict -Mode enforce -Processes $processes -RawLogs $rawLogs -KnownNoisePath $noise -OutPath $outPath

    Test-Case 'неожиданные: ошибка сервера и шум server-only в клиенте (scope)' {
        $u = @($red.unexpected)
        Assert-True ($u.Count -eq 2) ('unexpected=' + $u.Count)
        Assert-True ($u[0].process -eq 'server' -and $u[0].message -eq 'Unexpected server failure') 'нет ошибки сервера'
        Assert-True ($u[1].process -eq 'client-1' -and $u[1].message -eq 'Framework noise 42' -and $u[1].count -eq 2) 'scope server не ограничил клиента'
    }
    Test-Case 'известные: каркас по сообщению, каркас по стеку, шум сценария' {
        $k = @($red.known)
        Assert-True ($k.Count -eq 3) ('known=' + $k.Count)
        Assert-True ($k[0].id -eq 'fw-server-only' -and $k[0].source -eq 'framework' -and $k[0].count -eq 7) 'fw-server-only'
        Assert-True ($k[1].id -eq 'fw-stack' -and $k[1].process -eq 'client-1') 'fw-stack по стеку'
        Assert-True ($k[2].id -eq 'scn-probe' -and $k[2].source -eq 'scenario') 'шум сценария'
        Assert-True ($k[2].message -eq ('Scenario noise: probe ' + [string][char]0x0448 + [char]0x0443 + [char]0x043C)) 'кириллица из \u-экранирования'
    }
    Test-Case 'нативные: Crash!!! сервера, Fatal signal только своего pid и без повтора' {
        $n = @($red.native)
        Assert-True ($n.Count -eq 2) ('native=' + $n.Count)
        Assert-True ($n[0].process -eq 'server' -and $n[0].marker -eq 'Crash!!!' -and $n[0].line -eq 3) 'Crash!!! сервера'
        Assert-True (@($n[0].context).Count -eq 4) ('контекст сервера: ' + @($n[0].context).Count)
        Assert-True ($n[1].process -eq 'client-1' -and $n[1].marker -eq 'Fatal signal' -and $n[1].text -match ' 1111 ') 'Fatal signal своего pid'
        Assert-True (@($n[1].context).Count -eq 2 -and (@($n[1].context) -join "`n") -notmatch 'unrelated') 'контекст logcat по pid'
    }
    Test-Case 'процесс без logIssues и отсутствующий сырой лог — примечания, не тишина' {
        $p = @($red.perProcess) | Where-Object { $_.process -eq 'client-2' }
        Assert-True ($null -ne $p -and -not $p.captured) 'client-2 captured'
        Assert-True (@($red.notes).Count -eq 2) ('notes=' + @($red.notes).Count)
    }
    Test-Case 'вердикт red и log-verdict.json разбирается' {
        Assert-True ($red.verdict -eq 'red' -and $red.mode -eq 'enforce') 'вердикт'
        $file = [System.IO.File]::ReadAllText($outPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        Assert-True ($file.verdict -eq 'red' -and @($file.unexpected).Count -eq 2 -and @($file.native).Count -eq 2 -and @($file.perProcess).Count -eq 3) 'содержимое файла'
        $bytes = [System.IO.File]::ReadAllBytes($outPath)
        Assert-True (-not ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB)) 'файл с BOM'
    }
    Test-Case 'только известный шум — green' {
        $clean = Read-Fixture 'client-1.json'
        $clean.logIssues = @($clean.logIssues | Where-Object { $_.message -ne 'Framework noise 42' })
        $green = Invoke-E2ELogVerdict -Processes @(@{ process = 'client-1'; data = $clean }) -KnownNoisePath $noise
        Assert-True ($green.verdict -eq 'green' -and @($green.known).Count -eq 2 -and @($green.unexpected).Count -eq 0) ('green: ' + $green.verdict)
        Assert-True ((Get-E2ELogVerdictExitCode 0 $green) -eq 0) 'green меняет код'
    }
    Test-Case 'enforce и report: коды выхода' {
        $report = Invoke-E2ELogVerdict -Mode report -Processes $processes -RawLogs $rawLogs -KnownNoisePath $noise
        Assert-True ($report.verdict -eq 'red' -and $report.mode -eq 'report') 'report считает тот же вердикт'
        Assert-True ((Get-E2ELogVerdictExitCode 0 $red) -eq 1) 'enforce red поверх 0 -> 1'
        Assert-True ((Get-E2ELogVerdictExitCode 0 $report) -eq 0) 'report не меняет 0'
        Assert-True ((Get-E2ELogVerdictExitCode 2 $red) -eq 2) 'код 2 не понижается'
        Assert-True ((Get-E2ELogVerdictExitCode 1 $red) -eq 1) 'код 1 остаётся'
        $err = New-E2ELogVerdictError 'enforce' 'сломан список'
        Assert-True ((Get-E2ELogVerdictExitCode 0 $err) -eq 2 -and $err.verdict -eq 'error') 'enforce error -> 2'
        Assert-True ((Get-E2ELogVerdictExitCode 0 (New-E2ELogVerdictError 'report' 'x')) -eq 0) 'report error не меняет код'
        Assert-True ((Get-E2ELogVerdictExitCode 0 $null) -eq 0) 'без вердикта код не меняется'
    }
    Test-Case 'неверный regex каркаса — понятная ошибка с id' {
        Assert-Throws { Invoke-E2ELogVerdict -Processes $processes -KnownNoisePath (Join-Path $Fixtures 'bad-noise.json') } @("'bad-one'", 'неверный regex', 'entries[1]')
    }
    Test-Case 'неверный regex сценария — понятная ошибка с процессом' {
        $broken = Read-Fixture 'client-1.json'
        $broken.knownNoise = @([pscustomobject]@{ id = 'scn-bad'; regex = '(unclosed'; reason = 'fixture' })
        Assert-Throws { Invoke-E2ELogVerdict -Processes @(@{ process = 'client-1'; data = $broken }) -KnownNoisePath $noise } @("'scn-bad'", 'client-1 knownNoise[0]', 'неверный regex')
    }
    Test-Case 'список каркаса: нет файла, нет version, неверный scope, пустой reason' {
        Assert-Throws { Read-E2EKnownNoise (Join-Path $Fixtures 'nope.json') } @('не найден')
        $tmp = Join-Path $outDir 'n.json'
        [System.IO.File]::WriteAllText($tmp, '{ "entries": [] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @('version')
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "a", "regex": "x", "reason": "r", "scope": "quest" } ] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @("scope 'quest'")
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "a", "regex": "x", "reason": "" } ] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @('пустой reason')
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "a", "regex": "x", "reason": "r" }, { "id": "a", "regex": "y", "reason": "r" } ] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @('повторяется')
    }
    Test-Case 'известный дефект: помечен владельцем и задачей, прогон остаётся red' {
        $tmp = Join-Path $outDir 'd.json'
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "def-1", "kind": "defect", "owner": "network", "ticket": "T-1", "regex": "Target component", "reason": "r" } ] }')
        $issue = [pscustomobject]@{ type = 'Error'; message = 'Target component wasn''t found'; stack = ''; count = 2; firstAtSeconds = 1.0 }
        $data = [pscustomobject]@{ status = 'completed'; logIssues = @($issue); logIssuesDropped = 0; knownNoise = @() }
        $v = Invoke-E2ELogVerdict -Processes @(@{ process = 'server'; data = $data }) -KnownNoisePath $tmp
        $d = @($v.defects)
        Assert-True ($v.verdict -eq 'red' -and $d.Count -eq 1 -and $d[0].owner -eq 'network' -and $d[0].ticket -eq 'T-1' -and @($v.unexpected).Count -eq 0) `
            ('дефект: ' + $v.verdict + '/' + $d.Count)
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "def-2", "kind": "defect", "regex": "x", "reason": "r" } ] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @('owner и ticket')
        [System.IO.File]::WriteAllText($tmp, '{ "version": 1, "entries": [ { "id": "def-3", "kind": "bug", "regex": "x", "reason": "r" } ] }')
        Assert-Throws { Read-E2EKnownNoise $tmp } @("kind 'bug'")
    }
    Test-Case 'примечания дирижёра (-Notes) попадают в notes' {
        $v = Invoke-E2ELogVerdict -Processes @(@{ process = 'client-1'; data = $null }) -KnownNoisePath $noise -Notes @('pid не найден')
        Assert-True (@($v.notes).Count -eq 2 -and @($v.notes)[0] -eq 'pid не найден') ('notes: ' + (@($v.notes) -join ' | '))
    }
    Test-Case 'ошибки сверх лимита захвата — red' {
        $over = [pscustomobject]@{ status = 'completed'; logIssues = @(); logIssuesDropped = 3; knownNoise = @() }
        $v = Invoke-E2ELogVerdict -Processes @(@{ process = 'server'; data = $over }) -KnownNoisePath $noise
        Assert-True ($v.verdict -eq 'red' -and $v.dropped -eq 3) ('dropped: ' + $v.verdict + '/' + $v.dropped)
    }
    Test-Case 'список каркаса Tools/e2e/known-noise.json читается' {
        $rules = Read-E2EKnownNoise (Join-Path $RepoRoot 'Tools\e2e\known-noise.json')
        Assert-True ($null -ne $rules) 'правила не прочитаны'
        $default = Invoke-E2ELogVerdict -Processes @(@{ process = 'server'; data = (Read-Fixture 'server.json') })
        Assert-True ($default.knownNoiseFile -like '*Tools\e2e\known-noise.json') ('по умолчанию: ' + $default.knownNoiseFile)
    }
    Test-Case 'краткая строка вердикта печатается' {
        Write-E2ELogVerdictSummary $red
        Write-E2ELogVerdictSummary (New-E2ELogVerdictError 'report' 'проверка печати')
    }
}
finally {
    Remove-Item -LiteralPath $outDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ('passed=' + $script:Passed + ' failed=' + $script:Failed)
if ($script:Failed -gt 0) { exit 1 }
exit 0
