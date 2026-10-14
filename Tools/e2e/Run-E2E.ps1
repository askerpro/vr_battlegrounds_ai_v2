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
# Изоляция прогона. Каждый прогон получает свою пару UDP-портов (KCP и Discovery):
# по умолчанию две свободные, выбранные bind на порт 0, либо явно -NetworkPort/-DiscoveryPort.
# Пара уходит серверу и каждому клиенту аргументами -e2eNetworkPort/-e2eDiscoveryPort,
# поэтому прогон не конфликтует с редактором, worker и плеерами других worktree на 7778/47777,
# а клиент не найдёт через Discovery чужой сервер. Гасит скрипт только процессы, которые
# запустил сам (и их потомков по ParentProcessId), — никогда не по имени процесса:
# сирота чужого прогона сидит на своих портах и этому прогону не мешает.
#
# Вердикт по логам (контракт quest-device-run r4, Tools/e2e/E2ELogVerdict.psm1): ошибки лога каждого процесса
# (logIssues его результата) сверяются с известным шумом каркаса (Tools/e2e/known-noise.json) и сценария
# (knownNoise), в логах плееров ищутся нативные падения. Итог — log-verdict.json и блок logVerdict в summary.json.
# -LogVerdict enforce (по умолчанию): неожиданные ошибки или падения делают зелёный прогон кодом 1;
# -LogVerdict report — только отчёт.
#
# Каркас мультипроцессного сценария (e2e-smoke-kit, Docs/testing.md): всем процессам уходят -e2eClients N
# (роли client-1..client-N) и параметры -Arg ключ=значение как -e2eArg; при -LateClientDelay — -e2eLate client-N
# (последний клиент поздний; серверу роль нужна, чтобы барьеры без includeLate его не ждали). Журналы шагов
# процессов (steps в их результатах) сводятся в общую ленту по UTC — блок steps в summary.json.
#
# Коды возврата:
#   0 — все проверки зелёные, в логах нет неожиданных ошибок (или -LogVerdict report)
#   1 — проверки прошли, часть красная (находка воспроизводится), или при enforce — неожиданные ошибки в логах
#   2 — прогон не состоялся, вердикт вынести нельзя (в том числе битый список известного шума при enforce)
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 -Map TestMap2 -Timeout 420
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 -NetworkPort 27778 -DiscoveryPort 27777
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-E2E.ps1 -Scenario <имя> -Clients 3 -LateClientDelay 60 -Arg map1=TestMap1,map2=TestMap2

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

    # Поздний вход: пауза в секундах перед запуском последнего клиента (0 — без паузы).
    # Нужна сценариям, где клиент приходит на уже идущую карту (map-run-relay-barrier).
    [int]    $LateClientDelay = 0,

    # Пара UDP-портов прогона: KCP (сеть Mirror) и Discovery. 0 и 0 — выбрать
    # автоматически две свободные. Задаются только вместе; один без другого — ошибка.
    [int]    $NetworkPort = 0,
    [int]    $DiscoveryPort = 0,

    # Параметры сценария ключ=значение, каждый уходит всем процессам как -e2eArg (читает ctx.Arg).
    # В режиме -File массив приходит одной строкой: несколько пар — через запятую. Без пробелов и кавычек.
    [string[]] $Arg = @(),

    # Не проверять свежесть билда относительно исходников.
    [switch] $SkipStaleCheck,

    # Вердикт по логам: enforce — неожиданные ошибки в логах делают прогон RED (код 1); report — только отчёт.
    [ValidateSet('enforce', 'report')]
    [string] $LogVerdict = 'enforce'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
