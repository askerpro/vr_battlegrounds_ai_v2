# Карта источников пилота

Обновлено: 2026-10-10. Пути ниже относительны корню репозитория. Не читать все источники при старте: выбрать строку по вопросу. Код подтверждает устройство, свежий хаб — координацию, отчёт конкретного прогона — только его результат.

## Архитектура и границы

| Сущность | Ответственность и связь | Основной источник |
|---|---|---|
| PlayLaunch | Планирование и один владелец request/frozen config; вызывает общий backend | `Assets/Editor/VR_Battlegrounds/Testing/PlayLaunch.cs` |
| Локальный профиль | Постоянный checkout JSON, разовый override, восстановление baseline; профиль не меняют во время чужого request | `Assets/Scripts/Debug/Bootstrap/PlayLaunchConfiguration.cs`, `PlayLaunchProfileStore.cs`; `Assets/Editor/VR_Battlegrounds/Debug/Bootstrap/PlayLaunchSettings.cs` |
| Окно пользователя | Использует тот же PlayLaunch; не второй launcher | `Assets/Editor/VR_Battlegrounds/Debug/PlayLaunchWindow.cs` |
| MMP backend | Процессы/IPC/manifest, heartbeat, адресные requests и cleanup | `Assets/Editor/VR_Battlegrounds/Testing/PlayModeTestStand.cs`, `PlayModeStandParticipant.cs`, `StandManifest.cs`, `StandProtocol.cs` |
| Адрес и протокол | RunId + ParticipantId + ProcessSessionId; сетевые команды также epoch/avatar, повтор RequestId не повторяет действие | `tasks/vr-test-stand/contracts/play-launch-control.json`, `Tools/TestStand/Program.cs` |
| Пара портов | Один Editor writer применяет frozen пару до штатного старта сети; ошибка ограничена RunId | `Assets/Editor/VR_Battlegrounds/Testing/PlayLaunchNetworkPorts.cs` |
| Сетевой lifecycle | GameNetworkDiscovery начинает сеть/disconnect/reconnect; Mirror владеет online/offlineScene | `Assets/Scripts/Network/GameNetworkDiscovery.cs`, `GameNetworkManager.cs`; `Docs/session-architecture.md` |
| Карта | Начальная сцена через ServerStartupRoute; live map через MapLoader; Ready учитывает admission/run key | `tasks/map-runtime-bootstrap/Readme.md`, `Assets/Scripts/Debug/Bootstrap/DebugOrchestrator.cs`; `Docs/game-manager.md` |
| Runtime E2E | Один E2ERunner, role-specific IE2EScenario и машиночитаемый результат; запуск сети остаётся у штатных владельцев | `Assets/Scripts/Debug/E2E/E2ERunner.cs`, `IE2EScenario.cs`, `E2EContext.cs`, `E2EResult.cs` |
| Сборки плеера | Существующий CLI-дирижёр собранных процессов; его lifecycle не считать уже унифицированным с MMP | `Tools/e2e/Run-E2E.ps1`, `Docs/testing.md` |
| Звук | ServerOnly без звука; host/client с fallback до появления listener локального аватара; cleanup восстанавливает состояние | `Assets/Scripts/Managers/LocalAudioListenerOwner.cs`; `Docs/test-stand.md`, раздел «Завершение и звук» |
| XR-input | Предстоящий адаптер, не существующая реализация; нельзя добавлять второго писателя UXR input/Transform/grab-state | `tasks/vr-test-stand/Details.md`, разделы5/7/14; `Docs/test-stand.md`, «Границы текущей итерации» |
| Broker/proxy/координация | Внешний владелец agent-coordination-protocol/agent-infra; stage admission не Unity lease | `.agents/rules/agent_coordination.md`; внешние инструкции `F:/UnityProjects/agent-infra/docs/editor-broker.md` и `unity-mcp-proxy.md` только при работе с ними |
| Игровые механики | Оружие, хват, экономика/арсенал и матч остаются у профильных владельцев; E2E согласует наблюдения и expected results | `Docs/weapons/`, `tasks/haptics-system/expert/`, `Docs/Arsenal/`, `Docs/gameplay.md` |

## Где искать подробности

- Использование: `Docs/test-stand.md` — профиль, порты, адресные команды, cleanup и границы.
- История решений: `tasks/vr-test-stand/Details.md` §§4–5 — конфликты/архитектура; §13 — принятая итерация; §14 и «Финальная проверка383» — порты и отозванный377. Файл большой: искать заголовок/номер прогона и читать только совпавший раздел.
- Основной `tasks/vr-test-stand/Readme.md` содержит исторические формулировки RUNNING/«готово к публикации» рядом с уже merged этапами. Не переносить их как свежий статус. Проверить snapshot и хаб; миграция должна выявлять такие противоречия.
- `implementation-plan.md` — исторический план; checkbox не заменяет состояние хаба.
- `integration-analysis.md` — указатель на Details.md, не отдельная копия анализа.
- `tasks/vr-test-stand/contracts/play-launch-control.json` — сохранённая спецификация; revision/state/ACK проверяются свежим хабом.
- `tasks/vr-test-stand/changelog/` — собственные SDK-записи; общие SDK-журналы не обновлять в миграции.

## Evidence и его доступность

Generated reports не входят в Git. Их локальный корень у исходного владельца:
`F:/CodexWorktrees/vr-test-stand/Vr_Battlegrounds_ai/tasks/vr-test-stand/reports/`.

Ключевые файлы: `ports-self-check.json`, `ports-busy-383.json`, `ports-after-busy-383.json`, `ports-final-review.md`, `ports-383-audit/findings.md`, соответствующие `*-logs/`. Старый `ports-busy-377.json` сохраняется, acceptance отозвана.

Новая рабочая копия не обязана иметь эти файлы. Если пути недоступны: показать evidenceUnavailable; использовать committed Details.md как историческое описание, не заявлять независимую повторную проверку raw logs. Не force-add/copy все traces в Git или стартовый пакет. Дополнительные материалы запрашивать у владельца задачи через хаб.

Исторические tooling checks: ProtocolHarness32/32, EditorBinding0errors/0warnings. Проверки диагностического retry helper10/10 находятся в локальных reports; live383 не вызвал новую disconnect-retry ветку. Прогон тестов инфраструктуры рабочих зон оформляется как отдельное новое evidence.
