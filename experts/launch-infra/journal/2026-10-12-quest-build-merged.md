# Сборка пары для шлема с штампом ревизии принята и влита (origin/dev 2c314876)

Этап `quest-perf-pipeline/quest-build`: принят пользователем 2026-10-12, merge-request 15f21560. Проверено: worker 461
(вход d1e3da3b) — пара собрана `StartPairBuild` за 452 с без модального окна (сохранённый ответ стёрт перед сборкой),
`bundleVersion` восстановлен; Quest 3 device-smoke GREEN, ревизии APK/сервер совпадают `d1e3da3ba8fc`.

Находки:
- Модальное «Unsupported Input Handling on Android» читает решение из `SessionState["DialogOptOut.AndroidInputHandlingWarning"]`
  (int DialogResult); публичный `EditorUtility.SetDialogOptOutDecision` пишет другой ключ и не помогает. Зависимость от
  внутренней детали Unity — при обновлении проверить; страховка — детектор модальных окон брокера (infra 4176).
- Штамп ревизии через `bundleVersion` на время сборки виден везде без новых файлов: `Application.version`, versionName APK
  (`dumpsys package` до запуска), E2E-результат, run.json.
- `-Build` (batch Unity) нельзя в агентском worktree (LFS-роль `agent`, ассеты — указатели): признак — `role` из
  `configure_lfs_worktree.py status`; `filter.lfs.process --skip` стоит и в основном checkout, он не признак.

Не проверено: `-Build` в основном checkout, `RunPairBatch` в batch. Дальше по задаче — `perf-scenario` у performance.