try { Import-Module (Join-Path $PSScriptRoot 'E2ELogVerdict.psm1') -Force -ErrorAction Stop }
catch {
    Write-Host ''
    Write-Host ('Модуль вердикта по логам не загружен: ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}
if ([string]::IsNullOrEmpty($Player)) {
    $Player = Join-Path $RepoRoot 'Build\e2e\VrBattlegrounds.exe'
}

function Write-Section([string] $Text) {
    Write-Host ''
    Write-Host ('== ' + $Text) -ForegroundColor Cyan
}

# ── Порты прогона ─────────────────────────────────────────────────────────

# Свободный UDP-порт: bind на порт 0 выдаёт система, сокет сразу освобождается.
function Get-FreeUdpPort {
    $client = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0))
    try { return ([System.Net.IPEndPoint] $client.Client.LocalEndPoint).Port }
    finally { $client.Close() }
}

# Две разные свободные UDP-порта: KCP и Discovery.
function Select-PortPair {
    $network = Get-FreeUdpPort
    $discovery = Get-FreeUdpPort
    while ($discovery -eq $network) { $discovery = Get-FreeUdpPort }
    return @($network, $discovery)
}

# Порт свободен прямо сейчас: эксклюзивный bind и немедленное освобождение (как preflight в плеере).
function Test-UdpPortFree([int] $Port) {
    $socket = $null
    try {
        $socket = [System.Net.Sockets.Socket]::new(
            [System.Net.Sockets.AddressFamily]::InterNetwork,
            [System.Net.Sockets.SocketType]::Dgram,
            [System.Net.Sockets.ProtocolType]::Udp)
        $socket.ExclusiveAddressUse = $true
        $socket.Bind([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, $Port))
        return $true
    }
    catch { return $false }
    finally { if ($null -ne $socket) { $socket.Close() } }
}

# Ошибка явной пары портов или пустая строка.
function Get-PortPairError([int] $Network, [int] $Discovery) {
    if ($Network -lt 1 -or $Network -gt 65535) { return ('-NetworkPort вне 1..65535: ' + $Network) }
    if ($Discovery -lt 1 -or $Discovery -gt 65535) { return ('-DiscoveryPort вне 1..65535: ' + $Discovery) }
    if ($Network -eq $Discovery) { return ('-NetworkPort и -DiscoveryPort совпадают: ' + $Network) }
    return ''
}

# Потомки своих процессов по ParentProcessId (рекурсивно), без самих корней.
# Потомок должен быть создан не раньше $NotBefore: так PID, переиспользованный
# после смерти родителя, не утащит чужой процесс.
function Get-DescendantProcessIds([int[]] $RootIds, [datetime] $NotBefore) {
    $result = New-Object System.Collections.Generic.List[int]
    if ($null -eq $RootIds -or $RootIds.Count -eq 0) { return $result.ToArray() }

    $all = @()
    try { $all = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop) } catch { $all = @() }

    $queue = New-Object System.Collections.Generic.Queue[int]
    foreach ($id in $RootIds) { $queue.Enqueue($id) }
    while ($queue.Count -gt 0) {
        $parent = $queue.Dequeue()
        foreach ($candidate in $all) {
            $candidateId = [int] $candidate.ProcessId
            if ([int] $candidate.ParentProcessId -ne $parent) { continue }
            if ($RootIds -contains $candidateId -or $result.Contains($candidateId)) { continue }
            if ($null -ne $candidate.CreationDate -and $candidate.CreationDate -lt $NotBefore) { continue }
            $result.Add($candidateId)
            $queue.Enqueue($candidateId)
        }
    }
    return $result.ToArray()
}

$autoPorts = ($NetworkPort -eq 0 -and $DiscoveryPort -eq 0)
if (-not $autoPorts) {
    $portError = ''
    if ($NetworkPort -eq 0 -or $DiscoveryPort -eq 0) {
        $portError = '-NetworkPort и -DiscoveryPort задаются только вместе (0 и 0 — автоматически)'
    }
    else {
        $portError = Get-PortPairError $NetworkPort $DiscoveryPort
    }
    if ($portError -ne '') {
        Write-Host ''
        Write-Host ('Порты прогона заданы неверно: ' + $portError) -ForegroundColor Red
        exit 2
    }
}

