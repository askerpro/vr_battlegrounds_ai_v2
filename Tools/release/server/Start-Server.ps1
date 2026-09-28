# Запуск выделенного сервера с логом в файл.
#
# Сборщик (GameBuilder) кладёт этот скрипт рядом с VrBattlegroundsServer.exe.
# Лог пишет сам Unity (-logFile): в UTF-8, со всеми сообщениями движка. Скрипт
# одновременно показывает его в консоли и хранит последние $Keep логов в Logs\.
#
# Запуск:
#   powershell -ExecutionPolicy Bypass -File Start-Server.ps1 [доп. аргументы сервера]
#   или двойной клик по Start-Server.cmd

param(
    # Сколько последних логов хранить.
    [int] $Keep = 20
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}

$exe = Join-Path $PSScriptRoot 'VrBattlegroundsServer.exe'
$logDir = Join-Path $PSScriptRoot 'Logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

Get-ChildItem $logDir -Filter 'server-*.log' | Sort-Object LastWriteTime -Descending |
    Select-Object -Skip ([Math]::Max(0, $Keep - 1)) | Remove-Item -Force -ErrorAction SilentlyContinue

$log = Join-Path $logDir ('server-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
Write-Host "Лог: $log"

$arguments = @('-logFile', "`"$log`"") + $args
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -NoNewWindow -PassThru
$null = $process.Handle  # без этого ExitCode после выхода бывает пустым

# Показываем лог, пока сервер жив. Файл открыт Unity на запись — читаем с общим доступом.
while (-not (Test-Path $log) -and -not $process.HasExited) { Start-Sleep -Milliseconds 200 }

if (Test-Path $log) {
    $stream = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    try {
        while ($true) {
            $line = $reader.ReadLine()
            if ($line -ne $null) { Write-Host $line; continue }
            if ($process.HasExited) { break }
            Start-Sleep -Milliseconds 200
        }
    } finally {
        $reader.Close()
    }
}

Write-Host "Сервер завершился с кодом $($process.ExitCode). Лог: $log"
exit $process.ExitCode
