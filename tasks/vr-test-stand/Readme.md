# Адресный стенд и интеграция Play Launch

Обновлено: 2026-10-10. Владелец: vr-test-stand. Worktree: `F:/CodexWorktrees/vr-test-stand/Vr_Battlegrounds_ai`, ветка `codex/vr-test-stand`; принятый код `51c8c047`, документация `9bcef90d`; текущая база `0c1a7fbc`.

## Цель и границы

Запускать нужные роли в нескольких Play Mode процессах и направлять команды конкретному участнику. Окно человека и MCP используют один planner и владельца request. Сеть и карты ведут штатные владельцы игры; профиль изолирован по checkout.

Принятый маркерный пилот `f42eb741` уже в dev. Текущая интеграция включает coordinator, профиль, adapters, адресный reconnect и потребление согласованной серверной startup-route. Сам маршрут первой сцены реализует map-runtime-bootstrap. XR-ввод, fault injection и recovery смерти координатора остаются отдельными последующими этапами.

## Устройство

PlayLaunch владеет frozen config; PlayModeTestStand — MPE/IPC; GameNetworkDiscovery — сетью. Адрес включает RunId, ParticipantId, ProcessSessionId; reconnect проверяет epoch/avatar. Ready требует свежие heartbeat и фактический допуск карты. NET-21 сохраняется.

Полный анализ: [Details.md](Details.md). Единственный машинный план: [plan.json](plan.json). Контракт [play-launch-control](contracts/play-launch-control.json) ревизии1 active, implementation51c8c047. Инструкции потребителям: [Docs/test-stand.md](../../Docs/test-stand.md), ссылка уже есть в каноническом AGENTS.md.

## План

1. Локальные порты Default/Fixed/Auto и VRBG_LAUNCH_CONFIG — текущая небольшая итерация.
2. Проверить Unity/Android, фактическую пару во всех MPP участниках, reconnect и cleanup на worker; опубликовать инструкции после проверки.
3. Следующая итерация: hardware XR startup; затем программный ввод поз/кнопок/UI/хвата/ходьбы.

SDK patch 58 записывается только в собственный [changelog](changelog/2026-10-08-sdk-58.md). Общие журналы и AGENTS.md не изменяем.

## Проверка и состояние

Предыдущая итерация MERGED. checkout-network-ports RUNNING, revision25/base0c1a7fbc. На375: Fixed29/29 и Auto13/13; на376: native Fixed18/18 и Auto18/18. Negative377 отозван: он принял прежнюю ошибку за новый отказ. После привязки ошибки к RunId worker383 подтвердил новый PortOccupied10/10 и затем свободный Fixed37/37: Server+2Client, адресные команды, reconnect и отдельный ServerOnly Ready. Unity/Android gate PASS, exact baseline/capture/cleanup PASS, finish/receive DONE paths[]. ProtocolHarness32/32, EditorBinding0/0, диагностический helper10/10. Новая retry-ветка живьём не понадобилась. Полный аудит логов383 завершён; политика готова к публикации.

356 на базе6c: Unity/Android gate PASS, managed recovery server10/10/client5/5 PASS. Capturemetrics false исправлены и подтверждены независимым audit; исходные отчёты не переписываем. Аудит полного358 на0071:136rawsegments/4PID/12identities,31,164,419newbytes, issues0; прежние teleport/Life/font/UXR UI/audio ошибки0. Реальны nativeXR8errors, licensing9 и UPM3managederrors. Успех функций не означает error-free/fullsuite/Quest acceptance.

Worker358 fullcandidate5cases56/56PASS. Whole-candidate review без подтверждённых функциональных дефектов. Worker364 на34535: Unity/AndroidPASS, E2Eserver10/10+client5/5, wrapper10/10, capture/cleanupPASS;16,376,170newbytes audited, producterrors0. Native/Bot36533/33PASS с exactbaseline/ownership/capture;4,305,200newbytes audited, producterrors0. ProtocolHarness32/32 и EditorBinding0errors/0warnings повторно PASS на34535. BotT01 выстрелы не проверяет, gameplayPassed=null/NeedsReview по контракту; пользователь принял ограниченную итерацию. SelectedEditMode не подтверждены:365failedinit15s/0started,366WebSocketdisconnectдоjobreceipt; infra2350/2352/2359 у сопровождающего. Эти ограничения сохраняются в принятом пакете, полногоsuite/физическогоtoolbarclick/аппаратнойприёмки не заявляем.

Штатный Monitor с автоматической доставкой вывода недоступен; проверяем inbox/status перед зависимыми действиями. Native routeepoch6 обновлён из живого listing. Cross-task: durable inbox → сигнал проверенному endpoint. Очередь ждать active turn до offered безtimeout, сразуclaim; после конечного пакетаcleanup→finish доанализа.

Пользователь поручил периодически проверять снятие блокировок при активном ожидании; перед idle сообщать сопровождающему и просить native wake. Maintenancecomplete2299 получен через внутренний канал, ACK выполнен; base362 DONE34535cad, native routeepoch8. При wake сначала свежие inbox/status/route и допуск, затем подготовленный пакет. Аренды и очередь не удерживаем вне конечных Unity операций.

## Следующее действие

Опубликовать ограниченную итерацию через hub; evidence reports/ports-self-check.json. Аудит383:99segments/26481794newbytes, неожиданных продуктовых errors0. Warning меню/teamID0/duplicate managers сохранены, без доказанной регрессии; infra configlock передан2992. Rebase и восстановление stash по2919 успешны; stash сохранён. Worker освобождён. Native listing недоступен, маршрут очищен(epoch13); сообщения идут в inbox. NativeXR/UPM/licensing и SelectedEditMode остаются отдельными ограничениями.

Готовые исправления: health restore после spawn/ActiveAvatar; только живой sender и успешный stateevent отражаются; locomotion публикует автор аватара, включая deferredgate; duplicate UI не просыпается, NetworkManager сохраняет NET20escape; fontLegacyRuntime; один fallbackAudioListener, ServerOnlysilent с восстановлениемpause. Status наблюдает, Editorupdate владеетcleanup; exactempty observations могут ждать45s, мутации не повторяются. Новые gameplay/avatar tests до приёмки не добавлены.

Пользователь разрешил commit/publication после самопроверки; это не доказательство аппаратного теста. Документация портов входит в согласованный checkout-network-ports stage. HardwareXR Off, XR input/simulation, faultinjection и coordinator-death recovery — последующие этапы.

Локальные порты: Default/Fixed/Auto + checkout JSON/VRBG_LAUNCH_CONFIG. Один writer применяет frozen пару перед штатным стартом сети. Детали — Details§14; конфиги вне Git. Разные checkout используют разные пары; provisioning worker принадлежит сопровождающему.


Отчёты: `reports/`, вне Git.