# -Arg: в режиме -File массив приходит одной строкой через запятую (как в Run-QuestE2E.ps1).
$scenarioArgs = New-Object System.Collections.Generic.List[string]
$argKeys = New-Object System.Collections.Generic.List[string]
foreach ($item in $Arg) {
    foreach ($pair in ([string] $item -split ',')) {
        if ($pair -eq '') { continue }
        # Start-Process склеивает аргументы через пробел без кавычек: пробел или кавычка в значении разорвали бы его.
        if ($pair -notmatch '^([^=\s"]+)=([^\s"]*)$') {
            Write-Host ''
            Write-Host ('-Arg "' + $pair + '": нужен вид ключ=значение, непустой ключ, без пробелов и кавычек') -ForegroundColor Red
            exit 2
        }
        $argKey = $Matches[1]
        if ($argKeys.Contains($argKey)) {
            Write-Host ''
            Write-Host ('-Arg: ключ "' + $argKey + '" задан повторно') -ForegroundColor Red
            exit 2
        }
        $argKeys.Add($argKey)
        $scenarioArgs.Add($pair)
    }
}

# Поздний клиент — последний, если задана пауза и клиентов больше одного (как и прежде).
$lateRole = ''
if ($LateClientDelay -gt 0 -and $Clients -gt 1) { $lateRole = 'client-' + $Clients }

# Общие аргументы каркаса для всех процессов: число клиентов, поздний клиент, параметры сценария.
$commonArgs = @('-e2eClients', "$Clients")
if ($lateRole -ne '') { $commonArgs += @('-e2eLate', $lateRole) }
foreach ($pair in $scenarioArgs) { $commonArgs += @('-e2eArg', $pair) }

$ProcessName = [System.IO.Path]::GetFileNameWithoutExtension($Player)

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

