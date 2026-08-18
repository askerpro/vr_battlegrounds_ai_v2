# Дирижёр e2e-прогона на двух процессах (ярус C из Docs/testing.md).
#
# Поднимает выделенный сервер (-batchmode -nographics) и нужное число клиентов
# из одного и того же плеера, ждёт от каждого процесса машиночитаемый вердикт,
# сводит их в один отчёт и возвращает код возврата.
#
# Роль процесса выбирает сам билд: GameNetworkDiscovery смотрит на
# Mirror.Utils.IsHeadless(), то есть на наличие графического устройства.
# Поэтому сервер обязан идти с -nographics, а клиенты — без него.
#
# Вердикт выносит сценарий внутри плеера, а не этот скрипт: см.
# Assets/Scripts/Debug/E2E/. Скрипт только запускает, ждёт, добивает и сводит.
#
# Коды возврата:
#   0 — все проверки зелёные
#   1 — проверки прошли, часть красная (находка воспроизводится)
#   2 — прогон не состоялся, вердикт вынести нельзя
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 -Map TestMap2 -Timeout 420

[CmdletBinding()]
param(
    # Имя сценария для -e2eScenario.
    [string] $Scenario = 'dedicated-server-arsenal',

    # Сцена карты. Нужна карта с GameplayManager (MatchManager.prefab)
    # и стеной арсенала: TestMap1 или TestMap2.
    [string] $Map = 'TestMap1',

    # Сколько клиентских процессов поднять. Для dedicated-server-arsenal нужно 2:
    # EliminationMode не выйдет из WaitingForPlayers, пока в каждой из двух команд
    # нет игрока.
    [int]    $Clients = 2,

    # Общий таймаут прогона в секундах. По его истечении процессы добиваются
    # принудительно, а вердикт берётся из того, что успело записаться.
    [int]    $Timeout = 300,

    # Путь к плееру. Пусто — Build\e2e\VrBattlegrounds.exe в корне репозитория.
    [string] $Player = '',

    # Не проверять свежесть билда относительно исходников.
    [switch] $SkipStaleCheck
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrEmpty($Player)) {
    $Player = Join-Path $RepoRoot 'Build\e2e\VrBattlegrounds.exe'
}

function Write-Section([string] $Text) {
    Write-Host ''
    Write-Host ('== ' + $Text) -ForegroundColor Cyan
}

# ── 0. Добить осиротевшие процессы ────────────────────────────────────────
# Висящий плеер держит UDP-порт Discovery и рвёт следующий прогон.
$ProcessName = [System.IO.Path]::GetFileNameWithoutExtension($Player)
$orphans = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
if ($orphans.Count -gt 0) {
    Write-Section ('Добиваю осиротевшие процессы: ' + $orphans.Count)
    foreach ($orphan in $orphans) {
        try { Stop-Process -Id $orphan.Id -Force -ErrorAction Stop } catch {}
    }
    Start-Sleep -Seconds 2
}

# ── 1. Плеер на месте и не устарел ────────────────────────────────────────
if (-not (Test-Path $Player)) {
    Write-Host ''
    Write-Host 'Плеер не собран:' -ForegroundColor Red
    Write-Host ('  ' + $Player)
    Write-Host 'Собери его в открытом редакторе: Tools > VR Battlegrounds > Debug > Собрать e2e-плеер (Windows)'
    Write-Host 'или агентом через MCP: VrBattlegrounds.EditorTools.E2EPlayerBuilder.Run()'
    Write-Host 'При закрытом редакторе годится и батч:'
    Write-Host ('  Unity.exe -batchmode -quit -projectPath "' + $RepoRoot + '" -executeMethod VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch')
    exit 2
}

