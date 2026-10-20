# Heavy-регрессия

Одна команда перед вливанием: набор e2e-сценариев и один вердикт.

```powershell
python Tools/e2e/heavy.py --profile editor --suite default
```

## Профиль editor

Editor-стенд (MPPM) в worker. heavy о брокере не знает: агент сам держит свою RUNNING аренду
(checkpoint → request → watch-ticket → claim → begin → guard), запускает heavy из своего linked worktree,
затем finish/receive. Unity вызывается через штатный MCP-прокси этого worktree
(`uv run Tools/agents/unity_mcp_proxy.py`); без своей аренды прокси отказывает, heavy возвращает код 2.
MCP SDK heavy ставит сам через `uv run --with mcp`.

Для каждого сценария набора: `PlayLaunch.Plan/Play` (Role=server, ClientCount по сценарию, AutoGoLive=false,
BotCount=0, Readiness=map-playable) → `Status` до Ready → `PlayModeTestStand.Send(run-e2e)` серверу, затем
клиентам → `Status` до E2EFinished → копия `e2e-*.json` → `Cancel` → архивы логов участников.
Повторяются только наблюдения `Status`/`Operation`; Play, Send и Cancel отправляются один раз.

## Вердикт

`Tools/e2e/results/heavy/<время>-<профиль>-<набор>/verdict.json` (вне Git), рядом `calls.jsonl`,
результаты и логи участников. Код возврата:

| Код | Итог | Что делать |
|---|---|---|
| 0 | можно вливать | приложить путь к verdict.json к review-request |
| 1 | регрессия | не вливать; причины — в тексте и verdict.json |
| 2 | вердикт не получен | тоже не вливать: аренда, стенд, MCP; сбой окружения — infra_issue сопровождающему |

Проверки: ветка и код worker содержат игровые изменения свежей origin/dev (`Assets/`, `Packages/`,
`ProjectSettings/`; уход dev только документами, tasks или Tools — предупреждение, перед merge-request всё
равно rebase); Editor свободен (нет Play, компиляции, грязных сцен, prefab stage, чужого запуска).
Если стенд не дошёл до Ready (изредка клон MPPM не подключается к MPE), heavy после подтверждённой
остановки один раз повторяет старт сценария; первая попытка — в `launchRetries` и `failed-launch-1/`.
Падение самого сценария не повторяется. По сценарию — у каждого участника `passed`,
`status=completed` и все checks; `logIssues` против шума сценария и `Tools/e2e/known-noise.json`
(как `E2ELogVerdict.psm1`: неожиданное, `kind=defect` и `logIssuesDropped` роняют); в архивах логов
строки-сообщения `UxrComponentNotFoundException` и `NullReferenceException` роняют, прочие исключения
считаются; нативные падения роняют; стенд должен остановиться после прогона.

## Набор

`Tools/e2e/suites/<имя>.json`: `version`, `description`, `defaults` (`map`, `mode`, `readyTimeoutSeconds`,
`scenarioTimeoutSeconds`) и `scenarios` (`name`, `clients`, `profiles`, переопределения). Неизвестное поле
— ошибка. Editor-стенд держит до 3 клиентов. Новый сценарий в `default` добавляет его автор после
приёмки пользователем. `--scenario <имя>` прогоняет часть набора, `--suite <путь.json>` — свой набор.

## Тесты

Чистые функции (набор, вердикт, свежесть, состояния стенда) — без Unity и MCP; фикстуры — реальные
результаты участников прогонов на worker:

```powershell
python -m unittest discover -s Tools/e2e/heavy/tests -t Tools/e2e
```
