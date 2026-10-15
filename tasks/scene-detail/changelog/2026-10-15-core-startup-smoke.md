# Загрузка SceneDetail в настоящем Play

По запросу UI7937 проверена готовность provider к accepted release. Приёмки core нет;
статус и оставшиеся проверки переданы UI7941. Приёмка EditMode714PNG относится к UI.

В основном Editor24024, Lobby clean/EditMode, выполнен PlayLaunch.Plan→Play→Status.
Первый Plan отклонил неверный readiness network-started без запуска; использован connected.
Run97d0962f340e49e19a31fc730c5ff4a3, owner scene-detail-core-runtime, PC-host,0clients/0bots.
Lobby Ready, Server/Client/Connected=true, SceneDetail Current существует, Live отдельный,
690LOD+361renderer, Debug Snapshot непустой, console errors0.

Свой запуск завершён Cancel: Idle, CleanupPassed=true, ProfileRestored=true,
Current=null, Lobby clean. Worker не использован. Отчёты reports/core-runtime/.

Scope: наблюдение загрузки/уборки. Не проверены действия controls, JSON roundtrip,
визуальное сохранение отсечения, GPU-бюджет и Quest. Новые тесты и accepted commit не создавались.
