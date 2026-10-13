# Вердикт e2e-прогона по логам процессов (контракт tasks/quest-perf-pipeline/contracts/quest-device-run.json, r4).
#
# Каждый процесс сам собирает Error/Exception/Assert в свой результат (E2ELogCapture → logIssues: type, message —
# первая строка, stack — начало стека, count, firstAtSeconds; logIssuesDropped — не вошедшие в лимит) и объявляет
# известный шум сценария (knownNoise: id, regex, reason — E2EResult.DeclareKnownNoise). Модуль классифицирует записи:
#   known      — regex шума сценария этого процесса или шума каркаса (Tools/e2e/known-noise.json, с учётом scope)
#                совпал с текстом «message + перевод строки + stack»; в записи — id и источник;
#   unexpected — всё остальное.
# Дополнительно ищет нативные падения в сырых логах — их не видит ни один обработчик лога: «Crash!!!» (лог плеера
# Windows) и «Fatal signal» (libc Android в logcat); нативные падения шумом не гасятся.
#
# Вердикт: red — есть unexpected, native или logIssuesDropped > 0 (не вошедшие в лимит ошибки не классифицированы);
# иначе green. Режим enforce — red делает код прогона 1 (если он иначе был бы 0), report — только отчёт.
# Неверный regex, битый или неполный список шума — исключение с понятным текстом (не тихий пропуск); дирижёр
# превращает его в verdict=error.
#
# Совместим с Windows PowerShell 5.1. Файл в UTF-8 с BOM: без него 5.1 читает кириллицу как ANSI.

$script:NativeMarkers = @('Crash!!!', 'Fatal signal')
$script:RegexTimeout = [TimeSpan]::FromSeconds(2)
# logcat -v threadtime: «MM-DD HH:MM:SS.mmm  PID  TID L TAG: текст».
$script:ThreadtimePid = [regex]::new('^\d\d-\d\d\s+\d\d:\d\d:\d\d\.\d+\s+(\d+)\s+\d+\s')
$script:ContextLines = 5
$script:MaxLineLength = 300
$script:ContextLookahead = 400

