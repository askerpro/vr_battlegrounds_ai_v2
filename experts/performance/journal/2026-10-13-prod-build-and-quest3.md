# Prod-сборка Quest в 2,4 раза дольше Test; Quest 3 пока не включаем; Prod — на отдельном checkout

2026-10-13, задача `perf-stress-scenario`, этап `controller-free-launch`.

- Замер (worker, аренда 537, ревизия 5d614df4de60, Ryzen 9 5900X): Test APK Quest 150 с (инкрементально), сервер
  72 с; Prod APK 355 с, от постановки до результата 634 с. Prod-манифест (aapt) без записей рук — ни один пакет их
  сам не пишет; Test-APK — `uses-feature handtracking required=false` + разрешение.
- Решение пользователя: Prod-сборки — отдельно от Test, чтобы не сбрасывать кэш. launch-infra берёт маршрут
  (batch `Build-Game.ps1 -ProjectPath` на отдельном checkout, 5412), размещение и очередь — agent-infra (5408).
- Решение пользователя: Quest 3 (`TargetQuest3`) пока не включаем; предупреждение валидатора остаётся предупреждением.
  Замеры на Quest 3 идут в режиме совместимости Quest 2 — условие каждого замера.
- Решение пользователя: вторая сессия performance в worktree не нужна.
