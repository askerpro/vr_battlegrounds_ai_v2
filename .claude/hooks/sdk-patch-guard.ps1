# PostToolUse-хук: правка исходников UltimateXR SDK обязана попасть в sdk-patches.md.
#
# Такие правки теряются при обновлении SDK, если их не записать. Событие редкое,
# последствие дорогое — поэтому хук намеренно шумный.
#
# Отказоустойчивость: любая внутренняя ошибка => exit 0 (молчим, не мешаем работе).

$ErrorActionPreference = 'Stop'

# Файл сохранён в UTF-8 с BOM (иначе PowerShell 5.1 читает его как ANSI и кириллица бьётся),
# а stderr принудительно переводим в UTF-8 — Claude Code читает вывод хука как UTF-8.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

    $payload = $raw | ConvertFrom-Json
    $path = $payload.tool_input.file_path
    if ([string]::IsNullOrWhiteSpace($path)) { exit 0 }

    $normalized = $path -replace '\\', '/'

    # Сам файл учёта патчей трогать можно молча — иначе хук зациклит сам себя.
    if ($normalized -match 'Docs/UltimateXR/sdk-patches\.md$') { exit 0 }

    if ($normalized -match 'Assets/ThirdParty/UltimateXR/') {
        $msg = @"
[SDK GUARD] Правка применена успешно — это не ошибка записи.

Изменён исходник UltimateXR SDK:
  $path

Такие правки стираются при обновлении SDK. Обязательный следующий шаг:
записать изменение в Docs/UltimateXR/sdk-patches.md — что, где и зачем.

Если правка временная или отладочная — откати её вместо документирования.
"@
        [Console]::Error.WriteLine($msg)
        exit 2
    }

    exit 0
}
catch {
    exit 0
}