# Значение свойства объекта из ConvertFrom-Json или словаря; нет свойства — $null.
function Get-E2EProp($Object, [string] $Name) {
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-E2EProp($Object, [string] $Name) {
    if ($null -eq $Object) { return $false }
    if ($Object -is [System.Collections.IDictionary]) { return $Object.Contains($Name) }
    return ($null -ne $Object.PSObject.Properties[$Name])
}

# Массив без $null: ConvertFrom-Json отдаёт $null, скаляр или массив — здесь всегда список.
function ConvertTo-E2EList($Value) {
    $list = New-Object System.Collections.ArrayList
    if ($null -eq $Value) { return ,$list }
    foreach ($item in @($Value)) {
        if ($null -ne $item) { [void]$list.Add($item) }
    }
    return ,$list
}

function Get-E2EInnerMessage($ErrorRecord) {
    $e = $ErrorRecord.Exception
    while ($null -ne $e.InnerException) { $e = $e.InnerException }
    return $e.Message
}

function Limit-E2EText([string] $Text) {
    if ($null -eq $Text) { return '' }
    $Text = $Text.TrimEnd()
    if ($Text.Length -le $script:MaxLineLength) { return $Text }
    return $Text.Substring(0, $script:MaxLineLength) + '…'
}

# Правило шума: проверенный regex. Ошибка — исключение с местом и id, а не пропуск.
function New-E2ENoiseRule([string] $Id, [string] $Pattern, [string] $Reason, [string] $Scope, [string] $Source, [string] $Where) {
    if ([string]::IsNullOrWhiteSpace($Id)) { throw ('известный шум (' + $Where + '): пустой id') }
    if ([string]::IsNullOrEmpty($Pattern)) { throw ('известный шум ''' + $Id + ''' (' + $Where + '): пустой regex') }
    if ([string]::IsNullOrWhiteSpace($Reason)) { throw ('известный шум ''' + $Id + ''' (' + $Where + '): пустой reason — нужно доказательство безвредности') }
    try {
        $rx = [regex]::new($Pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $script:RegexTimeout)
    }
    catch {
        throw ('известный шум ''' + $Id + ''' (' + $Where + '): неверный regex ''' + $Pattern + ''': ' + (Get-E2EInnerMessage $_))
    }
    return [pscustomobject]@{ id = $Id; regex = $rx; pattern = $Pattern; reason = $Reason; scope = $Scope; source = $Source
                              kind = 'noise'; owner = $null; ticket = $null }
}

<#
.SYNOPSIS
Читает шум каркаса: { "version": 1, "entries": [ { "id", "regex", "reason", "scope" } ] }.
scope: any (по умолчанию) | server | client (любой client-*). Ошибка формата или regex — исключение.
#>
function Read-E2EKnownNoise([string] $Path) {
    if ([string]::IsNullOrEmpty($Path) -or -not (Test-Path -LiteralPath $Path)) {
        throw ('список известного шума каркаса не найден: ' + $Path)
    }
    try { $doc = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json }
    catch { throw ('список известного шума каркаса не разобран как JSON (' + $Path + '): ' + (Get-E2EInnerMessage $_)) }
    if ($null -eq $doc -or (Get-E2EProp $doc 'version') -ne 1) {
        throw ('список известного шума каркаса (' + $Path + '): ожидается "version": 1')
    }
    if (-not (Test-E2EProp $doc 'entries')) {
        throw ('список известного шума каркаса (' + $Path + '): нет массива "entries"')
    }

    $rules = New-Object System.Collections.ArrayList
    $ids = @{}
    $index = 0
    foreach ($entry in (ConvertTo-E2EList (Get-E2EProp $doc 'entries'))) {
        $where = (Split-Path -Leaf $Path) + ', entries[' + $index + ']'
        $index++
        $id = [string] (Get-E2EProp $entry 'id')
        $scope = [string] (Get-E2EProp $entry 'scope')
        if ($scope -eq '') { $scope = 'any' }
        if (@('any', 'server', 'client') -notcontains $scope) {
            throw ('известный шум ''' + $id + ''' (' + $where + '): scope ''' + $scope + ''' — допустимо any, server, client')
        }
        if ($id -ne '' -and $ids.ContainsKey($id)) { throw ('известный шум ''' + $id + ''' (' + $where + '): id повторяется') }
        $rule = New-E2ENoiseRule $id ([string] (Get-E2EProp $entry 'regex')) ([string] (Get-E2EProp $entry 'reason')) $scope 'framework' $where
        # kind=defect: известный дефект с владельцем и задачей. Это не шум — прогон он по-прежнему роняет,
        # запись лишь показывает, что ошибку уже разбирают (решение пользователя: ошибки исправлять, не фильтровать).
        $kind = [string] (Get-E2EProp $entry 'kind')
        if ($kind -eq '') { $kind = 'noise' }
        if (@('noise', 'defect') -notcontains $kind) {
            throw ('известный шум ''' + $id + ''' (' + $where + '): kind ''' + $kind + ''' — допустимо noise, defect')
        }
        if ($kind -eq 'defect') {
            $rule.owner = [string] (Get-E2EProp $entry 'owner')
            $rule.ticket = [string] (Get-E2EProp $entry 'ticket')
            if ([string]::IsNullOrWhiteSpace($rule.owner) -or [string]::IsNullOrWhiteSpace($rule.ticket)) {
                throw ('известный дефект ''' + $id + ''' (' + $where + '): нужны owner и ticket')
            }
        }
        $rule.kind = $kind
        $ids[$id] = $true
        [void]$rules.Add($rule)
    }
    return ,$rules
}

function Test-E2EScope([string] $Scope, [string] $Process) {
    if ($Scope -eq 'any') { return $true }
    if ($Scope -eq 'server') { return ($Process -eq 'server') }
    if ($Scope -eq 'client') { return ($Process -like 'client*') }
    return $false
}

<#
.SYNOPSIS
Ищет нативные падения («Crash!!!», «Fatal signal») в сырых логах.
RawLogs: элементы @{ process = <метка>; path = <файл>; pid = <pid или $null> }. С pid учитываются только строки
logcat -v threadtime этого процесса (поток logcat шлема содержит все процессы устройства). Повтор той же строки
в нескольких файлах одного процесса (поток и снимок по pid) считается один раз.
#>
function Find-E2ENativeCrashes([object[]] $RawLogs) {
    $found = New-Object System.Collections.ArrayList
    $scanned = New-Object System.Collections.ArrayList
    $seen = @{}
    foreach ($log in (ConvertTo-E2EList $RawLogs)) {
        $label = [string] (Get-E2EProp $log 'process')
        $path = [string] (Get-E2EProp $log 'path')
        $pidValue = Get-E2EProp $log 'pid'
        $pidFilter = $null
        if ($null -ne $pidValue -and [string] $pidValue -ne '') { $pidFilter = [string] $pidValue }
        $leaf = ''
        if ($path -ne '') { $leaf = Split-Path -Leaf $path }

        if ($path -eq '' -or -not (Test-Path -LiteralPath $path)) {
            [void]$scanned.Add([ordered]@{ process = $label; file = $leaf; pid = $pidFilter; present = $false })
            continue
        }
        [void]$scanned.Add([ordered]@{ process = $label; file = $leaf; pid = $pidFilter; present = $true })

        $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            $marker = $null
            foreach ($m in $script:NativeMarkers) {
                if ($line.IndexOf($m, [StringComparison]::Ordinal) -ge 0) { $marker = $m; break }
            }
            if ($null -eq $marker) { continue }
            if ($null -ne $pidFilter -and -not (Test-E2EPidLine $line $pidFilter)) { continue }

            $key = $label + '|' + $line
            if ($seen.ContainsKey($key)) { continue }
            $seen[$key] = $true

            $context = New-Object System.Collections.ArrayList
            $limit = [Math]::Min($lines.Length, $i + 1 + $script:ContextLookahead)
            for ($j = $i + 1; $j -lt $limit -and $context.Count -lt $script:ContextLines; $j++) {
                if ($null -ne $pidFilter -and -not (Test-E2EPidLine $lines[$j] $pidFilter)) { continue }
                [void]$context.Add((Limit-E2EText $lines[$j]))
            }
            [void]$found.Add([ordered]@{
                process = $label; file = $leaf; line = ($i + 1); marker = $marker
                text = (Limit-E2EText $line); context = $context
            })
        }
    }
    return [pscustomobject]@{ native = $found; scanned = $scanned }
}

# Строка logcat принадлежит процессу. Имя параметра не $Pid: это автоматическая переменная PowerShell.
function Test-E2EPidLine([string] $Line, [string] $ProcessId) {
    $m = $script:ThreadtimePid.Match($Line)
    return ($m.Success -and $m.Groups[1].Value -eq $ProcessId)
}

<#
.SYNOPSIS
Строит вердикт по логам и (с -OutPath) пишет log-verdict.json.
Processes: элементы @{ process = <метка: server, client-1…>; data = <результат ConvertFrom-Json или $null> }.
RawLogs: как у Find-E2ENativeCrashes. KnownNoisePath: по умолчанию known-noise.json рядом с модулем.
Notes: примечания дирижёра (что не проверено) — попадают в notes вердикта.
Ошибка списка шума (каркаса или сценария) — исключение.
#>
function Invoke-E2ELogVerdict {
    [CmdletBinding()]
    param(
        [ValidateSet('enforce', 'report')] [string] $Mode = 'enforce',
        [object[]] $Processes = @(),
        [object[]] $RawLogs = @(),
        [string] $KnownNoisePath = '',
        [string] $OutPath = '',
        [string[]] $Notes = @()
    )

    if ($KnownNoisePath -eq '') { $KnownNoisePath = Join-Path $PSScriptRoot 'known-noise.json' }
    $framework = Read-E2EKnownNoise $KnownNoisePath

    $unexpected = New-Object System.Collections.ArrayList
    $known = New-Object System.Collections.ArrayList
    $defects = New-Object System.Collections.ArrayList
    $perProcess = New-Object System.Collections.ArrayList
    # Не $notes: переменные PowerShell без учёта регистра, это был бы параметр $Notes ([string[]], фиксированный размер).
    $noteList = New-Object System.Collections.ArrayList
    foreach ($note in @($Notes)) { if (-not [string]::IsNullOrEmpty($note)) { [void]$noteList.Add($note) } }
    $dropped = 0

    foreach ($proc in (ConvertTo-E2EList $Processes)) {
        $label = [string] (Get-E2EProp $proc 'process')
        $data = Get-E2EProp $proc 'data'
        $entry = [ordered]@{
            process = $label; captured = $false; issues = 0; occurrences = 0
            unexpected = 0; known = 0; defects = 0; dropped = 0; knownNoise = 0; note = $null
        }
        [void]$perProcess.Add($entry)

        if ($null -eq $data) {
            $entry.note = 'результата процесса нет — его ошибки лога не собраны'
            [void]$noteList.Add($label + ': ' + $entry.note)
            continue
        }
        if (-not (Test-E2EProp $data 'logIssues')) {
            $entry.note = 'в результате нет logIssues — сборка без E2ELogCapture, ошибки лога не собраны'
            [void]$noteList.Add($label + ': ' + $entry.note)
            continue
        }
        $entry.captured = $true

        $scenarioRules = New-Object System.Collections.ArrayList
        $noiseIndex = 0
        foreach ($noise in (ConvertTo-E2EList (Get-E2EProp $data 'knownNoise'))) {
            $where = $label + ' knownNoise[' + $noiseIndex + ']'
            $noiseIndex++
            [void]$scenarioRules.Add((New-E2ENoiseRule ([string] (Get-E2EProp $noise 'id')) ([string] (Get-E2EProp $noise 'regex')) `
                ([string] (Get-E2EProp $noise 'reason')) 'any' 'scenario' $where))
        }
        $entry.knownNoise = $scenarioRules.Count

        foreach ($issue in (ConvertTo-E2EList (Get-E2EProp $data 'logIssues'))) {
            $message = [string] (Get-E2EProp $issue 'message')
            $stack = [string] (Get-E2EProp $issue 'stack')
            $count = 1
            $rawCount = Get-E2EProp $issue 'count'
            if ($null -ne $rawCount) { $count = [int] $rawCount }
            $text = $message + "`n" + $stack

            $hit = $null
            foreach ($rule in $scenarioRules) {
                if ($rule.regex.IsMatch($text)) { $hit = $rule; break }
            }
            if ($null -eq $hit) {
                foreach ($rule in $framework) {
                    if ((Test-E2EScope $rule.scope $label) -and $rule.regex.IsMatch($text)) { $hit = $rule; break }
                }
            }

            $record = [ordered]@{
                process = $label
                type = [string] (Get-E2EProp $issue 'type')
                message = $message
                stack = $stack
                count = $count
                firstAtSeconds = (Get-E2EProp $issue 'firstAtSeconds')
            }
            $entry.issues++
            $entry.occurrences += $count
            if ($null -ne $hit -and $hit.kind -eq 'defect') {
                $record.id = $hit.id
                $record.owner = $hit.owner
                $record.ticket = $hit.ticket
                [void]$defects.Add($record)
                $entry.defects++
            }
            elseif ($null -ne $hit) {
                $record.id = $hit.id
                $record.source = $hit.source
                [void]$known.Add($record)
                $entry.known++
            }
            else {
                [void]$unexpected.Add($record)
                $entry.unexpected++
            }
        }

        $rawDropped = Get-E2EProp $data 'logIssuesDropped'
        if ($null -ne $rawDropped) { $entry.dropped = [int] $rawDropped }
        $dropped += $entry.dropped
    }

    $scan = Find-E2ENativeCrashes $RawLogs
    foreach ($s in $scan.scanned) {
        if (-not $s.present) { [void]$noteList.Add($s.process + ': сырого лога нет — ' + $s.file) }
    }

    $reasons = New-Object System.Collections.ArrayList
    if ($unexpected.Count -gt 0) { [void]$reasons.Add('неожиданных ошибок лога: ' + $unexpected.Count) }
    if ($defects.Count -gt 0) { [void]$reasons.Add('известных дефектов (ещё не исправлены): ' + $defects.Count) }
    if ($scan.native.Count -gt 0) { [void]$reasons.Add('нативных падений: ' + $scan.native.Count) }
    if ($dropped -gt 0) { [void]$reasons.Add('ошибок сверх лимита захвата (не классифицированы): ' + $dropped) }
    $verdict = 'green'
    if ($reasons.Count -gt 0) { $verdict = 'red' }

    $result = [ordered]@{
        contractRule = 'quest-device-run r4: вердикт по логам'
        mode = $Mode
        verdict = $verdict
        error = $null
        reasons = $reasons
        unexpected = $unexpected
        defects = $defects
        known = $known
        native = $scan.native
        dropped = $dropped
        perProcess = $perProcess
        rawLogs = $scan.scanned
        notes = $noteList
        knownNoiseFile = $KnownNoisePath
        knownNoiseFramework = $framework.Count
    }
    if ($OutPath -ne '') { Write-E2ELogVerdictFile $result $OutPath }
    return [pscustomobject]$result
}

<#
.SYNOPSIS
Вердикт, который вынести нельзя (битый список шума, неверный regex): verdict=error с текстом причины.
#>
function New-E2ELogVerdictError([string] $Mode, [string] $Message, [string] $OutPath = '') {
    $result = [ordered]@{
        contractRule = 'quest-device-run r4: вердикт по логам'
        mode = $Mode
        verdict = 'error'
        error = $Message
        reasons = @($Message)
        unexpected = @(); defects = @(); known = @(); native = @(); dropped = 0; perProcess = @(); rawLogs = @(); notes = @()
    }
    if ($OutPath -ne '') {
        try { Write-E2ELogVerdictFile $result $OutPath } catch {}
    }
    return [pscustomobject]$result
}

function Write-E2ELogVerdictFile($Result, [string] $OutPath) {
    $json = $Result | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($OutPath, $json, (New-Object System.Text.UTF8Encoding($false)))
}

<#
.SYNOPSIS
Код прогона с учётом вердикта по логам: только enforce и только поверх 0 — red даёт 1, error (вердикт вынести
нельзя) — 2. Коды 1 и 2 не меняются; report код не меняет.
#>
function Get-E2ELogVerdictExitCode([int] $ExitCode, $Verdict) {
    if ($ExitCode -ne 0 -or $null -eq $Verdict) { return $ExitCode }
    if ($Verdict.mode -ne 'enforce') { return $ExitCode }
    if ($Verdict.verdict -eq 'red') { return 1 }
    if ($Verdict.verdict -eq 'error') { return 2 }
    return $ExitCode
}

<#
.SYNOPSIS
Краткая строка вердикта и до 10 примеров (неожиданные ошибки, затем нативные падения).
#>
function Write-E2ELogVerdictSummary($Verdict, [string] $FileName = 'log-verdict.json') {
    if ($null -eq $Verdict) { return }
    $colour = 'Green'
    if ($Verdict.verdict -eq 'red') { $colour = 'Red' }
    if ($Verdict.verdict -eq 'error') { $colour = 'Yellow' }
    if ($Verdict.mode -eq 'report' -and $Verdict.verdict -ne 'green') { $colour = 'Yellow' }

    $modeText = $Verdict.mode
    if ($Verdict.mode -eq 'report') { $modeText = 'report — код прогона не меняет' }
    if ($Verdict.verdict -eq 'error') {
        Write-Host ('ЛОГИ: ERROR (' + $modeText + ') — вердикт вынести нельзя: ' + $Verdict.error) -ForegroundColor $colour
        return
    }
    Write-Host ('ЛОГИ: ' + ([string] $Verdict.verdict).ToUpperInvariant() + ' (' + $modeText + ') — неожиданных ' + @($Verdict.unexpected).Count +
                ', известных дефектов ' + @($Verdict.defects).Count +
                ', известного шума ' + @($Verdict.known).Count + ', нативных падений ' + @($Verdict.native).Count +
                ', сверх лимита ' + $Verdict.dropped + '; подробно: ' + $FileName) -ForegroundColor $colour

    $shown = 0
    foreach ($u in @($Verdict.unexpected)) {
        if ($null -eq $u -or $shown -ge 10) { continue }
        Write-Host ('  [' + $u.process + '] ' + $u.type + ' x' + $u.count + ': ' + (Limit-E2EText $u.message)) -ForegroundColor $colour
        $shown++
    }
    foreach ($d in @($Verdict.defects)) {
        if ($null -eq $d -or $shown -ge 10) { continue }
        Write-Host ('  [' + $d.process + '] известный дефект ' + $d.id + ' (' + $d.owner + ', ' + $d.ticket + ') x' + $d.count + ': ' +
                    (Limit-E2EText $d.message)) -ForegroundColor $colour
        $shown++
    }
    foreach ($n in @($Verdict.native)) {
        if ($null -eq $n -or $shown -ge 10) { continue }
        Write-Host ('  [' + $n.process + '] нативное падение (' + $n.file + ':' + $n.line + '): ' + $n.text) -ForegroundColor $colour
        $shown++
    }
    $total = @($Verdict.unexpected).Count + @($Verdict.defects).Count + @($Verdict.native).Count
    if ($total -gt $shown) { Write-Host ('  … и ещё ' + ($total - $shown)) -ForegroundColor $colour }
    foreach ($note in @($Verdict.notes)) {
        if ($null -ne $note) { Write-Host ('  примечание: ' + $note) -ForegroundColor DarkYellow }
    }
}

Export-ModuleMember -Function Invoke-E2ELogVerdict, Read-E2EKnownNoise, Find-E2ENativeCrashes, `
    New-E2ELogVerdictError, Get-E2ELogVerdictExitCode, Write-E2ELogVerdictSummary
