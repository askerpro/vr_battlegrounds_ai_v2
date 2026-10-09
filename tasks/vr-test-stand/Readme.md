# Адресный стенд и интеграция Play Launch

Обновлено: 2026-10-09. Владелец: vr-test-stand. Worktree: `F:/CodexWorktrees/vr-test-stand/Vr_Battlegrounds_ai`, ветка `codex/vr-test-stand`; база после rebase — `34535cad`.

## Цель и границы

Запускать нужные роли в нескольких Play Mode процессах и направлять команды конкретному участнику. Окно человека и MCP используют один planner и владельца request. Сеть и карты ведут штатные владельцы игры; профиль изолирован по checkout.

Принятый маркерный пилот `f42eb741` уже в dev. Текущая интеграция включает coordinator, профиль, adapters, адресный reconnect и потребление согласованной серверной startup-route. Сам маршрут первой сцены реализует map-runtime-bootstrap. XR-ввод, fault injection и recovery смерти координатора остаются отдельными последующими этапами.

## Устройство

PlayLaunch владеет frozen config; PlayModeTestStand — MPE/IPC; GameNetworkDiscovery — сетью. Адрес включает RunId, ParticipantId, ProcessSessionId; reconnect проверяет epoch/avatar. Ready требует свежие heartbeat и фактический допуск карты. NET-21 сохраняется.

Полный анализ: [Details.md](Details.md). Единственный машинный план: [plan.json](plan.json). Контракт: [play-launch-control](contracts/play-launch-control.json); ревизия 1 согласована владельцем, ещё не active.

## План

1. Завершить Play Launch; отдельным пунктом этого пакета поддержать долгоживущий Tools/TestStand и проверить RED→GREEN типовые привязки.
2. Проверить действующие процессы, startup route, reconnect и очистку на worker.
3. После принятого вливания публиковать инструкции в Docs/test-stand.md; владелец области — vr-test-stand, сопровождавший принятый пилот.

SDK patch 58 записывается только в собственный [changelog](changelog/2026-10-08-sdk-58.md). Общие журналы и AGENTS.md не изменяем.

## Проверка и состояние

База34535cad; планrevision20, допускepoch47 RUNNING. Оба предшественника влиты. Rebase без конфликтов; backup32606e9a сохраняет74файла,74/74совпали по хешам перед обновлением metadata. Launcher чистый. Worker363–366 finish/receive DONE, последний result557b47c0 paths[], binaryinvalid[]. Пользователь 2026-10-09 явно принял текущую итерацию и разрешил commit/publication после самопроверки; самопроверка функционального пакета завершена, публикация выполняется.

356 на базе6c: Unity/Android gate PASS, managed recovery server10/10/client5/5 PASS. Capturemetrics false исправлены и подтверждены независимым audit; исходные отчёты не переписываем. Аудит полного358 на0071:136rawsegments/4PID/12identities,31,164,419newbytes, issues0; прежние teleport/Life/font/UXR UI/audio ошибки0. Реальны nativeXR8errors, licensing9 и UPM3managederrors. Успех функций не означает error-free/fullsuite/Quest acceptance.

Worker358 fullcandidate5cases56/56PASS. Whole-candidate review без подтверждённых функциональных дефектов. Worker364 на34535: Unity/AndroidPASS, E2Eserver10/10+client5/5, wrapper10/10, capture/cleanupPASS;16,376,170newbytes audited, producterrors0. Native/Bot36533/33PASS с exactbaseline/ownership/capture;4,305,200newbytes audited, producterrors0. ProtocolHarness32/32 и EditorBinding0errors/0warnings повторно PASS на34535. BotT01 выстрелы не проверяет, gameplayPassed=null/NeedsReview по контракту; пользователь принял ограниченную итерацию. SelectedEditMode не подтверждены:365failedinit15s/0started,366WebSocketdisconnectдоjobreceipt; infra2350/2352/2359 у сопровождающего. Эти ограничения сохраняются в принятом пакете, полногоsuite/физическогоtoolbarclick/аппаратнойприёмки не заявляем.

Штатный Monitor с автоматической доставкой вывода недоступен; проверяем inbox/status перед зависимыми действиями. Native routeepoch6 обновлён из живого listing. Cross-task: durable inbox → сигнал проверенному endpoint. Очередь ждать active turn до offered безtimeout, сразуclaim; после конечного пакетаcleanup→finish доанализа.

Пользователь поручил периодически проверять снятие блокировок при активном ожидании; перед idle сообщать сопровождающему и просить native wake. Maintenancecomplete2299 получен через внутренний канал, ACK выполнен; base362 DONE34535cad, native routeepoch8. При wake сначала свежие inbox/status/route и допуск, затем подготовленный пакет. Аренды и очередь не удерживаем вне конечных Unity операций.

## Следующее действие

Коммит и публикация принятого функционального пакета по hub, затем инструкции Docs/test-stand.md отдельным docs-publication stage. NativeXR/UPM/licensing startup и baselinewarnings сохраняются в реестре следующей итерации; инфраструктурные EditModestartup/transport2350/2352 расследует сопровождающий. Ошибки не скрываем и не объявляем исправленными.

Готовые исправления: health restore после spawn/ActiveAvatar; только живой sender и успешный stateevent отражаются; locomotion публикует автор аватара, включая deferredgate; duplicate UI не просыпается, NetworkManager сохраняет NET20escape; fontLegacyRuntime; один fallbackAudioListener, ServerOnlysilent с восстановлениемpause. Status наблюдает, Editorupdate владеетcleanup; exactempty observations могут ждать45s, мутации не повторяются. Новые gameplay/avatar tests до приёмки не добавлены.

Приёмка текущей итерации подтверждена прямым сообщением пользователя; это разрешение на commit/publication, а не доказательство аппаратного теста. Документация продукта отдельным docs-publication stage после вливания. HardwareXR Off, XR input/simulation, локальные port overrides, faultinjection и coordinator-death recovery — последующие этапы.

Локальные порты: ownedserver уже получает свободную пару network/discovery. Предложение Default/Fixed/Auto + checkoutlocal JSON/VRBG_LAUNCH_CONFIG сохранено в Details§12, фиксированные overrides ещё не реализованы; конфиги должны оставаться вне Git.


Отчёты: `reports/`, вне Git.