# Свежесть билда меряем не по .exe: инкрементальная пересборка его не трогает,
# а обновляет только управляемые сборки в <плеер>_Data\Managed.
$playerStamp = (Get-Item $Player).LastWriteTimeUtc
$managedDir = Join-Path ([System.IO.Path]::GetDirectoryName($Player)) ($ProcessName + '_Data\Managed')
if (Test-Path $managedDir) {
    $newestDll = Get-ChildItem -Path $managedDir -Filter *.dll -File |
                 Sort-Object LastWriteTimeUtc -Descending |
                 Select-Object -First 1
    if ($null -ne $newestDll -and $newestDll.LastWriteTimeUtc -gt $playerStamp) {
        $playerStamp = $newestDll.LastWriteTimeUtc
    }
}

if (-not $SkipStaleCheck) {
    $sourceRoots = @(
        (Join-Path $RepoRoot 'Assets\Scripts'),
        (Join-Path $RepoRoot 'Assets\Editor')
    )
    $newest = $null
    foreach ($root in $sourceRoots) {
        if (-not (Test-Path $root)) { continue }
        $candidate = Get-ChildItem -Path $root -Filter *.cs -Recurse -File |
                     Sort-Object LastWriteTimeUtc -Descending |
                     Select-Object -First 1
        if ($null -eq $candidate) { continue }
        if ($null -eq $newest) { $newest = $candidate; continue }
        if ($candidate.LastWriteTimeUtc -gt $newest.LastWriteTimeUtc) { $newest = $candidate }
    }

    if ($null -ne $newest -and $newest.LastWriteTimeUtc -gt $playerStamp) {
        Write-Host ''
        Write-Host 'Билд устарел.' -ForegroundColor Yellow
        Write-Host ('  плеер собран:  ' + $playerStamp.ToString('u'))
        Write-Host ('  свежий скрипт: ' + $newest.LastWriteTimeUtc.ToString('u') + '  ' + $newest.FullName)
        Write-Host 'Пересобери плеер или запусти с -SkipStaleCheck, если правка не влияет на прогон.'
        exit 2
    }
}

