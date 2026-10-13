# Дирижёр e2e-прогона на шлеме Quest (контракт tasks/quest-perf-pipeline/contracts/quest-device-run.json, rev3).
#
# Топология client (по умолчанию, prod flow): на ПК поднимается выделенный сервер — Windows-плеер
# (-batchmode -nographics, роль server), шлем запускается настоящим клиентом и находит сервер через
# Discovery по LAN на изолированной паре портов прогона; -e2eServerAddress <IP ПК> — запасной путь.
# Вердикт выносят сценарии внутри плееров (JSON E2EResult), дирижёр только запускает, ждёт, гасит,
# собирает и сводит. Топология host на шлеме требует шва выбора роли в плеере (зона network) и
# в этой ревизии не реализована: -Topology host отвечает кодом 2.
#
# Дирижёр — единственный, кто работает с устройством через adb (выбор устройства, установка APK,
# пробуждение и датчик приближения, am start, ожидание, force-stop, logcat, pull). Настройки шлема
# меняются только на время прогона: KEYCODE_WAKEUP и prox_close перед стартом, automation_disable
# в finally. Сам скрипт ничего не меняет в брандмауэре Windows: при отсутствии правила он отказывает
# и печатает команду, которую выполняет человек с правами администратора.
#
# Гасит дирижёр только то, что запустил сам: свой серверный PID и его потомков (как Run-E2E.ps1),
# на шлеме — am force-stop пакета, который до прогона не был запущен (иначе отказ).
#
# Ревизии сборок: e2e-плеер и тестовый APK несут штамп ревизии исходников в версии
# (<база>+<ревизия>, BuildRevisionStamp). Ревизия APK читается из versionName установленного пакета,
# ревизия сервера — из appVersion его результата; разные ревизии или ревизия, которой нет, — код 2.
# -Build собирает пару одной ревизией batch-режимом Unity (редактор этого checkout должен быть закрыт;
# при открытом — StartPairBuild в редакторе, затем прогон с -Apk). До запуска Unity -Build проверяет, что
# LFS-ассеты checkout — полные файлы, а не указатели (git lfs ls-files + сигнатура указателя в первых 200 байтах).
#
# Точки обзора (-ViewsFile): локальный JSON { "views": [ { "name", "x", "y"?, "z", "yaw" } ] } проверяется до
# прогона, копируется в папку прогона, на шлем (files/e2e/<прогон>/views.json) и в persistentDataPath сервера
# (e2e/<прогон>/views.json); обе стороны получают -e2eArg views=e2e/<прогон>/views.json (путь от persistentDataPath).
#
# Поза головы (-HeadPose, контракт r5): по умолчанию шлем-клиент получает -e2eHeadPose eye=1.6 — высоту глаз и горизонт
# камеры задаёт E2EHeadPose, человек шлем не держит. calib — цель из калибровки, off — реальный трекинг (замеры
# с позой шлема). Серверу флаг не передаётся. До устройства манифест APK (-Apk или собранный -Build) разбирается
# aapt: включённые LateLatching/SpaceWarp при симуляции — код 2; проверить нельзя — предупреждение, renderCheck=unknown.
#
# Вердикт по логам (контракт r4, Tools/e2e/E2ELogVerdict.psm1): ошибки лога обеих сторон (logIssues их результатов)
# сверяются с известным шумом каркаса (Tools/e2e/known-noise.json) и сценария (knownNoise); нативные падения ищутся
# в server.log и в logcat шлема только по pid приложения (поток logcat содержит все процессы устройства). Итог —
# log-verdict.json и блок logVerdict в run.json и summary.json. -LogVerdict enforce (по умолчанию): неожиданные
# ошибки или падения делают зелёный прогон кодом 1; -LogVerdict report — только отчёт.
#
# Коды возврата:
#   0 — все проверки обеих сторон зелёные, в логах нет неожиданных ошибок (или -LogVerdict report)
#   1 — сценарий поймал отказ (completed, но есть красные проверки) или при enforce — неожиданные ошибки в логах
#   2 — прогон не состоялся: нет устройства/плеера/правила брандмауэра, нет вердикта, битый JSON,
#       не собран артефакт, сборки из разных ревизий или без ревизии, не собралась пара (-Build), нет run.json,
#       при enforce — битый список известного шума (вердикт по логам вынести нельзя)
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Scenario device-smoke -Arg artifact=1
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Apk Build\Quest\VrBattlegrounds_Quest.apk
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -Build
#   powershell -ExecutionPolicy Bypass -File Tools\e2e\Run-QuestE2E.ps1 -HeadPose off
# В режиме -File массив -Arg передаётся через запятую: -Arg mode=short,puppets=8.

