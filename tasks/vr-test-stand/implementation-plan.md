# Play Launch и адресный стенд — план реализации

Дата: 2026-10-08. Пользователь поручил реализацию после анализа.
Spec: [анализ и контракт](integration-analysis.md).
Исторический план и принятые решения; актуальный статус ведётся только в [Readme.md](Readme.md).
Worktree: `F:/CodexWorktrees/vr-test-stand/Vr_Battlegrounds_ai`, ветка `codex/vr-test-stand`.
База: `2c845870`. Выполнение inline по executing-plans, одно независимое ревью всей ветки в конце.

## Ограничения и решения

- Один владелец запуска, прежний StandRequestGate/IPC сохраняются. Legacy adapters не исполняются параллельно новому владельцу.
- Профиль только checkout UserSettings; агент использует request, не пишет профиль. Нет записи EditorPrefs/ProjectSettings в runtime-прогоне.
- Offline→Lobby→target сохраняется. NET-21 и маршруты production не меняются; skip Lobby/XR/fault injection — отдельные последующие решения из анализа.
- Gameplay readiness только по MapRunAuthority/MapRunAdmission. Editor/runtime сборки не смешиваются.
- SDK process override паузы — документированный patch; существующие серийные и E2E bootstrap writers адаптируются без обхода владельца.
- Постоянные инфраструктурные тесты допустимы; изменённая игровая логика до пользовательской проверки не закрепляется игровыми тестами.
- До пользовательского принятия нет обычных commit/push. Unity только через собственную аренду; finish до анализа.

## Пакеты

- [x] 1. Pure config/profile/request owner, инфраструктурный RED→GREEN: `Assets/Scripts/Debug/Bootstrap/PlayLaunchConfiguration.cs`, `PlayLaunchProfileStore.cs`, `Tools/TestStand/LaunchConfigurationTests.cs`.
- [ ] 2. Checkout settings и process focus override: `DebugBootstrapSettings.cs`, `EditorFocusPauseMenu.cs`, `UxrManager.cs`, `Docs/UltimateXR/sdk-patches.md`; чтение legacy только явным импортом человека.
- [ ] 3. Unified coordinator/facade и immutable participant plans: `Assets/Editor/VR_Battlegrounds/Testing/PlayLaunch.cs`, существующие `StandManifest`, `PlayModeTestStand`, `PlayModeStandParticipant`. Старый StartProbe — adapter.
- [ ] 4. Planner/window/legacy startup adapter: `PlayModeStartFromOffline`, `DebugBootstrapWindow`, `DebugOrchestrator`, новый scene marker/classifier. Запуск окна и API используют один plan.
- [ ] 5. Bot/standalone/build adapters: `BotCombatStandEditor`, сборщики/scene resolver; устранить временные edits Build Settings/registry; миграции сцены/каталога только подготовленным пакетом worker.
- [ ] 6. Адресный reconnect и E2E bridge: `GameNetworkDiscovery`, `E2EContext/E2ERunner`, `SessionRecoveryOnReconnectScenario`; единый lifecycle, стабильная identity, epochs и отказы старым адресам.
- [ ] 7. Проверка: local compile/harness, own broker worker Android gate, 3 процесса, isolation/restore, target readiness/reconnect, ordinary Play, no tracked changes; finish/receive.
- [ ] 8. Документы/AGENTS и независимое ревью; результат на пользовательское принятие.

## Риски ревью

EditorPrefs cache другого редактора; domain reload baseline и старый адрес; дубли сетевого bootstrap;
Ready от старой карты; конфигурация дочерних процессов; таймаут с частичным эффектом; scope cleanup
после исключения; build scenes fixtures не попадают в release; параллельный чужой request не стирается.

## Ledger

- 2026-10-08: все замечания P1/P2 закрыты повторным статическим ревью. Локально harness31/31,
  EditorBinding обычный и VRBG_NO_E2E: 0 warnings/errors, diff-check без ошибок. Это не Unity PASS.
  Полный план зарегистрирован vr-test-stand revision1, UXR58 зарезервирован, overlap отправлен владельцу.
  Worker приостановлен обслуживанием координации; до объявления завершения его не трогаем.

- Ruling: toolbar Play с явным источником допускает только main без дополнительных native editors и ClientCount=0; многопроцессный запуск выполняется общим PlayLaunch backend. Это исключает детей без frozen snapshot; native Cancel удерживает request до возврата в EditMode. Ограничение снимается только при переводе native topology на общий descriptor, не отдельной копией профиля.

- 2026-10-08: ревью выявило несовместимых writers focus, отсутствие VRBG_NO_E2E guards, слишком широкий E2E bridge, устаревающий Ready и игнорирование явной карты внешнего клиента/native Play. Заявка 208 отменена до аренды: её input не содержит исправлений.
- Ruling: managed launch владеет focus; puppet/clip стенды берут свой scope только при обычном запуске. E2E bridge допускает только session-recovery-on-reconnect до адаптации остальных сценариев. Ready требует heartbeat не старше 5 секунд и проверяет явно выбранную сцену даже на внешнем сервере.

- 2026-10-08: worktree обновлён до 2c845870. Новые принятые weapon commits не пересекаются с launch.
- Ruling: выполняем этапы интеграции до единого reconnect; skip Lobby и XR не входят в этот implementation, как согласовано в анализе.
- Ruling: сохраняем совместимые public Bootstrap setters для старых редакторских потребителей, но storage переводится на checkout. Активный request защищён от profile writers.
- Ruling: forced reload проверяется отдельно, не переключением EditorSettings в обычной пробе.
- 2026-10-08: configuration/store RED (22 pass/1 fail отсутствующий контракт) → GREEN (30 pass), затем Client+Host topology/deep copy. EditorBinding compile 0 warnings/errors до network additions; реальная Unity-проверка впереди.
- В работе: SDK patch 58, checkout adapter, общий facade/window, managed legacy driver, адресный reconnect с connection/avatar epoch.