# ── 2. Папка прогона ──────────────────────────────────────────────────────
$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$runDir = Join-Path $RepoRoot ('Tools\e2e\results\' + $stamp + '-' + $Scenario)
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

# Сценарию внутри плеера даём таймаут чуть меньше общего: он должен успеть
# записать вердикт сам, до того как дирижёр начнёт добивать процессы.
$scenarioTimeout = [Math]::Max(30, $Timeout - 20)

$serverResult = Join-Path $runDir 'server.json'
$serverLog    = Join-Path $runDir 'server.log'

Write-Section 'Конфигурация прогона'
Write-Host ('  плеер:     ' + $Player)
Write-Host ('  собран:    ' + $playerStamp.ToString('u'))
Write-Host ('  сценарий:  ' + $Scenario)
Write-Host ('  карта:     ' + $Map)
Write-Host ('  клиентов:  ' + $Clients)
Write-Host ('  таймаут:   ' + $Timeout + ' с (сценарию ' + $scenarioTimeout + ' с)')
Write-Host ('  результат: ' + $runDir)

$launched = New-Object System.Collections.ArrayList
$expected = New-Object System.Collections.ArrayList

function Start-Player([string[]] $ExtraArgs, [string] $LogPath) {
    $arguments = @('-logFile', $LogPath) + $ExtraArgs
    return Start-Process -FilePath $Player -ArgumentList $arguments -PassThru
}

$exitCode = 2
try {
    # ── 3. Сервер ─────────────────────────────────────────────────────────
    Write-Section 'Запускаю выделенный сервер'
    $serverArgs = @(
        '-batchmode', '-nographics',
        '-e2eScenario', $Scenario,
        '-e2eRole', 'server',
        '-e2eResult', $serverResult,
        '-e2eMap', $Map,
        '-e2eClients', "$Clients",
        '-e2eTimeout', "$scenarioTimeout"
    )
    $serverProc = Start-Player -ExtraArgs $serverArgs -LogPath $serverLog
    [void]$launched.Add($serverProc)
    [void]$expected.Add(@{ role = 'server'; path = $serverResult; log = $serverLog })
    Write-Host ('  pid ' + $serverProc.Id)

    # Файл-заглушку харнесс пишет ещё до загрузки первой сцены — значит плеер
    # стартовал и аргументы разобраны. Без этого маркера ждать клиентами нечего.
    $ready = $false
    $readyDeadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $readyDeadline) {
        if (Test-Path $serverResult) { $ready = $true; break }
        if ($serverProc.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }

    if (-not $ready) {
        Write-Host '  сервер не отметился файлом результата' -ForegroundColor Red
        if ($serverProc.HasExited) {
            Write-Host ('  процесс уже вышел с кодом ' + $serverProc.ExitCode) -ForegroundColor Red
        }
        Write-Host ('  смотри ' + $serverLog)
    }
    else {
        Write-Host '  харнесс сервера поднялся'
        # Небольшая пауза: StartServer и AdvertiseServer идут уже в первой сцене.
        Start-Sleep -Seconds 6
    }

    # ── 4. Клиенты ────────────────────────────────────────────────────────
    Write-Section ('Запускаю клиентов: ' + $Clients)
    for ($i = 1; $i -le $Clients; $i++) {
        $role = 'client-' + $i
        $clientResult = Join-Path $runDir ($role + '.json')
        $clientLog    = Join-Path $runDir ($role + '.log')

        $clientArgs = @(
            '-screen-width', '640',
            '-screen-height', '480',
            '-screen-fullscreen', '0',
            '-e2eScenario', $Scenario,
            '-e2eRole', $role,
            '-e2eResult', $clientResult,
            '-e2eMap', $Map,
            '-e2eServerAddress', '127.0.0.1',
            '-e2eDeviceToken', ('e2e-' + $role),
            '-e2eTimeout', "$scenarioTimeout"
        )

        $clientProc = Start-Player -ExtraArgs $clientArgs -LogPath $clientLog
        [void]$launched.Add($clientProc)
        [void]$expected.Add(@{ role = $role; path = $clientResult; log = $clientLog })
        Write-Host ('  ' + $role + ': pid ' + $clientProc.Id)

        # Разводим старты: два плеера, начинающие Discovery в один такт,
        # мешают друг другу на общем UDP-порту.
        Start-Sleep -Seconds 3
    }

    # ── 5. Ждём вердикт сервера ───────────────────────────────────────────
    Write-Section 'Жду вердикт сервера'
    $deadline = (Get-Date).AddSeconds($Timeout)
    $verdictReady = $false

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $serverResult) {
            $status = ''
            try {
                $raw = Get-Content -Path $serverResult -Raw -ErrorAction Stop
                $status = (ConvertFrom-Json $raw).status
            }
            catch { $status = '' }

            if ($status -ne '' -and $status -ne 'started') { $verdictReady = $true; break }
        }

        if ($serverProc.HasExited -and (Test-Path $serverResult)) {
            # Процесс вышел — дальше вердикт уже не изменится.
            Start-Sleep -Seconds 1
            $verdictReady = $true
            break
        }

        Start-Sleep -Seconds 2
    }

    if ($verdictReady) {
        Write-Host '  вердикт записан'

        # Клиентские проверки завершаются вместе с серверными или чуть позже —
        # ждём их файлы отдельно, иначе контрольная проверка про ClientRpc
        # не успеет записаться и прогон станет INCONCLUSIVE на пустом месте.
        $clientDeadline = (Get-Date).AddSeconds(45)
        while ((Get-Date) -lt $clientDeadline) {
            $pending = 0
            foreach ($item in $expected) {
                if ($item.role -eq 'server') { continue }
                $status = ''
                if (Test-Path $item.path) {
                    try { $status = (ConvertFrom-Json (Get-Content -Path $item.path -Raw)).status } catch { $status = '' }
                }
                if ($status -eq '' -or $status -eq 'started') { $pending++ }
            }

            if ($pending -eq 0) { break }
            Start-Sleep -Seconds 2
        }
    }
    else {
        Write-Host ('  таймаут ' + $Timeout + ' с — вердикта нет') -ForegroundColor Yellow
    }
}
finally {
    # ── 6. Добить всё, что запускали ──────────────────────────────────────
    Write-Section 'Гашу процессы'
    foreach ($proc in $launched) {
        try {
            if (-not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                Write-Host ('  убит pid ' + $proc.Id)
            }
        }
        catch {}
    }

    # Контрольный проход по имени: плеер мог породить дочерний процесс.
    Start-Sleep -Seconds 1
    $leftovers = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    foreach ($leftover in $leftovers) {
        try { Stop-Process -Id $leftover.Id -Force -ErrorAction Stop } catch {}
    }
}

