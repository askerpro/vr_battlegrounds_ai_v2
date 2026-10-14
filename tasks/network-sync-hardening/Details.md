# Пакет текущей проверки сетевых исправлений

2026-10-14, network / Codex. Совместная проверка `network-sync-hardening` и `precache-server-guard`.
Код не принят; этот документ готовит действия и освобождение аренды, но не выдаёт допуск.

## Входное состояние и доказательства

- Принятая база после fetch: `d3c62ca5e12d9beeceb57954c8ad751e9690d60d`; HEAD до актуализации документов:
  `edcfca552d01dcc6a46e4f56f66deb51bfe5bf93`, 0 behind / 7 ahead.
- Текущие исправления: precache `59262d43b`, SDK 80 `ce7b348eb`, Mirror 4 `6d66c53e2`.
- Старые SHA до rebase: соответственно `f645d72f9`, `859b06777`, `ccfaf5991`.
- Ограниченный статический обзор: `reports/2026-10-14-resume-audit.md`. Нового подтверждённого дефекта не найдено.
- Свежие проверки: `git diff --check origin/dev..HEAD` — exit 0;
  `check_text_eol.py --repo . --range origin/dev..HEAD` — `ok=true`, `issue_count=0`, `commit_checked=22`,
  два пути пропущены как не подпадающие под LF-policy. Это не компиляция.
- Library, Temp и корневые csproj в этом checkout отсутствуют. Старые офлайн-компиляции и CoreCLR-сравнение 351
  случаев из предыдущей сессии не выдаются за результат текущего дерева, Mono или IL2CPP.
- Объявление 6805 сняло СТОП 6539 и разрешило штатные заявки/ожидание FIFO. Тикет 610 ранее отменён.
  DelayCall-предупреждение само не запрещает admission; текущую готовность проверяют _ready/begin.
- Read-only broker status текущей сессии: Editor PID 96696 жив (heartbeat 0.9 с), active/offered=null,
  blocked/recovery_required=false, modal=false. Но `EDITOR_DELAYCALL_STALLED`: два чтения дали 93.1 и 146.4 с,
  последнее — probes=55/fired=7. Готовность к MCP не подтверждена. Отчёт — `reports/2026-10-14-broker-status.json`;
  проблема передана сопровождающему (6675). Worker самостоятельно не запускался и не восстанавливался.

## Подготовленные действия после объявления готовности

1. Обработать inbox, проверить актуальный mode и условия сопровождающего. Fetch origin/dev; при отставании
   сохранить локальные изменения и выполнить штатный rebase. После rebase снова проверить SHA и собственные LF.
2. Создать технический checkpoint текущего дерева. Через актуальный CLI и новый idempotency key оформить одну
   заявку для двух задач; старый `precache-server-guard-guard-1` не переиспользовать. Далее watch-ticket → claim →
   begin → guard. Проверить worker root, точный SHA, RUNNING lease и маршрутизацию своего MCP-прокси.
3. Дождаться завершения Unity import/компиляции и Mirror weaver; сохранить фактические ошибки и текущий SHA.
4. Выполнить `VrBattlegrounds.EditorTools.AndroidCompileGate.Run()` через execute_code. Вход найден в
   `Assets/Editor/VR_Battlegrounds/Debug/AndroidCompileGate.cs:80`. Сохранить Result.Passed/Errors и маркер
   `[AndroidCompileGate]`; наличие маркера или success инструмента не означает PASS. `RunBatch` не применять
   к worker: этот вход завершает Editor.
5. Запустить существующие EditMode проверки assembly `VrBattlegrounds.Tests.EditMode`: namespace-фильтры
   `VrBattlegrounds.Tests.Network` и `VrBattlegrounds.Tests.Weapons`. Свежую schema run_tests прочитать перед
   вызовом; ждать завершения get_test_job, сохранить totals, failed, ignored и конкретные исключения.
6. Не менять тесты, сцены и игровые настройки ради зелёного результата. Полные отчёты сохранять в reports/;
   в контекст возвращать passed, счётчики, до 10 примеров и пути. Использовать штатный compact-result.
7. Дождаться завершения всех активных операций, убрать только свои временные изменения и сразу finish → receive,
   включая случай проваленной проверки. Диагностика, следующая правка и ожидание человека — после finish.

Подготовленный пакет заканчивается компиляцией, Android gate и существующими EditMode тестами. Play/runtime и
физическая приёмка выполняются отдельно после анализа результата, с новым подготовленным пакетом и арендами.

## Существующее покрытие и приёмка

Узкий перечень из статического аудита — 25 случаев по исходникам, не результат Test Runner:
`HeadlessPrecacheGuardTests`, `DespawnedObjectEventFilterTests`, `AvatarStateEventGateTests`,
`NetworkStateRelayTests`, `AuthorityRequestForDestroyedTests` (namespace Network), `LedgerSpawnReadyTests`
(namespace Weapons). Общий запуск Network дополнительно включает `PlayerCalibrationOwnerTests` и guards
`RpcCarriesNoStateTests`, `StateSnapshotCoverageTests`, `RemoteCallHashTests`.

Приёмка precache: сервер с графикой + 8 ботов — нет роста копий прогрева и памяти; host/client сохраняют прогрев.
Если один UxrManager переживает смену server-only → host/client, проверить возврат штатного прогрева: текущий
guard пишет false и не восстанавливает true. Такой жизненный цикл менеджера пока не подтверждён; это условный
риск, не установленный дефект.

Приёмка Mirror: dedicated-server-arsenal и смена тела/скина host+client, server+client — 0 ошибок
`UpdateTeleportState`/`UxrComponentNotFoundException`. Проверить local/remote avatar, scene-object disable/reset
и custom unspawn/pooling, где используются: callbacks остановки ещё видят spawned, callbacks уничтожения уже нет.

Приёмка SDK 80: Development/Release плеер без DebugMode — нет per-event логов UXR; DebugMode или
`-uxr-log-verbose` возвращает исходные уровни. Выстрел, хват, перезарядка и поздний вход проходят на Quest.
Editor-проверка не доказывает установку политики в плеере или IL2CPP-поведение кеша.

## Тесты после пользовательской проверки

Существующие assert двухаргументного ShouldPrecache остаются корректными для rendering-only helper, но описание
`HeadlessPrecacheGuardTests` устарело и нет server-only/sceneLoaded/composition покрытия.
`DespawnedObjectEventFilterTests` моделирует server spawned.Remove вручную; не проверяет реальный client Destroy.
Добавить матрицу холодного/тёплого кеша, перегрузок/null/наследования и ошибок рефлексии; лог-политику 10 категорий;
StateSyncFrameStack, NetworkTransformKeyframe, UniqueIdStaleRegistry — в согласованном regression-tests этапе.
До приёмки пользователя новые тесты и изменения ожиданий не выполняются.
