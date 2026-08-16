---
description: Проверить ошибки компиляции и рантайм-логи Unity
---

Проверь состояние проекта в Unity. $ARGUMENTS

## Путь 1 — Unity MCP (предпочтительный)

Если инструменты `mcp__unityMCP__*` доступны — используй их: `read_console` для консоли,
`manage_editor` для состояния редактора. Это единственный способ увидеть рантайм-логи
и Play Mode.

## Путь 2 — Editor.log (когда MCP недоступен)

Unity пишет живой лог редактора. Ошибки компиляции видны там же.

```powershell
$log = "$env:LOCALAPPDATA\Unity\Editor\Editor.log"
Select-String -Path $log -Pattern "error CS|Compilation failed" -Encoding UTF8 |
  Select-Object -Last 30 | ForEach-Object { $_.Line.Trim() }
```

Пусто — код компилируется. Ненулевой вывод — читай `error CS<код>` с путём и строкой.

Когда последний раз компилировалось:

```powershell
Select-String -Path "$env:LOCALAPPDATA\Unity\Editor\Editor.log" -Pattern "CompileScripts" -Encoding UTF8 |
  Select-Object -Last 3 | ForEach-Object { $_.Line.Trim() }
```

Логи импорта ассетов (когда проблема в импорте моделей, шейдеров, префабов) —
`Logs/AssetImportWorker*.log` в корне проекта.

## Чего этот путь не даёт

Editor.log не покажет состояние сцены, иерархию объектов и рантайм-события Play Mode.
Если задача требует этого, а MCP не поднят — не изобретай обходные пути. Сформулируй
пользователю конкретный список того, что нужно посмотреть или сделать в редакторе руками.

## Важно

Unity компилирует при получении фокуса окна. Если правки только что внесены, а Unity
свёрнут — лог ещё старый. Попроси пользователя переключиться в редактор и повтори проверку.
