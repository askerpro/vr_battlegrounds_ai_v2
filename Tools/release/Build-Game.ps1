# Сборка сервера, Quest и планшета без открытого редактора (batch-режим Unity).
#
# Вся логика — в Assets/Editor/VR_Battlegrounds/Release/GameBuilder.cs; скрипт только
# находит Unity нужной версии, запускает -executeMethod и показывает хвост лога.
# Все профили одного запуска собираются из одного состояния проекта и сверяются
# по отпечатку UXR id — см. Docs/release.md.
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

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

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

# Стартовая платформа — та, с которой начнётся сборка: так на одно переключение меньше.
$first = ($Targets -split '[,;]')[0].Trim().ToLower()
$buildTarget = if ($first -eq 'quest' -or $first -eq 'tablet') { 'Android' } else { 'Win64' }

$logDir = Join-Path $RepoRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null
$log = Join-Path $logDir ('build-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', "`"$RepoRoot`"",
    '-buildTarget', $buildTarget,
    '-executeMethod', 'VrBattlegrounds.EditorTools.Release.GameBuilder.RunBatch',
    '-buildProfile', $Targets,
    '-logFile', "`"$log`""
)
if ($Development) { $Config = 'Test' }
$arguments += @('-config', $Config)

Write-Host "Unity: $Unity"
Write-Host "Профили: $Targets, конфигурация: $Config"
Write-Host "Лог: $log"

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -NoNewWindow -PassThru -Wait
$code = $process.ExitCode

if (Test-Path $log) {
    # Итог сборщика — блок от маркера [GameBuilder] до конца сообщения.
    $lines = Get-Content $log -Encoding UTF8
    $start = ($lines | Select-String -SimpleMatch '[GameBuilder]' | Select-Object -Last 1)
    if ($start) {
        $lines[($start.LineNumber - 1)..([Math]::Min($lines.Count - 1, $start.LineNumber + 15))] | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host 'Итога [GameBuilder] в логе нет — сборка не дошла до конца. Хвост лога:' -ForegroundColor Yellow
        $lines | Select-Object -Last 40 | ForEach-Object { Write-Host $_ }
    }
}

if ($code -eq 0) {
    Write-Host 'Готово. Сборки в Build\Server, Build\Quest, Build\Tablet.' -ForegroundColor Green
    exit 0
}

Write-Host "Unity завершился с кодом $code." -ForegroundColor Red
exit 1