# Пара портов выбирается и перепроверяется непосредственно перед стартом сервера:
# между выбором и bind в плеере порт может занять кто-то ещё (TOCTOU). Авто-пару
# при занятости выбираем заново, не больше трёх попыток; явную пару не подменяем.
$portsReady = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    if ($autoPorts) {
        $pair = Select-PortPair
        $NetworkPort = $pair[0]
        $DiscoveryPort = $pair[1]
    }
    if ((Test-UdpPortFree $NetworkPort) -and (Test-UdpPortFree $DiscoveryPort)) { $portsReady = $true; break }
    Write-Host ('  порты заняты: KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort + ' (попытка ' + $attempt + ')') -ForegroundColor Yellow
    if (-not $autoPorts) { break }
}
if (-not $portsReady) {
    Write-Host ''
    if ($autoPorts) {
        Write-Host 'Не удалось подобрать свободную пару UDP-портов за 3 попытки.' -ForegroundColor Red
    }
    else {
        Write-Host ('Заданные порты заняты: KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort) -ForegroundColor Red
    }
    exit 2
}

Write-Section 'Конфигурация прогона'
Write-Host ('  плеер:     ' + $Player)
Write-Host ('  собран:    ' + $playerStamp.ToString('u'))
Write-Host ('  сценарий:  ' + $Scenario)
Write-Host ('  карта:     ' + $Map)
Write-Host ('  клиентов:  ' + $Clients)
if ($lateRole -ne '') { Write-Host ('  поздний:   ' + $lateRole + ' через ' + $LateClientDelay + ' с') }
if ($scenarioArgs.Count -gt 0) { Write-Host ('  параметры: ' + ($scenarioArgs -join ' ')) }
Write-Host ('  таймаут:   ' + $Timeout + ' с (сценарию ' + $scenarioTimeout + ' с)')
$portMode = 'заданы явно'
if ($autoPorts) { $portMode = 'выбраны автоматически' }
Write-Host ('  порты:     KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort + ' (' + $portMode + ')')
Write-Host ('  результат: ' + $runDir)

$launched = New-Object System.Collections.ArrayList
$expected = New-Object System.Collections.ArrayList

function Start-Player([string[]] $ExtraArgs, [string] $LogPath) {
    $arguments = @('-logFile', $LogPath) + $ExtraArgs
    return Start-Process -FilePath $Player -ArgumentList $arguments -PassThru
}

$exitCode = 2
# Нижняя граница времени создания потомков своих процессов (запас — на точность часов).
$runStartedAt = (Get-Date).AddSeconds(-1)
try {
    # ── 3. Сервер ─────────────────────────────────────────────────────────
    Write-Section 'Запускаю выделенный сервер'
    $serverArgs = @(
        '-batchmode', '-nographics',
        '-e2eScenario', $Scenario,
        '-e2eRole', 'server',
        '-e2eResult', $serverResult,
        '-e2eMap', $Map,
        '-e2eTimeout', "$scenarioTimeout",
        '-e2eNetworkPort', "$NetworkPort",
        '-e2eDiscoveryPort', "$DiscoveryPort"
    ) + $commonArgs
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
        if ($role -eq $lateRole) {
            Write-Host ('  поздний вход: ' + $role + ' через ' + $LateClientDelay + ' с')
            Start-Sleep -Seconds $LateClientDelay
        }
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
            '-e2eTimeout', "$scenarioTimeout",
            '-e2eNetworkPort', "$NetworkPort",
            '-e2eDiscoveryPort', "$DiscoveryPort"
        ) + $commonArgs

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
    # Только свои PID и их потомки (плеер мог породить дочерний процесс).
    # Никакого прохода по имени: он гасил плееры других worktree и worker.
    Write-Section 'Гашу процессы'
    $rootIds = New-Object System.Collections.Generic.List[int]
    foreach ($proc in $launched) { $rootIds.Add([int] $proc.Id) }
    # Дерево снимаем до убийства корней: потом связь с родителем уже не проверить.
    $descendantIds = @(Get-DescendantProcessIds $rootIds.ToArray() $runStartedAt)

    foreach ($proc in $launched) {
        try {
            if (-not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                Write-Host ('  убит pid ' + $proc.Id)
            }
        }
        catch {}
    }

    foreach ($childId in $descendantIds) {
        try {
            Stop-Process -Id $childId -Force -ErrorAction Stop
            Write-Host ('  убит дочерний pid ' + $childId)
        }
        catch {}
    }
}

# ── 7. Свести отчёт ───────────────────────────────────────────────────────
Write-Section 'Отчёт'

$roleReports = New-Object System.Collections.ArrayList
$anyMissing = $false
$anyRedCheck = $false
$anyInconclusive = $false
# Вход вердикта по логам: результаты процессов (без результата — $null) и логи плееров.
$logProcesses = New-Object System.Collections.ArrayList
$logRaw = New-Object System.Collections.ArrayList
# Сводная лента шагов всех процессов (steps их результатов) и сколько записей процессы не сохранили.
$stepFeed = New-Object System.Collections.ArrayList
$stepsDropped = 0

# Время шага как строка ISO UTC: Windows PowerShell оставляет строку, PowerShell 7 превращает её в DateTime.
function ConvertTo-StepUtc($Value) {
    if ($Value -is [datetime]) { return $Value.ToUniversalTime().ToString('o', [System.Globalization.CultureInfo]::InvariantCulture) }
    return [string] $Value
}

foreach ($item in $expected) {
    $role = $item.role
    [void]$logRaw.Add(@{ process = $role; path = $item.log; pid = $null })
    if (-not (Test-Path $item.path)) {
        [void]$logProcesses.Add(@{ process = $role; data = $null })
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
    [void]$logProcesses.Add(@{ process = $role; data = $data })

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

    # Журнал шагов процесса (необязательное поле: результаты старых сборок его не содержат).
    if ($null -ne $data.PSObject.Properties['steps']) {
        foreach ($step in @($data.steps)) {
            if ($null -eq $step) { continue }
            [void]$stepFeed.Add([pscustomobject][ordered]@{
                utc = ConvertTo-StepUtc $step.utc
                process = $role
                role = $step.role
                name = $step.name
                data = $step.data
                outcome = $step.outcome
            })
        }
    }
    if ($null -ne $data.PSObject.Properties['stepsDropped']) { $stepsDropped += [int] $data.stepsDropped }

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

# Лента по UTC: строки ISO 8601 «o» в UTC одной длины, поэтому порядок строк = порядок времени.
$sortedSteps = @($stepFeed | Sort-Object -Property @{ Expression = { $_.utc } }, @{ Expression = { $_.process } })
if ($sortedSteps.Count -gt 0) {
    Write-Section ('Лента шагов: ' + $sortedSteps.Count + ' (полностью — summary.json, блок steps)')
    foreach ($step in ($sortedSteps | Select-Object -Last 30)) {
        $stepColour = 'DarkGray'
        if ($step.outcome -eq 'barrier-failed' -or $step.outcome -eq 'failed' -or $step.outcome -eq 'aborted') { $stepColour = 'Yellow' }
        Write-Host ('  ' + $step.utc + '  ' + $step.process + '  ' + $step.outcome + '  ' + $step.role + ': ' + $step.name + '  ' + $step.data) -ForegroundColor $stepColour
    }
}

if ($anyMissing -or $anyInconclusive) { $exitCode = 2 }
elseif ($anyRedCheck) { $exitCode = 1 }
else { $exitCode = 0 }

$verdict = 'INCONCLUSIVE — прогон не состоялся, вердикт вынести нельзя'
$verdictColour = 'Yellow'
if ($exitCode -eq 1) { $verdict = 'RED — сценарий поймал отказ'; $verdictColour = 'Red' }
if ($exitCode -eq 0) { $verdict = 'GREEN — все проверки зелёные'; $verdictColour = 'Green' }

# ── 8. Вердикт по логам (r4) ──────────────────────────────────────────────
Write-Section 'Вердикт по логам'
$logVerdictPath = Join-Path $runDir 'log-verdict.json'
try {
    $logResult = Invoke-E2ELogVerdict -Mode $LogVerdict -Processes $logProcesses.ToArray() -RawLogs $logRaw.ToArray() -OutPath $logVerdictPath
}
catch {
    $logResult = New-E2ELogVerdictError $LogVerdict $_.Exception.Message $logVerdictPath
}
Write-E2ELogVerdictSummary $logResult
$exitBeforeLogs = $exitCode
$exitCode = Get-E2ELogVerdictExitCode $exitCode $logResult
if ($exitCode -ne $exitBeforeLogs) {
    if ($exitCode -eq 1) { $verdict = 'RED — проверки зелёные, но в логах неожиданные ошибки или нативные падения'; $verdictColour = 'Red' }
    if ($exitCode -eq 2) { $verdict = 'INCONCLUSIVE — вердикт по логам вынести нельзя: ' + $logResult.error; $verdictColour = 'Yellow' }
}

$summary = [pscustomobject]@{
    scenario = $Scenario
    map = $Map
    clients = $Clients
    networkPort = $NetworkPort
    discoveryPort = $DiscoveryPort
    startedLocal = $stamp
    verdict = $verdict
    exitCode = $exitCode
    roles = $roleReports
    lateRole = $lateRole
    scenarioArgs = @($scenarioArgs)
    steps = $sortedSteps
    stepsDropped = $stepsDropped
    logVerdict = [ordered]@{
        mode = $logResult.mode
        verdict = $logResult.verdict
        unexpected = @($logResult.unexpected).Count
        known = @($logResult.known).Count
        defects = @($logResult.defects).Count
        native = @($logResult.native).Count
        dropped = $logResult.dropped
        error = $logResult.error
        file = 'log-verdict.json'
    }
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $runDir 'summary.json') -Encoding UTF8

Write-Host ''
Write-Host ('ИТОГ: ' + $verdict) -ForegroundColor $verdictColour
Write-Host ('Артефакты прогона: ' + $runDir)
exit $exitCode