[CmdletBinding()]
param(
    # Имя сценария для -e2eScenario (обе стороны).
    [string]   $Scenario = 'device-smoke',

    # client — шлем клиент выделенного сервера на ПК; host — не реализовано (код 2).
    [string]   $Topology = 'client',

    # Сцена карты для -e2eMap (сервер грузит её, если сценарий этого требует).
    [string]   $Map = 'TestMap2',

    # Общий таймаут ожидания вердиктов в секундах (с момента запуска шлема). Сценариям уходит на 30 с меньше.
    [int]      $Timeout = 300,

    # Параметры сценария ключ=значение, каждый уходит обеим сторонам как -e2eArg. Значения без пробелов и запятых.
    [string[]] $Arg = @(),

    # APK для установки (adb install -r). Пусто — используется уже установленное приложение.
    [string]   $Apk = '',

    # Серийный номер устройства. Пусто — единственное устройство в состоянии device.
    [string]   $Serial = '',

    # Путь к adb.exe. Пусто — PATH, затем Android SDK Unity версии из ProjectSettings/ProjectVersion.txt.
    [string]   $Adb = '',

    # Windows-плеер выделенного сервера. Пусто — Build\e2e\VrBattlegrounds.exe в корне репозитория.
    [string]   $ServerPlayer = '',

    # Пара UDP-портов прогона: KCP и Discovery. 0 и 0 — две свободные, выбранные автоматически.
    [int]      $NetworkPort = 0,
    [int]      $DiscoveryPort = 0,

    # IPv4 ПК для -e2eServerAddress. Пусто — адрес ПК в одной подсети с wlan0 шлема.
    [string]   $ServerAddress = '',

    # Шлем в Quest Link: закрыть Link-клиент перед прогоном (обрывает PC VR-сессию, обратно не возвращается). Без флага — отказ.
    [switch]   $StopLink,

    # Пакет и activity приложения на шлеме.
    [string]   $Package = 'com.UnityTechnologies.com.unity.template.urpblank',
    [string]   $Activity = 'com.unity3d.player.UnityPlayerGameActivity',

    # Собрать пару (e2e-плеер сервера + APK Quest/Test одной ревизии) batch-режимом Unity и прогнать её.
    # Несовместим с -Apk и -ServerPlayer. Редактор этого checkout должен быть закрыт.
    [switch]   $Build,

    # Только с -Build: проверить условия сборки и напечатать команду Unity, ничего не запуская (код 0).
    [switch]   $DryRunBuild,

    # Предел сборки пары в минутах.
    [int]      $BuildTimeoutMinutes = 90,

    # Путь к Unity.exe для -Build. Пусто — версия из ProjectSettings/ProjectVersion.txt в Unity Hub.
    [string]   $Unity = '',

    # Локальный JSON точек обзора; уходит обеим сторонам как -e2eArg views=<путь от persistentDataPath>.
    [string]   $ViewsFile = '',

    # Симуляция позы головы на шлеме: высота глаз в метрах (-e2eHeadPose eye=<м>), calib (цель из калибровки)
    # или off (реальный трекинг шлема — для замеров с настоящей позой).
    [string]   $HeadPose = '1.6',

    # Вердикт по логам: enforce — неожиданные ошибки в логах делают прогон RED (код 1); report — только отчёт.
    [ValidateSet('enforce', 'report')]
    [string]   $LogVerdict = 'enforce'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
try { Import-Module (Join-Path $PSScriptRoot 'E2ELogVerdict.psm1') -Force -ErrorAction Stop }
catch {
    Write-Host ''
    Write-Host ('ОТКАЗ: модуль вердикта по логам не загружен: ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}
$ContractRevision = 5
# Символы, допустимые в аргументах строки -e unity "<...>": её разбирают и sh шлема, и Unity по пробелам.
$SafeToken = '^[A-Za-z0-9_.:/=+@%-]+$'

function Write-Section([string] $Text) {
    Write-Host ''
    Write-Host ('== ' + $Text) -ForegroundColor Cyan
}

# Отказ до начала прогона: папка прогона не создаётся, устройство и ПК не тронуты.
function Stop-Refused([string] $Message, [string[]] $Hints = @()) {
    Write-Host ''
    Write-Host ('ОТКАЗ: ' + $Message) -ForegroundColor Red
    foreach ($hint in $Hints) { Write-Host ('  ' + $hint) }
    exit 2
}

# ── Запуск внешних программ ───────────────────────────────────────────────

# Аргумент командной строки Windows по правилам MSVCRT (кавычки только при пробелах и кавычках).
function ConvertTo-CommandLineArg([string] $Value) {
    if ($Value -eq '') { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

# Запуск с перехватом stdout/stderr и таймаутом. Не через & : в PowerShell 5.1 stderr нативной
# программы при ErrorActionPreference=Stop превращается в исключение.
function Invoke-Tool([string] $FilePath, [string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $quoted = @()
    foreach ($a in $Arguments) { $quoted += (ConvertTo-CommandLineArg $a) }
    $psi.Arguments = ($quoted -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)

    $proc = [System.Diagnostics.Process]::Start($psi)
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    $timedOut = -not $proc.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { try { $proc.Kill() } catch {} }
    # Поток может держать открытым порождённый процесс (первый старт adb-сервера) — ждём ограниченно.
    $out = ''
    $err = ''
    if ($outTask.Wait(10000)) { $out = $outTask.Result }
    if ($errTask.Wait(2000)) { $err = $errTask.Result }
    $code = -1
    if (-not $timedOut) { try { $code = $proc.ExitCode } catch { $code = -1 } }
    return [pscustomobject]@{ ExitCode = $code; Out = $out; Err = $err; TimedOut = $timedOut }
}

function Invoke-Adb([string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    return Invoke-Tool $script:AdbPath (@('-s', $script:Serial) + $Arguments) $TimeoutSeconds
}

function Invoke-AdbShell([string[]] $Arguments, [int] $TimeoutSeconds = 60) {
    return Invoke-Adb (@('shell') + $Arguments) $TimeoutSeconds
}

function Write-TextFile([string] $Path, [string] $Text) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

# ── Порты (как в Run-E2E.ps1) ─────────────────────────────────────────────

function Get-FreeUdpPort {
    $client = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0))
    try { return ([System.Net.IPEndPoint] $client.Client.LocalEndPoint).Port }
    finally { $client.Close() }
}

function Select-PortPair {
    $network = Get-FreeUdpPort
    $discovery = Get-FreeUdpPort
    while ($discovery -eq $network) { $discovery = Get-FreeUdpPort }
    return @($network, $discovery)
}

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

function Get-PortPairError([int] $Network, [int] $Discovery) {
    if ($Network -lt 1 -or $Network -gt 65535) { return ('-NetworkPort вне 1..65535: ' + $Network) }
    if ($Discovery -lt 1 -or $Discovery -gt 65535) { return ('-DiscoveryPort вне 1..65535: ' + $Discovery) }
    if ($Network -eq $Discovery) { return ('-NetworkPort и -DiscoveryPort совпадают: ' + $Network) }
    return ''
}

# Потомки своих процессов по ParentProcessId, созданные не раньше $NotBefore (как в Run-E2E.ps1).
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

# ── Сеть: подсеть шлема и адрес ПК ────────────────────────────────────────

function ConvertTo-UInt32Address([string] $Address) {
    $bytes = ([System.Net.IPAddress]::Parse($Address)).GetAddressBytes()
    [Array]::Reverse($bytes)
    return [System.BitConverter]::ToUInt32($bytes, 0)
}

function Test-SameSubnet([string] $A, [string] $B, [int] $Prefix) {
    if ($Prefix -le 0) { return $true }
    $mask = [uint32]::MaxValue
    if ($Prefix -lt 32) { $mask = [uint32](([uint64][uint32]::MaxValue) -shl (32 - $Prefix) -band [uint32]::MaxValue) }
    return (((ConvertTo-UInt32Address $A) -band $mask) -eq ((ConvertTo-UInt32Address $B) -band $mask))
}

# ── Брандмауэр (только чтение, без прав администратора) ───────────────────

# Возвращает @{ Ok; Detail }. Ok=false — нет разрешающего входящего правила для exe в активном
# включённом профиле или есть блокирующее (блок побеждает разрешение).
function Test-ServerFirewall([string] $ExePath, [int[]] $Ports) {
    $profiles = @(Get-NetFirewallProfile -ErrorAction Stop)
    $active = New-Object System.Collections.Generic.List[string]
    foreach ($connection in @(Get-NetConnectionProfile -ErrorAction SilentlyContinue)) {
        $name = [string] $connection.NetworkCategory
        if ($name -eq 'DomainAuthenticated') { $name = 'Domain' }
        if (-not $active.Contains($name)) { $active.Add($name) }
    }
    if ($active.Count -eq 0) { $active.Add('Public') }

    $enabled = New-Object System.Collections.Generic.List[string]
    foreach ($p in $profiles) {
        if ($active.Contains([string] $p.Name) -and [string] $p.Enabled -eq 'True') { $enabled.Add([string] $p.Name) }
    }
    if ($enabled.Count -eq 0) {
        return @{ Ok = $true; Detail = ('брандмауэр выключен для активных профилей: ' + ($active -join ', ')) }
    }

    $target = [System.IO.Path]::GetFullPath($ExePath)
    $allowed = New-Object System.Collections.Generic.List[string]
    $blocked = New-Object System.Collections.Generic.List[string]
    foreach ($filter in @(Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue)) {
        $program = [string] $filter.Program
        if ($program -eq '' -or $program -eq 'Any') { continue }
        $expanded = [Environment]::ExpandEnvironmentVariables($program)
        if (-not [string]::Equals($expanded, $target, [StringComparison]::OrdinalIgnoreCase)) { continue }
        foreach ($rule in @($filter | Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
            if ([string] $rule.Direction -ne 'Inbound' -or [string] $rule.Enabled -ne 'True') { continue }
            $ruleProfiles = @(([string] $rule.Profile) -split ',\s*')
            $portFilter = $rule | Get-NetFirewallPortFilter -ErrorAction SilentlyContinue
            $protocolOk = ($null -eq $portFilter) -or (@('Any', 'UDP') -contains [string] $portFilter.Protocol)
            $portsOk = $true
            if ($null -ne $portFilter) {
                $local = @($portFilter.LocalPort | ForEach-Object { [string] $_ })
                if (-not ($local -contains 'Any')) {
                    foreach ($port in $Ports) { if (-not ($local -contains [string] $port)) { $portsOk = $false } }
                }
            }
            foreach ($name in $enabled) {
                if (-not ($ruleProfiles -contains 'Any' -or $ruleProfiles -contains $name)) { continue }
                if ([string] $rule.Action -eq 'Block' -and $protocolOk) { $blocked.Add($name + ': ' + $rule.DisplayName) }
                elseif ([string] $rule.Action -eq 'Allow' -and $protocolOk -and $portsOk) { $allowed.Add($name) }
            }
        }
    }

    if ($blocked.Count -gt 0) {
        return @{ Ok = $false; Detail = ('блокирующее входящее правило для плеера: ' + ($blocked -join '; ')) }
    }
    $missing = @($enabled | Where-Object { -not $allowed.Contains($_) })
    if ($missing.Count -gt 0) {
        return @{ Ok = $false; Detail = ('нет разрешающего входящего UDP-правила для плеера в профилях: ' + ($missing -join ', ')) }
    }
    return @{ Ok = $true; Detail = ('есть разрешающее входящее правило для плеера: ' + ($enabled -join ', ')) }
}

# ── Состояние устройства ─────────────────────────────────────────────────

function Get-Battery {
    $r = Invoke-AdbShell @('dumpsys', 'battery') 20
    if ($r.ExitCode -ne 0) { return $null }
    $text = $r.Out
    $level = $null; $temp = $null; $ac = $null; $usb = $null; $wireless = $null; $status = $null
    if ($text -match '(?m)^\s*level:\s*(\d+)') { $level = [int] $Matches[1] }
    if ($text -match '(?m)^\s*temperature:\s*(-?\d+)') { $temp = [double] $Matches[1] / 10.0 }
    if ($text -match '(?m)^\s*AC powered:\s*(\w+)') { $ac = ($Matches[1] -eq 'true') }
    if ($text -match '(?m)^\s*USB powered:\s*(\w+)') { $usb = ($Matches[1] -eq 'true') }
    if ($text -match '(?m)^\s*Wireless powered:\s*(\w+)') { $wireless = ($Matches[1] -eq 'true') }
    if ($text -match '(?m)^\s*status:\s*(\d+)') { $status = [int] $Matches[1] }
    return [ordered]@{
        level = $level; temperatureC = $temp; acPowered = $ac; usbPowered = $usb
        wirelessPowered = $wireless; status = $status; capturedUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
}

function Get-DeviceProp([string] $Name) {
    $r = Invoke-AdbShell @('getprop', $Name) 20
    if ($r.ExitCode -ne 0) { return $null }
    $value = $r.Out.Trim()
    if ($value -eq '') { return $null }
    return $value
}

function Get-PackageVersion {
    $r = Invoke-AdbShell @('dumpsys', 'package', $Package) 30
    $code = $null; $name = $null; $updated = $null
    if ($r.ExitCode -eq 0) {
        if ($r.Out -match 'versionCode=(\d+)') { $code = [long] $Matches[1] }
        if ($r.Out -match 'versionName=(\S+)') { $name = $Matches[1] }
        if ($r.Out -match 'lastUpdateTime=([^\r\n]+)') { $updated = $Matches[1].Trim() }
    }
    return [ordered]@{ versionCode = $code; versionName = $name; lastUpdateTime = $updated }
}

# Wi-Fi шлема из mWifiInfo (dumpsys wifi); при недоступности — cmd wifi status; иначе null.
function Get-DeviceWifi {
    $line = ''
    $r = Invoke-AdbShell @('dumpsys wifi | grep -m 1 "mWifiInfo SSID"') 30
    if ($r.ExitCode -eq 0) { $line = $r.Out }
    if ($line -notmatch 'SSID:') {
        $r = Invoke-AdbShell @('cmd', 'wifi', 'status') 30
        if ($r.ExitCode -eq 0) { $line = $r.Out }
    }
    if ($line -notmatch 'SSID:') { return $null }
    $ssid = $null; $freq = $null; $rssi = $null; $link = $null; $standard = $null
    if ($line -match 'SSID:\s*"([^"]*)"') { $ssid = $Matches[1] }
    if ($line -match 'Frequency:\s*(\d+)\s*MHz') { $freq = [int] $Matches[1] }
    if ($line -match 'RSSI:\s*(-?\d+)') { $rssi = [int] $Matches[1] }
    if ($line -match ',\s*Link speed:\s*(\d+)\s*Mbps') { $link = [int] $Matches[1] }
    if ($line -match 'Wi-Fi standard:\s*(\w+)') { $standard = $Matches[1] }
    return [ordered]@{ ssid = $ssid; frequencyMhz = $freq; rssiDbm = $rssi; linkSpeedMbps = $link; wifiStandard = $standard }
}

# Quest Link активен: Link-клиент запущен и его activity — верхняя в фокусе.
$LinkPackage = 'com.oculus.xrstreamingclient'
# Компонент активной Link-activity (пакет/класс) или $null, если Link не на переднем плане.
function Get-LinkComponent {
    $r = Invoke-AdbShell @('pidof', $LinkPackage) 15
    if ($r.ExitCode -ne 0 -or $r.Out.Trim() -eq '') { return $null }
    $act = Invoke-AdbShell @('dumpsys', 'activity', 'activities') 30
    foreach ($line in ($act.Out -split "`n")) {
        if ($line -match ('topResumedActivity=.*\s(' + [regex]::Escape($LinkPackage) + '/[^\s}]+)')) { return $Matches[1] }
    }
    return $null
}
function Test-LinkActive { return ($null -ne (Get-LinkComponent)) }

# Системный диалог шлема в фокусе (контроллеры, Link отключён, граница и т.п.) или $null. Пока он открыт,
# приложение не получит фокус и am start зависнет; дирижёр диалоги не закрывает — это действие человека.
function Get-BlockingDialog {
    $act = Invoke-AdbShell @('dumpsys', 'activity', 'activities') 30
    foreach ($line in ($act.Out -split "`n")) {
        if ($line -match 'topResumedActivity=.*\s((?:com\.oculus|com\.meta)[^\s/]*/[^\s}]*Dialog[^\s}]*)') { return $Matches[1] }
    }
    return $null
}

function Get-AppPid {
    $r = Invoke-AdbShell @('pidof', $Package) 15
    if ($r.ExitCode -ne 0) { return $null }
    $value = $r.Out.Trim()
    if ($value -match '^(\d+)') { return [int] $Matches[1] }
    return $null
}

# Путь артефакта шлема допустим только внутри каталога файлов приложения (persistentDataPath).
function Test-DeviceArtifactPath([string] $Path) {
    if ([string]::IsNullOrEmpty($Path) -or $Path -notmatch $SafeToken) { return $false }
    if ($Path -match '(^|/)\.\.(/|$)') { return $false }
    $roots = @('/sdcard/', '/storage/emulated/0/', '/storage/self/primary/')
    foreach ($root in $roots) {
        $prefix = $root + 'Android/data/' + $Package + '/files/'
        if ($Path.StartsWith($prefix, [StringComparison]::Ordinal) -and $Path.Length -gt $prefix.Length) { return $true }
    }
    return $false
}

function Read-ResultJson([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try { return (ConvertFrom-Json (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop)) } catch { return $null }
}

function Get-Status($Data) {
    if ($null -eq $Data) { return '' }
    return [string] $Data.status
}

function Get-BuildInfo($Data) {
    if ($null -eq $Data) { return $null }
    return [ordered]@{
        buildGuid = $Data.buildGuid; appVersion = $Data.appVersion; unityVersion = $Data.unityVersion
        developmentBuild = $Data.developmentBuild; platform = $Data.platform
    }
}

# ── Ревизии сборок ────────────────────────────────────────────────────────

# Ревизия из версии вида <база>+<ревизия> (BuildRevisionStamp); $null — штампа нет или он unknown.
function Get-SourceRevision([string] $Version) {
    if ([string]::IsNullOrEmpty($Version)) { return $null }
    if ($Version -notmatch '\+([^+\s]+)$') { return $null }
    $revision = $Matches[1]
    if ($revision -eq 'unknown') { return $null }
    return $revision
}

function Format-VersionForReport([string] $Version) {
    if ([string]::IsNullOrEmpty($Version)) { return 'нет версии' }
    return $Version
}

# Сверка версий сервера (appVersion его результата) и шлема (versionName пакета). Возвращает
# server/client — ревизии ($null, если не определены), mismatch — причина отказа или $null, check — строка для run.json.
function Get-RevisionCheck([string] $ServerVersion, [string] $ClientVersion) {
    $serverRevision = Get-SourceRevision $ServerVersion
    $clientRevision = Get-SourceRevision $ClientVersion
    $result = [ordered]@{ server = $serverRevision; client = $clientRevision; mismatch = $null; check = $null }

    if ($null -eq $serverRevision -or $null -eq $clientRevision) {
        $result.mismatch = 'ревизия сборки не определена: сервер ' + (Format-VersionForReport $ServerVersion) +
            ', шлем ' + (Format-VersionForReport $ClientVersion) + ' (нужен штамп <версия>+<ревизия>, сборки Test/e2e)'
    }
    elseif ($serverRevision -cne $clientRevision) {
        $result.mismatch = 'сборки из разных ревизий: сервер ' + $serverRevision + ', шлем ' + $clientRevision
    }
    elseif ($ServerVersion -cne $ClientVersion) {
        $result.mismatch = 'версии сервера и шлема различаются при одной ревизии: ' + $ServerVersion + ' / ' + $ClientVersion
    }

    if ($null -ne $result.mismatch) { $result.check = $result.mismatch }
    else {
        $result.check = 'ревизии совпадают: ' + $serverRevision
        if ($serverRevision.EndsWith('-dirty')) {
            $result.check += ' (рабочая копия с изменениями: одинаковый штамп не гарантирует одинаковых исходников)'
        }
    }
    return $result
}

# ── Сборка пары (-Build) ──────────────────────────────────────────────────

# Открытый редактор держит Temp\UnityLockfile открытым на запись (как в Tools/release/Build-Game.ps1).
function Test-EditorOpen([string] $LockPath) {
    if (-not (Test-Path -LiteralPath $LockPath)) { return $false }
    try {
        $stream = [System.IO.File]::Open($LockPath, 'Open', 'ReadWrite', 'None')
        $stream.Close()
        return $false
    }
    catch { return $true }
}

function Get-UnityBuildArgs([string] $StatusPath, [string] $LogPath) {
    return @(
        '-batchmode', '-quit',
        '-projectPath', $RepoRoot,
        # Quest собирается только Android-доменом редактора (OculusManifest под #if UNITY_ANDROID, Docs/release.md).
        '-buildTarget', 'Android',
        '-executeMethod', 'VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunPairBatch',
        '-pairStatus', $StatusPath,
        '-logFile', $LogPath
    )
}

# Свободное имя для артефакта в папке назначения (одинаковые имена с разных путей не затирают друг друга).
function Get-ArtifactDestination([string] $Directory, [string] $Leaf) {
    $candidate = Join-Path $Directory $Leaf
    $index = 2
    while (Test-Path -LiteralPath $candidate) {
        $candidate = Join-Path $Directory ([string] $index + '-' + $Leaf)
        $index++
    }
    return $candidate
}

# Файлы git LFS, которые на диске лежат указателями, а не содержимым. git lfs ls-files -n — пути отслеживаемых файлов;
# из каждого существующего читаются только первые 200 байт и сравниваются с сигнатурой указателя.
# error — проверка не выполнена (git lfs недоступен), tracked — число отслеживаемых путей, pointers — пути-указатели.
function Get-LfsPointerFiles([string] $Root) {
    $result = [ordered]@{ error = $null; tracked = 0; pointers = (New-Object System.Collections.Generic.List[string]) }
    $ls = $null
    try { $ls = Invoke-Tool 'git' @('-C', $Root, '-c', 'core.quotepath=false', 'lfs', 'ls-files', '-n') 300 }
    catch { $result.error = 'git не запустился: ' + $_.Exception.Message; return $result }
    if ($ls.TimedOut -or $ls.ExitCode -ne 0) {
        $result.error = 'git lfs ls-files завершился с кодом ' + $ls.ExitCode + ': ' + ($ls.Out + ' ' + $ls.Err).Trim()
        return $result
    }

    $signature = 'version https://git-lfs.github.com/spec/v1'
    $buffer = New-Object byte[] 200
    foreach ($line in ($ls.Out -split "`r?`n")) {
        if ($line -eq '') { continue }
        $result.tracked++
        $full = Join-Path $Root ($line -replace '/', '\')
        if (-not [System.IO.File]::Exists($full)) { continue }
        $read = 0
        $stream = $null
        try {
            $stream = [System.IO.File]::Open($full, 'Open', 'Read', 'ReadWrite')
            $read = $stream.Read($buffer, 0, $buffer.Length)
        }
        catch { continue }
        finally { if ($null -ne $stream) { $stream.Dispose() } }
        if ($read -ge $signature.Length -and [System.Text.Encoding]::ASCII.GetString($buffer, 0, $signature.Length) -ceq $signature) {
            $result.pointers.Add($line)
        }
    }
    return $result
}

function Test-JsonNumber($Value) {
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [decimal]) { return $true }
    if ($Value -is [double]) { return -not ([double]::IsNaN($Value) -or [double]::IsInfinity($Value)) }
    return $false
}

# Проверка файла точек обзора — те же правила, что у E2EViews.Load: корень-объект с непустым массивом views;
# у точки только поля name, x, y, z, yaw; name — уникальная строка из символов $SafeToken; x, z, yaw — числа; y — число,
# если задан. Возвращает error ($null — файл годен), names (по порядку) и count.
function Read-ViewsFile([string] $Path) {
    $result = [ordered]@{ error = $null; names = @(); count = 0 }
    $data = $null
    try { $data = ConvertFrom-Json ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)) }
    catch { $result.error = 'JSON не разбирается: ' + $_.Exception.Message; return $result }
    if ($null -eq $data -or $data -isnot [System.Management.Automation.PSCustomObject]) { $result.error = 'корень должен быть объектом { "views": [...] }'; return $result }
    $viewsProperty = $data.PSObject.Properties | Where-Object { $_.Name -ceq 'views' }
    if ($null -eq $viewsProperty -or $viewsProperty.Value -isnot [System.Array]) { $result.error = 'нет массива "views"'; return $result }
    $items = @($viewsProperty.Value)
    if ($items.Count -eq 0) { $result.error = 'массив "views" пуст'; return $result }

    $allowed = @('name', 'x', 'y', 'z', 'yaw')
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $names = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        if ($item -isnot [System.Management.Automation.PSCustomObject]) { $result.error = 'views[' + $i + '] не объект'; return $result }
        $fields = @{}
        foreach ($p in $item.PSObject.Properties) {
            if ($allowed -cnotcontains $p.Name) { $result.error = 'views[' + $i + ']: неизвестное поле "' + $p.Name + '" (допустимы name, x, y, z, yaw)'; return $result }
            $fields[$p.Name] = $p.Value
        }
        $name = $fields['name']
        if ($name -isnot [string] -or $name -eq '') { $result.error = 'views[' + $i + ']: нет строки "name"'; return $result }
        if ($name -notmatch $SafeToken) { $result.error = 'views[' + $i + ']: имя "' + $name + '" с недопустимыми символами (разрешены ' + $SafeToken + ')'; return $result }
        if (-not $seen.Add($name)) { $result.error = 'views[' + $i + ']: имя "' + $name + '" повторяется'; return $result }
        foreach ($key in @('x', 'z', 'yaw')) {
            if (-not $fields.ContainsKey($key) -or -not (Test-JsonNumber $fields[$key])) {
                $result.error = 'views[' + $i + '] "' + $name + '": нет числа "' + $key + '"'; return $result
            }
        }
        if ($fields.ContainsKey('y') -and -not (Test-JsonNumber $fields['y'])) {
            $result.error = 'views[' + $i + '] "' + $name + '": "y" не число'; return $result
        }
        $names.Add($name)
    }
    $result.names = $names.ToArray()
    $result.count = $names.Count
    return $result
}

# persistentDataPath Windows-плеера сервера: %USERPROFILE%\AppData\LocalLow\<companyName>\<productName> из
# ProjectSettings этого checkout (плеер, собранный из другого checkout с другими именами, сюда не попадёт).
function Get-ServerDataPath {
    $settings = Join-Path $RepoRoot 'ProjectSettings\ProjectSettings.asset'
    $company = Select-String -LiteralPath $settings -Pattern '^\s*companyName:\s*(.+?)\s*$' | Select-Object -First 1
    $product = Select-String -LiteralPath $settings -Pattern '^\s*productName:\s*(.+?)\s*$' | Select-Object -First 1
    if ($null -eq $company -or $null -eq $product) { return $null }
    return (Join-Path $env:USERPROFILE ('AppData\LocalLow\' + $company.Matches[0].Groups[1].Value + '\' + $product.Matches[0].Groups[1].Value))
}

# ── Поза головы (-HeadPose, контракт r5) ──────────────────────────────────

# Пределы высоты глаз — как PlayerCalibrationRules.MinEyeHeight/MaxEyeHeight (их же проверяет E2EContext).
$HeadPoseMinEye = 0.9
$HeadPoseMaxEye = 2.5

# Разбор -HeadPose: число (высота глаз, м) → eye=<м>, calib, off. Ошибка — текст в error.
function Read-HeadPose([string] $Value) {
    $result = [ordered]@{ value = $Value; mode = $null; eye = $null; clientArg = $null; error = $null }
    $text = ([string] $Value).Trim()
    if ($text -ieq 'off') { $result.mode = 'off'; return $result }
    if ($text -ieq 'calib') { $result.mode = 'calib'; $result.clientArg = 'calib'; return $result }
    if ($text -notmatch '^\d+(\.\d+)?$') {
        $result.error = 'нужна высота глаз в метрах (например 1.6, точка — разделитель), calib или off'
        return $result
    }
    $eye = [double]::Parse($text, [System.Globalization.CultureInfo]::InvariantCulture)
    if ($eye -lt $HeadPoseMinEye -or $eye -gt $HeadPoseMaxEye) {
        $result.error = ('высота глаз ' + $text + ' м вне ' + $HeadPoseMinEye + '..' + $HeadPoseMaxEye + ' м (PlayerCalibrationRules)')
        return $result
    }
    $result.mode = 'eye'
    $result.eye = $eye
    $result.clientArg = 'eye=' + $eye.ToString('0.###', [System.Globalization.CultureInfo]::InvariantCulture)
    return $result
}

# aapt из Android SDK Unity версии проекта (самые новые build-tools), затем PATH; $null — не найден.
function Find-Aapt {
    $versionFile = Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt'
    if (Test-Path -LiteralPath $versionFile) {
        $versionText = Get-Content -LiteralPath $versionFile -Raw
        if ($versionText -match 'm_EditorVersion:\s*(\S+)') {
            $buildTools = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $Matches[1] + '\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools')
            if (Test-Path -LiteralPath $buildTools) {
                $dirs = @(Get-ChildItem -LiteralPath $buildTools -Directory | Sort-Object -Descending -Property @{ Expression = {
                    $v = $null
                    if ([version]::TryParse($_.Name, [ref] $v)) { $v } else { [version] '0.0' }
                } })
                foreach ($dir in $dirs) {
                    $exe = Join-Path $dir.FullName 'aapt.exe'
                    if (Test-Path -LiteralPath $exe) { return $exe }
                }
            }
        }
    }
    $command = Get-Command 'aapt.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    return $null
}

# meta-data манифеста из вывода `aapt dump xmltree`: имя → значение. Строка — как есть; булево (type 0x12) — true/false;
# прочие типы — сырой текст aapt; без android:value — $null.
function Read-ManifestMetaData([string] $XmlTree) {
    $result = @{}
    $inMeta = $false
    $name = $null
    $value = $null
    foreach ($line in (($XmlTree -split "`r?`n") + @('E: <конец>'))) {
        if ($line -match '^\s*E:\s+(\S+)') {
            if ($inMeta -and $null -ne $name) { $result[$name] = $value }
            $inMeta = ($Matches[1] -eq 'meta-data')
            $name = $null
            $value = $null
            continue
        }
        if (-not $inMeta) { continue }
        if ($line -match '^\s*A:\s+android:name(\([^)]*\))?="([^"]*)"') { $name = $Matches[2]; continue }
        if ($line -match '^\s*A:\s+android:value(\([^)]*\))?=(.*)$') {
            $rawValue = $Matches[2].Trim()
            if ($rawValue -match '^"([^"]*)"') { $value = $Matches[1] }
            elseif ($rawValue -match '^\(type 0x12\)0x([0-9a-fA-F]+)') {
                if ([Convert]::ToUInt32($Matches[1], 16) -ne 0) { $value = 'true' } else { $value = 'false' }
            }
            else { $value = $rawValue }
        }
    }
    return $result
}

# Значение флага по ключам манифеста, оканчивающимся на $Suffix: true — хоть один включён, false — все выключены,
# absent — ключа нет, иначе — сырое значение.
function Get-ManifestFlag($Meta, [string] $Suffix) {
    $keys = @($Meta.Keys | Where-Object { $_ -match ('(?i)' + [regex]::Escape($Suffix) + '$') })
    if ($keys.Count -eq 0) { return 'absent' }
    $values = @($keys | ForEach-Object { ([string] $Meta[$_]).ToLowerInvariant() })
    if ($values -contains 'true') { return 'true' }
    if (@($values | Where-Object { $_ -ne 'false' }).Count -eq 0) { return 'false' }
    return ($values -join ',')
}

# LateLatching/SpaceWarp по манифесту APK (контракт r5: при симуляции позы оба выключены).
# status: passed — оба ключа есть и false; failed — хоть один true; unknown — проверить нельзя или ключа нет.
# Ключ LateLatching пишет OculusBuildProcessor (com.unity.xr.oculus.LateLatching); SpaceWarp Oculus XR Plugin 4.5
# в манифест не пишет — его в рантайме проверяет E2EHeadPose (OculusSettings) на шлеме.
function Get-ApkRenderCheck([string] $ApkPath) {
    $check = [ordered]@{
        status = 'unknown'; source = 'apk-manifest'; apk = $ApkPath; aapt = $null
        lateLatching = $null; spaceWarp = $null; oculusKeys = $null; detail = $null
    }
    $aapt = Find-Aapt
    if ($null -eq $aapt) {
        $check.source = $null
        $check.detail = 'aapt не найден (build-tools Android SDK Unity версии проекта, PATH) — манифест APK не проверен'
        return $check
    }
    $check.aapt = $aapt
    $dump = Invoke-Tool $aapt @('dump', 'xmltree', $ApkPath, 'AndroidManifest.xml') 120
    if ($dump.TimedOut -or $dump.ExitCode -ne 0) {
        $check.detail = ('aapt dump xmltree не удался (код ' + $dump.ExitCode + '): ' + ($dump.Err + ' ' + $dump.Out).Trim())
        return $check
    }
    $meta = Read-ManifestMetaData $dump.Out
    $check.oculusKeys = @($meta.Keys | Where-Object { $_ -like 'com.unity.xr.oculus.*' -or $_ -like 'com.oculus.*' } | Sort-Object)
    $check.lateLatching = Get-ManifestFlag $meta '.LateLatching'
    $check.spaceWarp = Get-ManifestFlag $meta 'SpaceWarp'

    $on = @()
    if ($check.lateLatching -eq 'true') { $on += 'LateLatching' }
    if ($check.spaceWarp -eq 'true') { $on += 'SpaceWarp' }
    if ($on.Count -gt 0) {
        $check.status = 'failed'
        $check.detail = ('в манифесте включено: ' + ($on -join ', ') + ' — рантайм подменял бы позу рендера позой шлема')
        return $check
    }
    if ($check.lateLatching -eq 'false' -and $check.spaceWarp -eq 'false') {
        $check.status = 'passed'
        $check.detail = 'LateLatching=false, SpaceWarp=false'
        return $check
    }
    $missing = @()
    if ($check.lateLatching -ne 'false') { $missing += ('LateLatching=' + $check.lateLatching) }
    if ($check.spaceWarp -ne 'false') { $missing += ('SpaceWarp=' + $check.spaceWarp) }
    $check.detail = ('по манифесту не подтверждено: ' + ($missing -join ', ') + '; ключей Oculus в манифесте: ' +
        $check.oculusKeys.Count + '. Рантайм-проверку (OculusSettings) делает E2EHeadPose на шлеме')
    return $check
}

function Write-HeadPoseRender($Check) {
    $color = 'Gray'
    if ($Check.status -eq 'unknown') { $color = 'Yellow' }
    if ($Check.status -eq 'failed') { $color = 'Red' }
    Write-Host ('  рендер для позы головы: ' + $Check.status + ' — ' + $Check.detail) -ForegroundColor $color
}

# ══════════════════════════════════════════════════════════════════════════
#  1. Проверки до прогона: при отказе ни устройство, ни ПК не тронуты
# ══════════════════════════════════════════════════════════════════════════

if ($Topology -eq 'host') {
    Stop-Refused 'host на шлеме требует шва выбора роли в плеере (зона network), не реализовано.' @(
        'Собранный плеер на шлеме стартует клиентом (роль выбирает GameNetworkDiscovery). Используй -Topology client.')
}
if ($Topology -ne 'client') { Stop-Refused ('неизвестная топология "' + $Topology + '": допустимы client и host') }

if ($Scenario -notmatch $SafeToken) { Stop-Refused ('имя сценария с недопустимыми символами: "' + $Scenario + '"') }
if ($Map -notmatch $SafeToken) { Stop-Refused ('имя карты с недопустимыми символами: "' + $Map + '"') }
if ($Timeout -lt 60) { Stop-Refused ('-Timeout меньше 60 с: сценарию нужно на 30 с меньше дирижёра') }
$scenarioTimeout = $Timeout - 30

# -Arg: в режиме -File массив приходит одной строкой через запятую.
$scenarioArgs = New-Object System.Collections.Generic.List[string]
$argKeys = New-Object System.Collections.Generic.List[string]
foreach ($item in $Arg) {
    foreach ($pair in ([string] $item -split ',')) {
        if ($pair -eq '') { continue }
        if ($pair -notmatch '^([^=\s]+)=(\S*)$') {
            Stop-Refused ('-Arg "' + $pair + '": нужен вид ключ=значение, непустой ключ, без пробелов')
        }
        $argKey = $Matches[1]
        if ($pair -notmatch $SafeToken) {
            Stop-Refused ('-Arg "' + $pair + '": недопустимые символы (строку -e unity разбирают sh шлема и Unity; разрешены ' + $SafeToken + ')')
        }
        if ($argKeys.Contains($argKey)) { Stop-Refused ('-Arg: ключ "' + $argKey + '" задан повторно') }
        $argKeys.Add($argKey)
        $scenarioArgs.Add($pair)
    }
}

# ── поза головы (-HeadPose) ──
$headPoseInfo = Read-HeadPose $HeadPose
if ($null -ne $headPoseInfo.error) {
    Stop-Refused ('-HeadPose "' + $HeadPose + '": ' + $headPoseInfo.error) @(
        'Допустимо: высота глаз в метрах (по умолчанию 1.6), calib — из калибровки, off — реальный трекинг шлема.')
}

# ── точки обзора (-ViewsFile) ──
$viewsInfo = $null
if (-not [string]::IsNullOrEmpty($ViewsFile)) {
    if ($argKeys.Contains('views')) { Stop-Refused '-ViewsFile и -Arg views=... несовместимы: путь к точкам задаёт дирижёр' }
    if (-not (Test-Path -LiteralPath $ViewsFile -PathType Leaf)) { Stop-Refused ('-ViewsFile не найден: ' + $ViewsFile) }
    $ViewsFile = (Resolve-Path -LiteralPath $ViewsFile).Path
    $viewsInfo = Read-ViewsFile $ViewsFile
    if ($null -ne $viewsInfo.error) { Stop-Refused ('-ViewsFile ' + $ViewsFile + ': ' + $viewsInfo.error) }
    $ServerDataPath = Get-ServerDataPath
    if ($null -eq $ServerDataPath) { Stop-Refused 'в ProjectSettings/ProjectSettings.asset нет companyName/productName — не найти persistentDataPath сервера' }
}

$autoPorts = ($NetworkPort -eq 0 -and $DiscoveryPort -eq 0)
if (-not $autoPorts) {
    $portError = ''
    if ($NetworkPort -eq 0 -or $DiscoveryPort -eq 0) { $portError = '-NetworkPort и -DiscoveryPort задаются только вместе (0 и 0 — автоматически)' }
    else { $portError = Get-PortPairError $NetworkPort $DiscoveryPort }
    if ($portError -ne '') { Stop-Refused ('порты прогона заданы неверно: ' + $portError) }
}

# ── сборка пары (-Build): условия до устройства, сама сборка — в начале прогона ──
$UnityPath = ''
$BuiltApk = Join-Path $RepoRoot 'Build\Quest\VrBattlegrounds_Quest.apk'
$PairStatusInEditor = 'Build/e2e-pair-status.json'
if ($DryRunBuild -and -not $Build) { Stop-Refused '-DryRunBuild работает только вместе с -Build' }
if ($Build) {
    if (-not [string]::IsNullOrEmpty($Apk)) { Stop-Refused '-Build и -Apk несовместимы: -Build ставит APK, собранный в этом прогоне' }
    if (-not [string]::IsNullOrEmpty($ServerPlayer)) { Stop-Refused '-Build и -ServerPlayer несовместимы: сервер — e2e-плеер, собранный в этом прогоне' }
    if ($BuildTimeoutMinutes -lt 1) { Stop-Refused ('-BuildTimeoutMinutes меньше 1: ' + $BuildTimeoutMinutes) }

    # Лёгкий агентский worktree (роль LFS «agent») держит вместо больших ассетов LFS-указатели: batch Unity на нём
    # импортировал бы указатели как ассеты и собрал бы битую игру (Docs/agents/lfs-worktrees.md). Роль читаем штатным
    # Tools/agents/configure_lfs_worktree.py status: основной checkout («main») и worker держат полные файлы.
    $lfsRole = ''
    try {
        $lfsStatus = Invoke-Tool 'python' @('-X', 'utf8', (Join-Path $RepoRoot 'Tools\agents\configure_lfs_worktree.py'), 'status') 60
        if ($lfsStatus.Out -match '"role"\s*:\s*"([^"]+)"') { $lfsRole = $Matches[1] }
    } catch {}
    if ($lfsRole -eq 'agent') {
        Stop-Refused '-Build недоступен в этом checkout: LFS-роль «agent», большие ассеты — указатели.' @(
            'Собери пару там, где ассеты полные: агент — на worker под арендой через',
            ('  VrBattlegrounds.EditorTools.E2EPlayerBuilder.StartPairBuild("' + $PairStatusInEditor + '"),'),
            'человек — в основном checkout. Затем запусти без -Build с -Apk и -ServerPlayer на собранную пару.')
    }

    # Роль «main» не гарантирует полных файлов: часть LFS-ассетов может остаться указателями (частичный pull,
    # include/exclude). Сборка из них — битая игра, поэтому до Unity проверяются сами файлы.
    $lfs = Get-LfsPointerFiles $RepoRoot
    if ($null -ne $lfs.error) {
        Stop-Refused ('-Build: проверка LFS-указателей не выполнена — ' + $lfs.error) @('Нужен git с git-lfs в PATH.')
    }
    if ($lfs.pointers.Count -gt 0) {
        $shown = @($lfs.pointers | Select-Object -First 10)
        $hints = @('Первые пути:')
        foreach ($p in $shown) { $hints += ('  ' + $p) }
        if ($lfs.pointers.Count -le $shown.Count) {
            $hints += 'Выгрузи содержимое (checkout — если объекты уже в локальном LFS-хранилище, иначе pull):'
            $hints += ('  git lfs checkout -- ' + (($shown | ForEach-Object { '"' + $_ + '"' }) -join ' '))
            $hints += ('  git lfs pull --include="' + ($shown -join ',') + '"')
        }
        else {
            $hints += 'Выгрузи содержимое всех указателей (checkout — если объекты уже в локальном LFS-хранилище, иначе pull):'
            $hints += '  git lfs checkout'
            $hints += '  git lfs pull'
        }
        Stop-Refused ('-Build: в checkout ' + $lfs.pointers.Count + ' из ' + $lfs.tracked +
            ' LFS-файлов — указатели, а не содержимое; Unity собрал бы битую игру.') $hints
    }
    Write-Host ('  LFS: ' + $lfs.tracked + ' отслеживаемых файлов, указателей нет')

    if (Test-EditorOpen (Join-Path $RepoRoot 'Temp\UnityLockfile')) {
        Stop-Refused ('редактор открыт; соберите пару в редакторе: VrBattlegrounds.EditorTools.E2EPlayerBuilder.StartPairBuild("' +
            $PairStatusInEditor + '") (агент — на worker под арендой), затем запустите без -Build с -Apk') @(
            'Пустой файл статуса — сборка идёт; JSON с serverPassed/questPassed — закончена.',
            ('Затем: Run-QuestE2E.ps1 -Apk ' + $BuiltApk))
    }

    if ([string]::IsNullOrEmpty($Unity)) {
        $versionLine = Select-String -LiteralPath (Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$'
        if ($null -eq $versionLine) { Stop-Refused 'в ProjectSettings/ProjectVersion.txt нет m_EditorVersion' }
        $Unity = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $versionLine.Matches[0].Groups[1].Value.Trim() + '\Editor\Unity.exe')
    }
    if (-not (Test-Path -LiteralPath $Unity)) { Stop-Refused ('не найден Unity: ' + $Unity + '. Укажи путь параметром -Unity.') }
    $UnityPath = (Resolve-Path -LiteralPath $Unity).Path
    $ServerPlayer = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot 'Build\e2e\VrBattlegrounds.exe'))

    if ($DryRunBuild) {
        $previewDir = Join-Path $RepoRoot 'Tools\e2e\results\<прогон>'
        $previewArgs = Get-UnityBuildArgs (Join-Path $previewDir 'pair-status.json') (Join-Path $previewDir 'unity-build.log')
        Write-Section 'Сухой прогон -Build: Unity не запускается, устройство и ПК не тронуты'
        Write-Host ('  редактор этого checkout закрыт, Unity: ' + $UnityPath)
        Write-Host ('  команда: ' + (ConvertTo-CommandLineArg $UnityPath) + ' ' + (($previewArgs | ForEach-Object { ConvertTo-CommandLineArg $_ }) -join ' '))
        Write-Host ('  предел:  ' + $BuildTimeoutMinutes + ' мин')
        Write-Host ('  APK:     ' + $BuiltApk)
        Write-Host ('  сервер:  ' + $ServerPlayer)
        exit 0
    }
}

if ([string]::IsNullOrEmpty($ServerPlayer)) { $ServerPlayer = Join-Path $RepoRoot 'Build\e2e\VrBattlegrounds.exe' }
if (-not $Build) {
    if (-not (Test-Path -LiteralPath $ServerPlayer)) {
        Stop-Refused ('Windows-плеер сервера не найден: ' + $ServerPlayer) @(
            'Собери пару одной ревизии: -Build (редактор закрыт) или E2EPlayerBuilder.StartPairBuild в редакторе,',
            'или укажи -ServerPlayer <путь к .exe>.')
    }
    $ServerPlayer = (Resolve-Path -LiteralPath $ServerPlayer).Path
}
if (-not [string]::IsNullOrEmpty($Apk)) {
    if (-not (Test-Path -LiteralPath $Apk)) { Stop-Refused ('APK не найден: ' + $Apk) }
    $Apk = (Resolve-Path -LiteralPath $Apk).Path
}

# ── рендер для симуляции позы головы: LateLatching/SpaceWarp по манифесту APK, до устройства ──
# С -Build APK появится в прогоне — проверка после сборки; без -Apk и -Build проверить нечего (renderCheck=unknown).
$headPoseRender = $null
if (-not [string]::IsNullOrEmpty($Apk)) {
    $headPoseRender = Get-ApkRenderCheck $Apk
    Write-HeadPoseRender $headPoseRender
    if ($headPoseRender.status -eq 'failed' -and $headPoseInfo.mode -ne 'off') {
        Stop-Refused ('-HeadPose ' + $headPoseInfo.mode + ': APK ' + $Apk + ' — ' + $headPoseRender.detail) @(
            'Выключи Late Latching и Application SpaceWarp (Project Settings → XR Plug-in Management → Oculus) и пересобери APK,',
            'или запусти с -HeadPose off (реальный трекинг шлема).')
    }
}
elseif (-not $Build) {
    $headPoseRender = [ordered]@{
        status = 'unknown'; source = $null; apk = $null; aapt = $null; lateLatching = $null; spaceWarp = $null; oculusKeys = $null
        detail = 'APK не задан (-Apk/-Build) — манифест установленного пакета не проверяется; рантайм-проверку делает E2EHeadPose'
    }
    if ($headPoseInfo.mode -ne 'off') { Write-HeadPoseRender $headPoseRender }
}

# ── adb ──
$AdbPath = ''
if (-not [string]::IsNullOrEmpty($Adb)) {
    if (-not (Test-Path -LiteralPath $Adb)) { Stop-Refused ('-Adb не найден: ' + $Adb) }
    $AdbPath = (Resolve-Path -LiteralPath $Adb).Path
}
else {
    $command = Get-Command 'adb.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) { $AdbPath = $command.Source }
    else {
        $versionFile = Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt'
        $unityVersion = ''
        if (Test-Path -LiteralPath $versionFile) {
            $versionText = Get-Content -LiteralPath $versionFile -Raw
            if ($versionText -match 'm_EditorVersion:\s*(\S+)') { $unityVersion = $Matches[1] }
        }
        if ($unityVersion -ne '') {
            $candidate = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $unityVersion + '\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
            if (Test-Path -LiteralPath $candidate) { $AdbPath = $candidate }
        }
    }
}
if ($AdbPath -eq '') {
    Stop-Refused 'adb не найден: ни -Adb, ни PATH, ни Android SDK Unity версии проекта.' @(
        'Укажи -Adb <путь к adb.exe> или установи модуль Android Build Support в Unity Hub.')
}

# ── устройство ──
[void](Invoke-Tool $AdbPath @('start-server') 30)
$devicesResult = Invoke-Tool $AdbPath @('devices') 30
if ($devicesResult.ExitCode -ne 0) { Stop-Refused ('adb devices завершился с кодом ' + $devicesResult.ExitCode + ': ' + $devicesResult.Err.Trim()) }
$deviceStates = [ordered]@{}
foreach ($line in ($devicesResult.Out -split "`r?`n")) {
    if ($line -match '^(\S+)\s+(\S+)\s*$' -and $line -notmatch '^List of devices') { $deviceStates[$Matches[1]] = $Matches[2] }
}
if ($Serial -ne '') {
    if (-not $deviceStates.Contains($Serial)) {
        Stop-Refused ('устройство "' + $Serial + '" не подключено. Видны: ' + (($deviceStates.Keys | ForEach-Object { $_ + ' (' + $deviceStates[$_] + ')' }) -join ', '))
    }
    if ($deviceStates[$Serial] -ne 'device') { Stop-Refused ('устройство "' + $Serial + '" в состоянии ' + $deviceStates[$Serial] + ', нужно device') }
}
else {
    $ready = @($deviceStates.Keys | Where-Object { $deviceStates[$_] -eq 'device' })
    if ($ready.Count -ne 1) {
        $seen = 'нет'
        if ($deviceStates.Count -gt 0) { $seen = (($deviceStates.Keys | ForEach-Object { $_ + ' (' + $deviceStates[$_] + ')' }) -join ', ') }
        Stop-Refused ('нужно ровно одно устройство в состоянии device, найдено ' + $ready.Count + '. Видны: ' + $seen) @('Укажи -Serial.')
    }
    $Serial = $ready[0]
}
if ($Serial -notmatch $SafeToken) { Stop-Refused ('серийный номер с недопустимыми символами: ' + $Serial) }

# ── одно устройство — один прогон ──
$foreignPid = Get-AppPid
if ($null -ne $foreignPid) {
    Stop-Refused ('приложение ' + $Package + ' уже запущено на ' + $Serial + ' (pid ' + $foreignPid + ') — не нашим прогоном.') @(
        'Закрой его на шлеме или дождись конца чужого прогона; дирижёр не гасит чужие процессы.')
}

# ── Quest Link ──
# В Link шлем работает дисплеем ПК: Link-клиент держит фокус, нативное приложение не получит его,
# и am start зависнет. По умолчанию — отказ. По -StopLink Link закрывается внутри прогона (try),
# обратно не запускается (связь с ПК со шлема не восстанавливается надёжно) — это фиксируется в run.json.
$linkComponent = Get-LinkComponent
if ($null -ne $linkComponent -and -not $StopLink) {
    Stop-Refused ('шлем ' + $Serial + ' в Quest Link (' + $linkComponent + ' на переднем плане) — игра не получит фокус.') @(
        'Выйди из Link на шлеме (быстрые настройки → Quest Link → отключить) и повтори прогон,',
        'или запусти с -StopLink: дирижёр закроет Link (обратно не возвращает; связь с ПК может потребовать перезапуска среды Link на ПК).')
}
$linkStopped = $false

$blockingDialog = Get-BlockingDialog
if ($null -ne $blockingDialog) {
    $dialogHint = 'Закрой его на шлеме (надень шлем или нажми кнопку контроллера) и повтори прогон.'
    if ($blockingDialog -match 'ControllerRequired') { $dialogHint = 'Система требует контроллеры: разбуди их (кнопка на контроллере) и повтори прогон.' }
    if ($blockingDialog -match 'LinkDisconnected') { $dialogHint = 'Диалог «Link отключён»: закрой его на шлеме и повтори прогон.' }
    Stop-Refused ('на шлеме ' + $Serial + ' открыт системный диалог ' + $blockingDialog + ' — приложение не получит фокус.') @($dialogHint)
}
if ($Apk -eq '' -and -not $Build) {
    $installed = Get-PackageVersion
    if ($null -eq $installed.versionCode) { Stop-Refused ('пакет ' + $Package + ' не установлен на ' + $Serial + '; укажи -Apk') }
}

# ── адреса: шлем и ПК в одной подсети ──
$deviceIp = $null
$devicePrefix = 0
$ipResult = Invoke-AdbShell @('ip', '-f', 'inet', 'addr', 'show', 'wlan0') 20
if ($ipResult.Out -match 'inet\s+(\d+\.\d+\.\d+\.\d+)/(\d+)') { $deviceIp = $Matches[1]; $devicePrefix = [int] $Matches[2] }
if ($null -eq $deviceIp) { Stop-Refused ('у шлема нет IPv4 на wlan0 — он не в Wi-Fi сети. Вывод: ' + ($ipResult.Out + $ipResult.Err).Trim()) }

$pcCandidates = @()
try {
    $pcCandidates = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction Stop |
        Where-Object { $_.IPAddress -ne '127.0.0.1' -and -not $_.IPAddress.StartsWith('169.254.') } |
        ForEach-Object { [string] $_.IPAddress })
} catch { $pcCandidates = @() }
if ($ServerAddress -eq '') {
    $sameSubnet = @($pcCandidates | Where-Object { Test-SameSubnet $_ $deviceIp $devicePrefix })
    if ($sameSubnet.Count -eq 0) {
        Stop-Refused ('у ПК нет IPv4 в подсети шлема ' + $deviceIp + '/' + $devicePrefix + '. Адреса ПК: ' + ($pcCandidates -join ', ')) @(
            'Подключи ПК и шлем к одной сети или укажи -ServerAddress.')
    }
    $ServerAddress = $sameSubnet[0]
}
elseif ($ServerAddress -notmatch '^\d+\.\d+\.\d+\.\d+$') { Stop-Refused ('-ServerAddress не IPv4: ' + $ServerAddress) }
elseif (-not (Test-SameSubnet $ServerAddress $deviceIp $devicePrefix)) {
    Write-Host ('  внимание: -ServerAddress ' + $ServerAddress + ' не в подсети шлема ' + $deviceIp + '/' + $devicePrefix) -ForegroundColor Yellow
}

# ── порты ──
$portsReady = $false
for ($attempt = 1; $attempt -le 3; $attempt++) {
    if ($autoPorts) { $pair = Select-PortPair; $NetworkPort = $pair[0]; $DiscoveryPort = $pair[1] }
    if ((Test-UdpPortFree $NetworkPort) -and (Test-UdpPortFree $DiscoveryPort)) { $portsReady = $true; break }
    if (-not $autoPorts) { break }
}
if (-not $portsReady) { Stop-Refused ('нет свободной пары UDP-портов (KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort + ')') }

# ── брандмауэр Windows ──
$firewall = $null
try { $firewall = Test-ServerFirewall $ServerPlayer @($NetworkPort, $DiscoveryPort) }
catch { Stop-Refused ('не удалось прочитать правила брандмауэра Windows: ' + $_.Exception.Message) }
if (-not $firewall.Ok) {
    Stop-Refused ('брандмауэр Windows не пропустит UDP шлема к серверу: ' + $firewall.Detail) @(
        'Скрипт сам ничего не меняет. Человек с правами администратора выполняет одну из команд:',
        ('  New-NetFirewallRule -DisplayName "VR Battlegrounds e2e server" -Direction Inbound -Action Allow -Protocol UDP -Program "' + $ServerPlayer + '" -Profile Any'),
        ('  netsh advfirewall firewall add rule name="VR Battlegrounds e2e server" dir=in action=allow protocol=UDP program="' + $ServerPlayer + '" profile=any enable=yes'))
}

# ══════════════════════════════════════════════════════════════════════════
#  2. Прогон: с этого места run.json пишется всегда (finally)
# ══════════════════════════════════════════════════════════════════════════

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$runId = $stamp + '-quest-' + $Scenario
$runDir = Join-Path $RepoRoot ('Tools\e2e\results\' + $runId)
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$deviceFilesDir = '/sdcard/Android/data/' + $Package + '/files'
$deviceResult = $deviceFilesDir + '/e2e/' + $runId + '/client-1.json'
$serverResult = Join-Path $runDir 'server.json'
$serverLog = Join-Path $runDir 'server.log'
$clientResult = Join-Path $runDir 'client-1.json'
$deviceToken = 'e2e-quest-' + $Serial

$serverArgs = @(
    '-batchmode', '-nographics',
    '-e2eScenario', $Scenario,
    '-e2eRole', 'server',
    '-e2eResult', $serverResult,
    '-e2eMap', $Map,
    '-e2eClients', '1',
    '-e2eTimeout', "$scenarioTimeout",
    '-e2eNetworkPort', "$NetworkPort",
    '-e2eDiscoveryPort', "$DiscoveryPort",
    '-logFile', $serverLog
)
$clientTokens = @(
    '-e2eScenario', $Scenario,
    '-e2eRole', 'client-1',
    '-e2eResult', $deviceResult,
    '-e2eMap', $Map,
    '-e2eTimeout', "$scenarioTimeout",
    '-e2eQuitOnFinish',
    '-e2eNetworkPort', "$NetworkPort",
    '-e2eDiscoveryPort', "$DiscoveryPort",
    '-e2eServerAddress', $ServerAddress,
    '-e2eDeviceToken', $deviceToken
)
foreach ($pair in $scenarioArgs) { $serverArgs += @('-e2eArg', $pair); $clientTokens += @('-e2eArg', $pair) }
# Поза головы — только шлему: серверу (headless) симулировать нечего.
if ($headPoseInfo.mode -ne 'off') { $clientTokens += @('-e2eHeadPose', $headPoseInfo.clientArg) }

# Точки обзора: один относительный путь от persistentDataPath для обеих сторон; файл кладётся в оба места до запуска.
$viewsArgPath = $null
if ($null -ne $viewsInfo) {
    $viewsArgPath = 'e2e/' + $runId + '/views.json'
    $serverArgs += @('-e2eArg', ('views=' + $viewsArgPath))
    $clientTokens += @('-e2eArg', ('views=' + $viewsArgPath))
}
foreach ($token in $clientTokens) {
    if ($token -notmatch $SafeToken) { Stop-Refused ('аргумент шлема с недопустимыми символами: "' + $token + '"') }
}
$unityArgs = $clientTokens -join ' '
$component = $Package + '/' + $Activity

$manifest = [ordered]@{
    contract = 'quest-device-run'
    contractRevision = $ContractRevision
    runId = $runId
    scenario = $Scenario
    topology = $Topology
    map = $Map
    timeoutSeconds = $Timeout
    scenarioTimeoutSeconds = $scenarioTimeout
    scenarioArgs = $scenarioArgs.ToArray()
    startedUtc = (Get-Date).ToUniversalTime().ToString('o')
    finishedUtc = $null
    device = [ordered]@{
        serial = $Serial; model = $null; fingerprint = $null; package = $Package; activity = $Activity
        versionCode = $null; versionName = $null; lastUpdateTime = $null; ip = $deviceIp; prefixLength = $devicePrefix
    }
    apk = $null
    batteryBefore = $null
    batteryAfter = $null
    deviceWifi = $null
    pc = [ordered]@{ cpu = $null; computerName = $env:COMPUTERNAME; firewall = $firewall.Detail }
    network = [ordered]@{ serverAddress = $ServerAddress; networkPort = $NetworkPort; discoveryPort = $DiscoveryPort; autoPorts = $autoPorts }
    launch = [ordered]@{
        device = [ordered]@{ command = ('am start -S -W -n ' + $component + " -e unity '" + $unityArgs + "'"); unityArgs = $unityArgs; amStartOutput = $null; pid = $null }
        server = [ordered]@{ player = $ServerPlayer; args = $serverArgs; pid = $null }
    }
    build = $null
    builds = [ordered]@{ server = $null; client = $null; revisionServer = $null; revisionClient = $null; check = $null }
    link = [ordered]@{ wasActive = ($null -ne $linkComponent); component = $linkComponent; stopped = $false; restored = $null; detail = $null }
    artifacts = [ordered]@{ server = @(); client = @() }
    views = $null
    headPose = [ordered]@{
        value = $HeadPose
        mode = $headPoseInfo.mode
        eye = $headPoseInfo.eye
        clientArg = $headPoseInfo.clientArg
        contractRule = 'quest-device-run r5: симуляция позы головы'
        renderCheck = $(if ($null -ne $headPoseRender) { $headPoseRender.status } else { 'unknown' })
        render = $headPoseRender
    }
    files = @()
    failure = $null
    exitCode = 2
    verdict = $null
    logVerdict = $null
}

if ($null -ne $viewsInfo) {
    $manifest.views = [ordered]@{
        source = $ViewsFile
        sha256 = (Get-FileHash -LiteralPath $ViewsFile -Algorithm SHA256).Hash.ToLowerInvariant()
        count = $viewsInfo.count
        names = $viewsInfo.names
        arg = $viewsArgPath
        local = 'views.json'
        device = ($deviceFilesDir + '/' + $viewsArgPath)
        server = (Join-Path $ServerDataPath ($viewsArgPath -replace '/', '\'))
        pushed = $false
        serverCopied = $false
    }
}

Write-Section 'Конфигурация прогона'
Write-Host ('  устройство: ' + $Serial + ' (' + $deviceIp + '/' + $devicePrefix + ')')
Write-Host ('  сервер:     ' + $ServerPlayer)
Write-Host ('  адрес ПК:   ' + $ServerAddress)
Write-Host ('  сценарий:   ' + $Scenario + ', карта ' + $Map + ', параметры: ' + ($scenarioArgs -join ' '))
Write-Host ('  таймаут:    ' + $Timeout + ' с (сценариям ' + $scenarioTimeout + ' с)')
Write-Host ('  порты:      KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort)
Write-Host ('  брандмауэр: ' + $firewall.Detail)
if ($null -ne $manifest.views) {
    Write-Host ('  точки:      ' + $manifest.views.count + ' из ' + $ViewsFile + ' (' + ($manifest.views.names -join ', ') + ')')
}
if ($headPoseInfo.mode -eq 'off') { Write-Host '  поза головы: off — реальный трекинг шлема' }
else { Write-Host ('  поза головы: -e2eHeadPose ' + $headPoseInfo.clientArg + ' (рендер: ' + $manifest.headPose.renderCheck + ')') }
Write-Host ('  результат:  ' + $runDir)

$serverProc = $null
$logcatProc = $null
$appPid = $null
$failure = $null
$deviceStarted = $false
$runStartedAt = (Get-Date).AddSeconds(-1)
$exitCode = 2
$pairRevision = $null

try {
    # ── сборка пары (-Build) ──
    if ($Build) {
        Write-Section 'Собираю пару: e2e-плеер сервера и APK Quest/Test одной ревизии (batch Unity)'
        $pairStatusPath = Join-Path $runDir 'pair-status.json'
        $unityLog = Join-Path $runDir 'unity-build.log'
        $unityBuildArgs = Get-UnityBuildArgs $pairStatusPath $unityLog
        $manifest.build = [ordered]@{
            status = 'running'; revision = $null; durationSeconds = $null; unityExitCode = $null
            unity = $UnityPath; args = $unityBuildArgs; statusFile = 'pair-status.json'; log = 'unity-build.log'; error = $null
        }
        if (Test-EditorOpen (Join-Path $RepoRoot 'Temp\UnityLockfile')) {
            $manifest.build.status = 'not-started'
            throw 'редактор этого checkout открылся до сборки — собери пару в редакторе (StartPairBuild) и запусти с -Apk'
        }
        Write-Host ('  лог Unity: ' + $unityLog + ' (предел ' + $BuildTimeoutMinutes + ' мин)')
        $buildStarted = Get-Date
        $unityRun = Invoke-Tool $UnityPath $unityBuildArgs ($BuildTimeoutMinutes * 60)
        $manifest.build.durationSeconds = [math]::Round(((Get-Date) - $buildStarted).TotalSeconds)
        $manifest.build.unityExitCode = $unityRun.ExitCode
        $pairData = Read-ResultJson $pairStatusPath
        if ($null -ne $pairData) { $manifest.build.revision = [string] $pairData.revision }

        if ($unityRun.TimedOut) {
            $manifest.build.status = 'timeout'
            throw ('сборка пары не уложилась в ' + $BuildTimeoutMinutes + ' мин, Unity остановлен; смотри ' + $unityLog)
        }
        if ($null -eq $pairData) {
            $manifest.build.status = 'failed'
            throw ('сборка пары не оставила статуса (код Unity ' + $unityRun.ExitCode + '); смотри ' + $unityLog)
        }
        if (-not ($pairData.serverPassed -and $pairData.questPassed) -or $unityRun.ExitCode -ne 0) {
            $manifest.build.status = 'failed'
            $manifest.build.error = [string] $pairData.error
            throw ('сборка пары не удалась: ' + [string] $pairData.error + ' (код Unity ' + $unityRun.ExitCode + '); смотри ' + $unityLog + ' и pair-status.json')
        }
        foreach ($builtFile in @($BuiltApk, $ServerPlayer)) {
            if (-not (Test-Path -LiteralPath $builtFile)) {
                $manifest.build.status = 'failed'
                throw ('сборка пары отметилась успешной, но файла нет: ' + $builtFile)
            }
        }
        $manifest.build.status = 'passed'
        $pairRevision = Get-SourceRevision ('+' + [string] $pairData.revision)
        $Apk = (Resolve-Path -LiteralPath $BuiltApk).Path
        Write-Host ('  собрано за ' + $manifest.build.durationSeconds + ' с, ревизия ' + $pairData.revision)

        # Рендер для симуляции позы головы — по манифесту только что собранного APK, до установки.
        $headPoseRender = Get-ApkRenderCheck $Apk
        $manifest.headPose.render = $headPoseRender
        $manifest.headPose.renderCheck = $headPoseRender.status
        Write-HeadPoseRender $headPoseRender
        if ($headPoseRender.status -eq 'failed' -and $headPoseInfo.mode -ne 'off') {
            throw ('-HeadPose ' + $headPoseInfo.mode + ': собранный APK — ' + $headPoseRender.detail + '; выключи Late Latching/SpaceWarp или -HeadPose off')
        }

        # Сборка шла долго: условия шлема, проверенные до неё, перепроверяются.
        $foreignPid = Get-AppPid
        if ($null -ne $foreignPid) { throw ('за время сборки приложение ' + $Package + ' запустили не нашим прогоном (pid ' + $foreignPid + ')') }
        $linkNow = Get-LinkComponent
        if ($null -ne $linkNow -and -not $StopLink) { throw ('за время сборки шлем ушёл в Quest Link (' + $linkNow + ') — выйди из Link или запусти с -StopLink') }
        if ($null -ne $linkNow -and $null -eq $linkComponent) {
            $linkComponent = $linkNow
            $manifest.link.wasActive = $true
            $manifest.link.component = $linkNow
        }
        $dialogNow = Get-BlockingDialog
        if ($null -ne $dialogNow) { throw ('за время сборки на шлеме открылся системный диалог ' + $dialogNow) }
    }

    # ── условия прогона ──
    $manifest.device.model = Get-DeviceProp 'ro.product.model'
    $manifest.device.fingerprint = Get-DeviceProp 'ro.build.fingerprint'
    try { $manifest.pc.cpu = ([string] (Get-CimInstance -ClassName Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name).Trim() } catch {}
    $manifest.deviceWifi = Get-DeviceWifi

    if ($null -ne $linkComponent) {
        Write-Section 'Закрываю Quest Link на время прогона (-StopLink)'
        [void](Invoke-AdbShell @('am', 'force-stop', $LinkPackage) 20)
        $linkStopped = $true
        $manifest.link.stopped = $true
        Start-Sleep -Seconds 3
        if (Test-LinkActive) { throw ('Quest Link не закрылся после force-stop ' + $LinkPackage) }
        Write-Host ('  закрыт ' + $linkComponent + '; обратно не возвращается')
    }

    if ($Apk -ne '') {
        Write-Section 'Устанавливаю APK'
        $hash = (Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash.ToLowerInvariant()
        $manifest.apk = [ordered]@{ name = [System.IO.Path]::GetFileName($Apk); path = $Apk; sha256 = $hash; installed = $false }
        $install = Invoke-Adb @('install', '-r', $Apk) 600
        if ($install.ExitCode -ne 0 -or $install.Out -notmatch 'Success') {
            throw ('adb install не удался: ' + ($install.Out + ' ' + $install.Err).Trim())
        }
        $manifest.apk.installed = $true
        Write-Host ('  установлен, sha256 ' + $hash)
    }
    $version = Get-PackageVersion
    $manifest.device.versionCode = $version.versionCode
    $manifest.device.versionName = $version.versionName
    $manifest.device.lastUpdateTime = $version.lastUpdateTime
    if ($null -eq $version.versionCode) { throw ('пакет ' + $Package + ' не установлен') }

    # ── ревизия APK до запуска; ревизию сервера отдаст его результат, сверка обеих — после прогона ──
    $installedRevision = Get-SourceRevision $version.versionName
    $manifest.builds.revisionClient = $installedRevision
    if ($null -eq $installedRevision) {
        Write-Host ('  versionName ' + (Format-VersionForReport $version.versionName) +
            ' без штампа ревизии — сверка сборок даст код 2 (нужен APK Test/e2e со штампом)') -ForegroundColor Yellow
    }
    else { Write-Host ('  versionName ' + $version.versionName + ', ревизия APK ' + $installedRevision) }
    if ($null -ne $pairRevision -and $installedRevision -cne $pairRevision) {
        throw ('сборки из разных ревизий: собрана пара ' + $pairRevision + ', на шлеме установлен ' + (Format-VersionForReport $version.versionName))
    }

    # ── точки обзора: папка прогона, persistentDataPath сервера, шлем (после установки APK — каталог уже приложения) ──
    if ($null -ne $manifest.views) {
        Write-Section 'Кладу файл точек обзора обеим сторонам'
        $viewsLocal = Join-Path $runDir 'views.json'
        Copy-Item -LiteralPath $ViewsFile -Destination $viewsLocal -Force -ErrorAction Stop

        New-Item -ItemType Directory -Path (Split-Path -Parent $manifest.views.server) -Force | Out-Null
        Copy-Item -LiteralPath $viewsLocal -Destination $manifest.views.server -Force -ErrorAction Stop
        $manifest.views.serverCopied = $true
        Write-Host ('  сервер: ' + $manifest.views.server)

        $deviceViewsDir = $manifest.views.device.Substring(0, $manifest.views.device.LastIndexOf('/'))
        $mkdir = Invoke-AdbShell @('mkdir', '-p', $deviceViewsDir) 30
        if ($mkdir.ExitCode -ne 0) { throw ('mkdir -p ' + $deviceViewsDir + ' на шлеме не удался: ' + ($mkdir.Out + ' ' + $mkdir.Err).Trim()) }
        $push = Invoke-Adb @('push', $viewsLocal, $manifest.views.device) 60
        if ($push.ExitCode -ne 0) { throw ('adb push файла точек не удался: ' + ($push.Out + ' ' + $push.Err).Trim()) }
        $manifest.views.pushed = $true
        Write-Host ('  шлем:   ' + $manifest.views.device)
    }

    # ── сервер ──
    Write-Section 'Запускаю выделенный сервер'
    if (-not ((Test-UdpPortFree $NetworkPort) -and (Test-UdpPortFree $DiscoveryPort))) {
        throw ('порты заняты перед стартом сервера: KCP ' + $NetworkPort + ', Discovery ' + $DiscoveryPort)
    }
    $quotedServerArgs = @()
    foreach ($a in $serverArgs) { $quotedServerArgs += (ConvertTo-CommandLineArg $a) }
    $serverProc = Start-Process -FilePath $ServerPlayer -ArgumentList $quotedServerArgs -PassThru
    $manifest.launch.server.pid = $serverProc.Id
    Write-Host ('  pid ' + $serverProc.Id)

    $ready = $false
    $readyDeadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $readyDeadline) {
        if (Test-Path -LiteralPath $serverResult) { $ready = $true; break }
        if ($serverProc.HasExited) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw ('сервер не отметился файлом результата за 90 с, смотри ' + $serverLog) }
    Write-Host '  харнесс сервера поднялся'
    # StartServer и AdvertiseServer идут уже в первой сцене.
    Start-Sleep -Seconds 6

    # ── шлем ──
    Write-Section 'Готовлю шлем'
    $manifest.batteryBefore = Get-Battery
    [void](Invoke-AdbShell @('input', 'keyevent', 'KEYCODE_WAKEUP') 20)
    [void](Invoke-AdbShell @('am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close') 20)
    [void](Invoke-Adb @('logcat', '-c') 20)
    # Кольцевой буфер logcat шлема мал (сотни КиБ): за прогон он перезаписывается, поэтому весь прогон
    # дополнительно пишется потоком в файл. Настройки буфера не меняем.
    $streamLog = Join-Path $runDir 'logcat-stream.txt'
    $logcatProc = Start-Process -FilePath $AdbPath -ArgumentList @('-s', $Serial, 'logcat', '-v', 'threadtime') `
        -RedirectStandardOutput $streamLog -RedirectStandardError (Join-Path $runDir 'logcat-stream.err.txt') `
        -WindowStyle Hidden -PassThru

    Write-Section 'Запускаю приложение на шлеме'
    Write-Host ('  am start -n ' + $component)
    Write-Host ('  unity: ' + $unityArgs)
    $deviceStarted = $true
    $start = Invoke-AdbShell @('am', 'start', '-S', '-W', '-n', $component, '-e', 'unity', ("'" + $unityArgs + "'")) 120
    $manifest.launch.device.amStartOutput = ($start.Out + $start.Err).Trim()
    if ($start.ExitCode -ne 0 -or $start.Out -match 'Error' -or $start.Out -notmatch 'Status:\s*ok') {
        $dialogNow = $null
        try { $dialogNow = Get-BlockingDialog } catch {}
        $dialogText = 'системного диалога в фокусе не видно'
        if ($null -ne $dialogNow) { $dialogText = 'на шлеме открыт системный диалог ' + $dialogNow }
        throw ('am start не удался (код ' + $start.ExitCode + ', таймаут ' + $start.TimedOut + '; ' + $dialogText + '): ' +
               ($manifest.launch.device.amStartOutput -replace '\s*\r?\n\s*', ' | '))
    }
    # -W сообщает, какая activity оказалась на экране. Системный диалог вместо приложения (например,
    # LaunchCheckControllerRequiredDialogActivity при выключенных контроллерах) — приложение не запущено.
    if ($start.Out -match '(?m)^Activity:\s*(\S+)') {
        $shown = $Matches[1]
        if (-not $shown.StartsWith($Package + '/', [StringComparison]::Ordinal)) {
            $hint = ''
            if ($shown -match 'ControllerRequired') { $hint = ' — система требует контроллеры: включи их (или сборка должна объявлять поддержку рук)' }
            throw ('вместо приложения шлем показал ' + $shown + $hint)
        }
    }
    for ($i = 0; $i -lt 20 -and $null -eq $appPid; $i++) {
        $appPid = Get-AppPid
        if ($null -eq $appPid) { Start-Sleep -Milliseconds 500 }
    }
    $manifest.launch.device.pid = $appPid
    if ($null -eq $appPid) { Write-Host '  pid приложения не найден — процесс уже завершился?' -ForegroundColor Yellow }
    else { Write-Host ('  pid ' + $appPid) }

    # ── ожидание вердиктов ──
    Write-Section 'Жду вердикты сервера и шлема'
    $deadline = (Get-Date).AddSeconds($Timeout)
    $clientGoneTicks = 0
    $serverGoneTicks = 0
    $lastLine = ''
    while ($true) {
        $serverStatus = Get-Status (Read-ResultJson $serverResult)
        $clientStatus = ''
        $cat = Invoke-AdbShell @('cat', $deviceResult) 20
        if ($cat.ExitCode -eq 0) {
            try { $clientStatus = [string] (ConvertFrom-Json $cat.Out).status } catch { $clientStatus = '' }
        }
        $serverDone = ($serverStatus -ne '' -and $serverStatus -ne 'started')
        $clientDone = ($clientStatus -ne '' -and $clientStatus -ne 'started')
        $line = '  сервер: ' + $serverStatus + ', шлем: ' + $clientStatus
        if ($line -ne $lastLine) { Write-Host $line; $lastLine = $line }
        if ($serverDone -and $clientDone) { break }

        # Процесс исчез, а вердикта нет — дальше он уже не появится (короткий запас на запись файла).
        if (-not $clientDone -and $null -eq (Get-AppPid)) { $clientGoneTicks++ } else { $clientGoneTicks = 0 }
        if (-not $serverDone -and $serverProc.HasExited) { $serverGoneTicks++ } else { $serverGoneTicks = 0 }
        if ($clientGoneTicks -ge 3) { Write-Host '  приложение на шлеме завершилось без вердикта' -ForegroundColor Yellow; break }
        if ($serverGoneTicks -ge 3) { Write-Host '  сервер завершился без вердикта' -ForegroundColor Yellow; break }
        if ((Get-Date) -ge $deadline) { Write-Host ('  таймаут дирижёра ' + $Timeout + ' с') -ForegroundColor Yellow; break }
        Start-Sleep -Seconds 2
    }
}
catch {
    $failure = $_.Exception.Message
    Write-Host ''
    Write-Host ('Прогон прерван: ' + $failure) -ForegroundColor Red
}
finally {
    # ══ 3. Уборка и сбор: всегда, в том числе при Ctrl+C и исключении ══
    Write-Section 'Гашу процессы и возвращаю шлем'
    if ($deviceStarted) {
        try { [void](Invoke-AdbShell @('am', 'force-stop', $Package) 30); Write-Host '  am force-stop' } catch {}
    }
    if ($null -ne $serverProc) {
        try {
            $descendants = @(Get-DescendantProcessIds @([int] $serverProc.Id) $runStartedAt)
            if (-not $serverProc.HasExited) { Stop-Process -Id $serverProc.Id -Force -ErrorAction Stop; Write-Host ('  убит сервер pid ' + $serverProc.Id) }
            foreach ($childId in $descendants) {
                try { Stop-Process -Id $childId -Force -ErrorAction Stop; Write-Host ('  убит дочерний pid ' + $childId) } catch {}
            }
        } catch {}
    }
    try { [void](Invoke-AdbShell @('am', 'broadcast', '-a', 'com.oculus.vrpowermanager.automation_disable') 30); Write-Host '  automation_disable' } catch {}

    # Link закрыт нами по -StopLink. Автоматический возврат не делаем (решение пользователя 2026-10-12: перезапуск
    # Link-клиента со шлема не восстанавливает связь с ПК надёжно) — отмечаем в run.json и подсказываем человеку.
    if ($linkStopped) {
        $manifest.link.restored = $false
        $manifest.link.detail = 'Link закрыт на время прогона и автоматически не возвращается — переподключи на шлеме; если связь с ПК не встаёт, перезапусти среду Meta Quest Link на ПК (Docs/testing.md)'
        Write-Host ('  Quest Link: ' + $manifest.link.detail) -ForegroundColor Yellow
    }

    if ($deviceStarted) {
        # logcat: процесс приложения (все теги) и полный Unity-тег; без pid — ещё и весь буфер.
        try {
            if ($null -ne $appPid) {
                $r = Invoke-Adb @('logcat', '-d', '-v', 'threadtime', ('--pid=' + $appPid)) 120
                Write-TextFile (Join-Path $runDir 'client-1.logcat.txt') $r.Out
            }
            $r = Invoke-Adb @('logcat', '-d', '-v', 'threadtime', '-s', 'Unity') 120
            Write-TextFile (Join-Path $runDir 'unity.logcat.txt') $r.Out
            if ($null -eq $appPid) {
                $r = Invoke-Adb @('logcat', '-d', '-v', 'threadtime') 180
                Write-TextFile (Join-Path $runDir 'logcat-full.txt') $r.Out
            }
        } catch { Write-Host ('  logcat не снят: ' + $_.Exception.Message) -ForegroundColor Yellow }
        if ($null -ne $logcatProc) {
            try { if (-not $logcatProc.HasExited) { Stop-Process -Id $logcatProc.Id -Force -ErrorAction Stop } } catch {}
        }

        try {
            $pull = Invoke-Adb @('pull', $deviceResult, $clientResult) 60
            if ($pull.ExitCode -ne 0) { Write-Host ('  результат шлема не забран: ' + ($pull.Out + $pull.Err).Trim()) -ForegroundColor Yellow }
        } catch {}
        try { $manifest.batteryAfter = Get-Battery } catch {}
    }

    # ── артефакты ──
    $artifactProblems = New-Object System.Collections.Generic.List[string]
    $clientData = Read-ResultJson $clientResult
    $serverData = Read-ResultJson $serverResult
    if ($null -ne $clientData -and $null -ne $clientData.artifacts) {
        $dest = Join-Path $runDir 'artifacts\client-1'
        foreach ($path in @($clientData.artifacts)) {
            $entry = [ordered]@{ source = [string] $path; local = $null; ok = $false; detail = $null }
            if (-not (Test-DeviceArtifactPath ([string] $path))) {
                $entry.detail = 'путь вне каталога файлов приложения или с недопустимыми символами'
            }
            else {
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
                $local = Get-ArtifactDestination $dest ([string] $path).TrimEnd('/').Split('/')[-1]
                $r = Invoke-Adb @('pull', [string] $path, $local) 600
                $entry.local = $local.Substring($runDir.Length + 1)
                $entry.ok = ($r.ExitCode -eq 0 -and (Test-Path -LiteralPath $local))
                $entry.detail = ($r.Out + $r.Err).Trim()
            }
            if (-not $entry.ok) { $artifactProblems.Add('client-1: ' + $path + ' — ' + $entry.detail) }
            $manifest.artifacts.client += ,$entry
        }
    }
    if ($null -ne $serverData -and $null -ne $serverData.artifacts) {
        $dest = Join-Path $runDir 'artifacts\server'
        foreach ($path in @($serverData.artifacts)) {
            $entry = [ordered]@{ source = [string] $path; local = $null; ok = $false; detail = $null }
            try {
                if (-not (Test-Path -LiteralPath ([string] $path))) { throw 'нет на диске' }
                New-Item -ItemType Directory -Path $dest -Force | Out-Null
                $local = Get-ArtifactDestination $dest (Split-Path -Leaf ([string] $path))
                Copy-Item -LiteralPath ([string] $path) -Destination $local -Recurse -Force -ErrorAction Stop
                $entry.local = $local.Substring($runDir.Length + 1)
                $entry.ok = $true
            } catch { $entry.detail = $_.Exception.Message }
            if (-not $entry.ok) { $artifactProblems.Add('server: ' + $path + ' — ' + $entry.detail) }
            $manifest.artifacts.server += ,$entry
        }
    }

    # ── сверка сборок обеих сторон ──
    $manifest.builds.server = Get-BuildInfo $serverData
    $manifest.builds.client = Get-BuildInfo $clientData
    # Ревизия шлема — из versionName установленного пакета (без него — из appVersion его результата),
    # сервера — из appVersion его результата. Без результата сервера сверять нечего: прогон и так код 2.
    $buildMismatch = $null
    $check = 'нет данных: результат сервера не получен, ревизию сервера сверить нельзя'
    $clientVersion = [string] $manifest.device.versionName
    if ($clientVersion -eq '' -and $null -ne $clientData) { $clientVersion = [string] $clientData.appVersion }
    if ($null -ne $serverData) {
        $revisionCheck = Get-RevisionCheck ([string] $serverData.appVersion) $clientVersion
        $manifest.builds.revisionServer = $revisionCheck.server
        $manifest.builds.revisionClient = $revisionCheck.client
        $check = $revisionCheck.check
        if ($null -ne $clientData -and [string] $serverData.unityVersion -ne [string] $clientData.unityVersion) {
            $buildMismatch = 'версии Unity сервера и шлема различаются: ' + $serverData.unityVersion + ' / ' + $clientData.unityVersion
            $check = $buildMismatch + '; ' + $check
        }
        elseif ($null -ne $revisionCheck.mismatch) { $buildMismatch = $revisionCheck.mismatch }
    }
    $manifest.builds.check = $check

    # ── вердикт ──
    Write-Section 'Отчёт'
    $roleReports = New-Object System.Collections.ArrayList
    $anyMissing = $false
    $anyInconclusive = $false
    $anyRed = $false
    foreach ($item in @(@{ role = 'server'; path = $serverResult; data = $serverData }, @{ role = 'client-1'; path = $clientResult; data = $clientData })) {
        $data = $item.data
        if ($null -eq $data) {
            $anyMissing = $true
            Write-Host ('[' + $item.role + '] результата нет или битый JSON: ' + $item.path) -ForegroundColor Red
            [void]$roleReports.Add([pscustomobject]@{ role = $item.role; passed = $false; status = 'missing'; summary = 'файл результата не получен или не разобрался' })
            continue
        }
        $colour = 'Red'
        if ($data.passed) { $colour = 'Green' }
        Write-Host ('[' + $item.role + '] passed=' + $data.passed + ' status=' + $data.status + ' (' + $data.durationSeconds + ' с)') -ForegroundColor $colour
        Write-Host ('           ' + $data.summary)
        foreach ($c in @($data.checks)) {
            if ($null -eq $c) { continue }
            $mark = 'FAIL'; $cc = 'Red'
            if ($c.passed) { $mark = ' OK '; $cc = 'Green' }
            Write-Host ('   [' + $mark + '] ' + $c.name) -ForegroundColor $cc
            Write-Host ('           ' + $c.detail) -ForegroundColor DarkGray
        }
        if ($data.status -ne 'completed') { $anyInconclusive = $true }
        if (-not $data.passed -and $data.status -eq 'completed') { $anyRed = $true }
        [void]$roleReports.Add([pscustomobject]@{ role = $item.role; passed = $data.passed; status = $data.status; summary = $data.summary; checks = $data.checks })
    }

    $reasons = New-Object System.Collections.Generic.List[string]
    if ($null -ne $failure) { $reasons.Add($failure) }
    if ($anyMissing) { $reasons.Add('нет результата одной из сторон') }
    if ($anyInconclusive) { $reasons.Add('сценарий не дошёл до completed') }
    if ($null -ne $buildMismatch) { $reasons.Add($buildMismatch) }
    foreach ($p in $artifactProblems) { $reasons.Add('артефакт не собран: ' + $p) }

    if ($reasons.Count -gt 0) { $exitCode = 2 }
    elseif ($anyRed) { $exitCode = 1 }
    else { $exitCode = 0 }

    $verdict = 'INCONCLUSIVE — прогон не состоялся, вердикт вынести нельзя'
    if ($exitCode -eq 1) { $verdict = 'RED — сценарий поймал отказ' }
    if ($exitCode -eq 0) { $verdict = 'GREEN — все проверки зелёные' }

    # ── вердикт по логам (r4) ──
    Write-Section 'Вердикт по логам'
    $logProcesses = @(@{ process = 'server'; data = $serverData }, @{ process = 'client-1'; data = $clientData })
    $logRaw = New-Object System.Collections.ArrayList
    $logNotes = New-Object System.Collections.ArrayList
    [void]$logRaw.Add(@{ process = 'server'; path = $serverLog; pid = $null })
    if ($deviceStarted) {
        if ($null -ne $appPid) {
            # Поток logcat — все процессы устройства: нативные падения берём только по pid приложения.
            [void]$logRaw.Add(@{ process = 'client-1'; path = (Join-Path $runDir 'logcat-stream.txt'); pid = $appPid })
            [void]$logRaw.Add(@{ process = 'client-1'; path = (Join-Path $runDir 'client-1.logcat.txt'); pid = $appPid })
        }
        else { [void]$logNotes.Add('client-1: pid приложения не найден — нативные падения шлема в logcat не проверены') }
    }
    try {
        $logResult = Invoke-E2ELogVerdict -Mode $LogVerdict -Processes $logProcesses -RawLogs $logRaw.ToArray() `
            -Notes $logNotes.ToArray() -OutPath (Join-Path $runDir 'log-verdict.json')
    }
    catch {
        $logResult = New-E2ELogVerdictError $LogVerdict $_.Exception.Message (Join-Path $runDir 'log-verdict.json')
    }
    Write-E2ELogVerdictSummary $logResult
    $exitBeforeLogs = $exitCode
    $exitCode = Get-E2ELogVerdictExitCode $exitCode $logResult
    if ($exitCode -ne $exitBeforeLogs) {
        if ($exitCode -eq 1) { $verdict = 'RED — проверки зелёные, но в логах неожиданные ошибки или нативные падения' }
        if ($exitCode -eq 2) {
            $verdict = 'INCONCLUSIVE — прогон не состоялся, вердикт вынести нельзя'
            $reasons.Add('вердикт по логам вынести нельзя: ' + $logResult.error)
        }
    }
    $manifest.logVerdict = [ordered]@{
        mode = $logResult.mode
        verdict = $logResult.verdict
        unexpected = @($logResult.unexpected).Count
        known = @($logResult.known).Count
        native = @($logResult.native).Count
        dropped = $logResult.dropped
        error = $logResult.error
        file = 'log-verdict.json'
        contractRule = 'quest-device-run r4: вердикт по логам'
    }

    $manifest.failure = $null
    if ($reasons.Count -gt 0) { $manifest.failure = ($reasons -join '; ') }
    $manifest.exitCode = $exitCode
    $manifest.verdict = $verdict
    $manifest.finishedUtc = (Get-Date).ToUniversalTime().ToString('o')

    $summary = [pscustomobject]@{
        scenario = $Scenario; topology = $Topology; map = $Map; clients = 1; serial = $Serial
        networkPort = $NetworkPort; discoveryPort = $DiscoveryPort; serverAddress = $ServerAddress
        startedLocal = $stamp; verdict = $verdict; exitCode = $exitCode; failure = $manifest.failure
        roles = $roleReports
        logVerdict = $manifest.logVerdict
    }
    try { Write-TextFile (Join-Path $runDir 'summary.json') ($summary | ConvertTo-Json -Depth 8) } catch {}
    try {
        $manifest.files = @(Get-ChildItem -LiteralPath $runDir -Recurse -File | ForEach-Object { $_.FullName.Substring($runDir.Length + 1) })
        Write-TextFile (Join-Path $runDir 'run.json') ($manifest | ConvertTo-Json -Depth 10)
    }
    catch {
        Write-Host ('run.json не записан: ' + $_.Exception.Message) -ForegroundColor Red
        $exitCode = 2
    }
    if (-not (Test-Path -LiteralPath (Join-Path $runDir 'run.json'))) { $exitCode = 2 }

    $verdictColour = 'Yellow'
    if ($exitCode -eq 1) { $verdictColour = 'Red' }
    if ($exitCode -eq 0) { $verdictColour = 'Green' }
    Write-Host ''
    foreach ($reason in $reasons) { Write-Host ('  причина: ' + $reason) -ForegroundColor Yellow }
    Write-Host ('ИТОГ: ' + $verdict) -ForegroundColor $verdictColour
    Write-Host ('Папка прогона: ' + $runDir)
}
exit $exitCode
