# Автоматический перф-прогон на шлеме Quest

Обновлено: 2026-10-12. Владелец: эксперт `launch-infra`. Worktree: `F:/CodexWorktrees/launch-infra/Vr_Battlegrounds_ai`,
ветка `codex/vr-test-stand`. Поручение пользователя 2026-10-11.

## Цель

Одна команда без участия человека: собрать Android-билд → установить на шлем через adb → запустить перф-тест →
дождаться конца → закрыть приложение → вытащить логи и перф-результаты в папку прогона → дать машиночитаемый итог.

## Границы

- launch-infra: конвейер (дирижёр), сборка, установка, запуск, параметры запуска плеера, сбор артефактов, вердикт
  «прогон состоялся / нет».
- performance: содержание замеров (стресс-тест, сцены, метрики), формат перф-результатов, бюджеты и оценка «хорошо/плохо».
  Стык — контракт формата результатов и параметров запуска перф-теста.
- Не трогаем: сетевую логику, содержание сцен, бюджеты.

## План

1. `design` — merged `ef477dc2`.
2. `device-runner` — merged `1fc34c84`: `Tools/e2e/Run-QuestE2E.ps1`, шлем-клиент + ПК-сервер, контракт
   `quest-device-run` r3; Quest 3 device-smoke GREEN.
3. `quest-build` — merged `2c314876`: `StartPairBuild`/`RunPairBatch`, `-Build`, штамп ревизии `0.1.0+<sha>`,
   сверка ревизий APK/сервер, подавление модального окна Input Handling.
4. `e2e-views` — merged `68950d0b`: `E2EViews` (точки обзора через `MoveAvatarTo`), `-ViewsFile`, LFS-проверка `-Build`.
5. `quest-vr-manifest` — merged `92d7cee8`: Quest-сборки снова VR (OculusManifest под `#if UNITY_ANDROID`), проверки манифеста.
6. `e2e-head-pose` — merged `a311cdb1`: симуляция позы головы `-HeadPose`.
7. Запуск без контроллеров — сделал performance (`perf-stress-scenario/controller-free-launch`, влит `3cabcc1a`).
8. `e2e-log-verdict` — принят пользователем 2026-10-13: `E2ELogCapture` в каждом процессе, `Tools/e2e/E2ELogVerdict.psm1`,
   `Tools/e2e/known-noise.json` (шум и известные дефекты `kind=defect`, прогон с дефектом остаётся red), `-LogVerdict`.
9. Следующий: `e2e-smoke-kit` — каркас для смок-теста серии gameplay (барьер шагов, роль процесса, журнал шагов,
   `-Arg` в Run-E2E, `VRBG_NO_E2E` в Prod); проект и условия network/gameplay — Details.md.
10. performance: задача `perf-stress-scenario` (scenario → arena-layout → view-tour); запрос lobby-decoration 4449.

Подробности: [Details.md](Details.md). Машинный план: [plan.json](plan.json). Отчёты — `reports/`, вне Git.