# ── 7. Свести отчёт ───────────────────────────────────────────────────────
Write-Section 'Отчёт'

$roleReports = New-Object System.Collections.ArrayList
$anyMissing = $false
$anyRedCheck = $false
$anyInconclusive = $false

foreach ($item in $expected) {
    $role = $item.role
    if (-not (Test-Path $item.path)) {
        $anyMissing = $true
        Write-Host ''
        Write-Host ('[' + $role + '] файла результата нет: ' + $item.path) -ForegroundColor Red
        Write-Host ('           лог: ' + $item.log)
        [void]$roleReports.Add([pscustomobject]@{
            role = $role; passed = $false; status = 'missing'; summary = 'файл результата не создан'
        })
        continue
    }

    $data = $null
    try { $data = ConvertFrom-Json (Get-Content -Path $item.path -Raw) } catch {}

    if ($null -eq $data) {
        $anyMissing = $true
        Write-Host ''
        Write-Host ('[' + $role + '] результат не разобрался как JSON: ' + $item.path) -ForegroundColor Red
        [void]$roleReports.Add([pscustomobject]@{
            role = $role; passed = $false; status = 'unparsable'; summary = 'битый JSON'
        })
        continue
    }

    $colour = 'Red'
    if ($data.passed) { $colour = 'Green' }

    Write-Host ''
    Write-Host ('[' + $role + '] passed=' + $data.passed + ' status=' + $data.status +
                ' (' + $data.durationSeconds + ' с)') -ForegroundColor $colour
    Write-Host ('           ' + $data.summary)

    foreach ($check in $data.checks) {
        $mark = 'FAIL'
        $checkColour = 'Red'
        if ($check.passed) { $mark = ' OK '; $checkColour = 'Green' }
        Write-Host ('   [' + $mark + '] ' + $check.name) -ForegroundColor $checkColour
        Write-Host ('           ' + $check.detail) -ForegroundColor DarkGray
    }

    if ($data.status -ne 'completed') { $anyInconclusive = $true }
    if (-not $data.passed -and $data.status -eq 'completed') { $anyRedCheck = $true }

    [void]$roleReports.Add([pscustomobject]@{
        role = $role
        passed = $data.passed
        status = $data.status
        summary = $data.summary
        checks = $data.checks
    })
}

if ($anyMissing -or $anyInconclusive) { $exitCode = 2 }
elseif ($anyRedCheck) { $exitCode = 1 }
else { $exitCode = 0 }

$verdict = 'INCONCLUSIVE — прогон не состоялся, вердикт вынести нельзя'
$verdictColour = 'Yellow'
if ($exitCode -eq 1) { $verdict = 'RED — сценарий поймал отказ'; $verdictColour = 'Red' }
if ($exitCode -eq 0) { $verdict = 'GREEN — все проверки зелёные'; $verdictColour = 'Green' }

$summary = [pscustomobject]@{
    scenario = $Scenario
    map = $Map
    clients = $Clients
    startedLocal = $stamp
    verdict = $verdict
    exitCode = $exitCode
    roles = $roleReports
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $runDir 'summary.json') -Encoding UTF8

Write-Host ''
Write-Host ('ИТОГ: ' + $verdict) -ForegroundColor $verdictColour
Write-Host ('Артефакты прогона: ' + $runDir)
exit $exitCode
