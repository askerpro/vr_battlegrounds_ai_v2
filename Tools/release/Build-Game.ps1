# Сборка сервера, Quest и планшета без открытого редактора (batch-режим Unity).
#
# Вся логика — в Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs; скрипт только
# находит Unity нужной версии, запускает -executeMethod и показывает хвост лога.
# Профили сверяются по отпечатку UXR id (uxr-ids.txt рядом со сборками) — см. Docs/release.md.
#
# Unity запускается отдельно на каждую группу платформ: Android-профили (quest, tablet) — один
# запуск с -buildTarget Android, server — отдельный с -buildTarget Win64. Editor-скрипты
# компилируются под платформу запуска, а переключение платформы внутри сборки их не
# перекомпилирует: OculusManifest (VR-категория в манифесте Quest) и другие Android-обработчики
# есть только в редакторе, открытом под Android. Один запуск «server,quest» дал бы плоский Quest.
#
# Редактор на этом проекте должен быть закрыт: Unity не открывает проект дважды.
# При открытом редакторе — меню Tools/VR Battlegrounds/Release.
#
# Коды возврата: 0 — все сборки собраны, 1 — сборка упала, 2 — не запустилась.
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\release\Build-Game.ps1 -Targets quest,tablet -Config Prod

[CmdletBinding()]
param(
    # all или список через запятую: server, quest, tablet.
    [string] $Targets = 'all',

    # Test — проверка на шлеме (Development, полные стектрейсы, быстрая сборка IL2CPP);
    # Prod — релиз (IL2CPP Master, без Development). Таблица — Docs/release.md.
    [ValidateSet('Test', 'Prod')]
    [string] $Config = 'Test',

    # Устарело: то же, что -Config Test.
    [switch] $Development,

    # Путь к Unity.exe. Пусто — версия из ProjectSettings/ProjectVersion.txt в Unity Hub.
    [string] $Unity = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

# Запуски Unity по группам платформ: сначала Android (quest, tablet), затем server.
# Бросает на неизвестном профиле.
function Get-BuildRuns([string] $Spec) {
    $known = @('server', 'quest', 'tablet')
    $names = New-Object System.Collections.Generic.List[string]

    foreach ($item in ($Spec -split '[,;]')) {
        $name = $item.Trim().ToLower()
        if ($name -eq '') { continue }

        if ($name -eq 'all') { $expanded = $known }
        elseif ($known -contains $name) { $expanded = @($name) }
        else { throw "неизвестный профиль '$name'. Есть: all, server, quest, tablet." }

        foreach ($n in $expanded) { if (-not $names.Contains($n)) { $names.Add($n) } }
    }

    if ($names.Count -eq 0) { throw 'не задан ни один профиль. Есть: all, server, quest, tablet.' }

    $runs = New-Object System.Collections.Generic.List[object]
    $android = @(@('quest', 'tablet') | Where-Object { $names.Contains($_) })
    if ($android.Count -gt 0) { $runs.Add([pscustomobject]@{ Name = 'android'; BuildTarget = 'Android'; Profiles = ($android -join ',') }) }
    if ($names.Contains('server')) { $runs.Add([pscustomobject]@{ Name = 'server'; BuildTarget = 'Win64'; Profiles = 'server' }) }
    return $runs.ToArray()
}

# Итог сборщика — блок от маркера [GameBuilder] до конца сообщения; нет маркера — хвост лога.
function Show-LogResult([string] $Log) {
    if (-not (Test-Path $Log)) {
        Write-Host "Лога нет: $Log" -ForegroundColor Yellow
        return
    }

    $lines = Get-Content $Log -Encoding UTF8
    $start = ($lines | Select-String -SimpleMatch '[GameBuilder]' | Select-Object -Last 1)
    if ($start) {
        $lines[($start.LineNumber - 1)..([Math]::Min($lines.Count - 1, $start.LineNumber + 15))] | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host 'Итога [GameBuilder] в логе нет — сборка не дошла до конца. Хвост лога:' -ForegroundColor Yellow
        $lines | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
    }
}

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

try {
    $runs = @(Get-BuildRuns $Targets)
} catch {
    Write-Host ('Профили: ' + $_.Exception.Message) -ForegroundColor Red
    exit 2
}

if ([string]::IsNullOrEmpty($Unity)) {
    $versionLine = Select-String -Path (Join-Path $RepoRoot 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$'
    $version = $versionLine.Matches[0].Groups[1].Value.Trim()
    $Unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
}

if (-not (Test-Path $Unity)) {
    Write-Host "Не найден Unity: $Unity. Укажи путь параметром -Unity." -ForegroundColor Red
    exit 2
}

# Открытый редактор держит Temp\UnityLockfile открытым на запись.
$lock = Join-Path $RepoRoot 'Temp\UnityLockfile'
if (Test-Path $lock) {
    try {
        $stream = [System.IO.File]::Open($lock, 'Open', 'ReadWrite', 'None')
        $stream.Close()
    } catch {
        Write-Host 'Проект открыт в редакторе. Закрой его или собирай из меню Tools/VR Battlegrounds/Release.' -ForegroundColor Red
        exit 2
    }
}

if ($Development) { $Config = 'Test' }

$logDir = Join-Path $RepoRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

Write-Host "Unity: $Unity"
Write-Host "Профили: $Targets, конфигурация: $Config, запусков Unity: $($runs.Count)"

$index = 0
foreach ($run in $runs) {
    $index++
    $log = Join-Path $logDir ('build-' + $stamp + '-' + $run.Name + '.log')

    $arguments = @(
        '-batchmode', '-quit',
        '-projectPath', "`"$RepoRoot`"",
        '-buildTarget', $run.BuildTarget,
        '-executeMethod', 'VrBattlegrounds.EditorTools.Release.GameBuilder.RunBatch',
        '-buildProfile', $run.Profiles,
        '-logFile', "`"$log`"",
        '-config', $Config
    )

    Write-Host ''
    Write-Host "Запуск $index/$($runs.Count): профили $($run.Profiles), -buildTarget $($run.BuildTarget)"
    Write-Host "Лог: $log"

    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -NoNewWindow -PassThru -Wait
    $code = $process.ExitCode

    Show-LogResult $log

    if ($code -ne 0) {
        Write-Host "Unity завершился с кодом $code (профили $($run.Profiles))." -ForegroundColor Red
        exit 1
    }
}

Write-Host 'Готово. Сборки в Build\Server, Build\Quest, Build\Tablet.' -ForegroundColor Green
exit 0
