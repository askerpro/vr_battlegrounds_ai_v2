# e2e-heavy-editor: heavy-регрессия перед вливанием, 2026-10-20

`python Tools/e2e/heavy.py --profile editor --suite default [--editmode also|only]` — набор e2e-сценариев на
Editor-стенде и один вердикт: `Tools/e2e/results/heavy/<время>/verdict.json`, код 0 — можно вливать, 1 — регрессия,
2 — вердикта нет. Раздел AGENTS.md «Heavy-регрессия перед вливанием», описание — `Tools/e2e/heavy/README.md`.
Влито: итерация 1 — 911f93b37; итерация 2 (EditMode, замечания ревью) — этап `it2`.

Инварианты:
- heavy не знает о брокере: агент держит свою аренду, Unity — через штатный MCP-прокси своего worktree.
  Только фасад PlayLaunch.Plan/Play/Status/Cancel и PlayModeTestStand.Send/Operation; Editor-код не меняется.
  Повторяются только наблюдения (Status, Operation, get_test_job); Play, Send, Cancel, run_tests — один раз.
- Остановленный стенд: State Idle или Failed (свой LaunchNotReady залипает до следующего Acquire), нет Play и
  владельца; heartbeat клонов после остановки устаревает с playing=true — живым считается только свежий.
- Один повтор старта, если стенд не дошёл до Ready и подтверждённо остановлен (клон MPPM изредка не
  подключается к MPE, infra_issue 10300); падение сценария не повторяется.
- Свежесть — только игровые пути (`Assets/`, `Packages/`, `ProjectSettings/`) origin/dev; код worker сверяется
  с текущими файлами ветки снимком через копию индекса.
- Логи: строки-сообщения `UxrComponentNotFoundException`/`NullReferenceException` в архивах участников роняют;
  имена типов в стековых кадрах не считаются. logIssues — как `E2ELogVerdict.psm1`.
- EditMode: полный прогон через run_tests; итоговый `result.results` get_test_job с include_failed_tests содержит
  все не прошедшие (лимит 25 — только у прогресса); распаковывать только конверт ответа, у job своё поле result.
  Новые красные — против `Tools/e2e/editmode-known-red.json` (35 на 2d2e7becf).
- Новый сценарий в `Tools/e2e/suites/default.json` добавляет его автор после приёмки пользователем.

Проверено на worker: аренды 863–894; default из трёх сценариев + EditMode — код 0 за 14 мин; негатив — код 1;
повтор старта — код 2. Unittest `Tools/e2e/heavy/tests` 51/51.
