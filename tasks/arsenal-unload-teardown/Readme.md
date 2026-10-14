# arsenal-unload-teardown — разборка станций арсенала при выгрузке карты

Владелец — эксперт `gameplay`. Обновлено 2026-10-14 (Codex возобновил работу). Машинный план — [plan.json](plan.json),
подробности — [Details.md](Details.md).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `top-level-detach` | влит, принят пользователем 2026-10-14 | origin/dev ec0887cf5 (fix 66ce3afcf, diag 4a66b6680) |
| `accepted-tests` | выполняется в хабе; 8 случаев, compile-only PASS; запуск 640 потерял MCP-ответ, NUnit не подтверждён | новая заявка 653 QUEUED, вход 2bf166035a08 с сохранённым GUID; 640 DONE |

## Что сделано

- Находка manipulation 6071 / launch-infra 6032: на выделенном сервере после перезагрузки карты 88× «Tried to ignore
  collisions … not part of a UnityScene». `ArsenalCompositionHandle.Dispose` отцеплял в корень каждый чужой
  `UxrGrabbableObject`, включая вложенные магазины/Slide, и делал это при выгрузке сцены.
- Исправление: отцеплять только чужие предметы верхнего уровня; явный признак выгрузки от владельца сцены —
  `MapBootstrap.OnDestroy` → `ArsenalStationComposer.ReleaseWithScene(scene)` → `Dispose` ничего не переподвешивает,
  предметы уходят со сценой (сетевые снимает Mirror). `Forget` сверяет станцию по ссылке.
- Постоянная диагностика: через кадр после загрузки карты `MapBootstrap` пишет «[MapBootstrap] <карта>: после загрузки —
  grabbable вне сцены=M, spawned вне карты=N» (Match; Warning при ненулевых).
- Проверки: ревью network ACK 6107, manipulation 6201; launch-infra Run-E2E map-run-startup-route (TestMap1, сервер +
  2 клиента, перезагрузка) — GREEN 6334: 0 вместо 88, 0 «not cleaned up», 0 UxrComponentNotFound, 0 Relay, 0/0.

## Следующий шаг

1. launch-infra выполнил обещание 6367: на input87643327108f с ec0887cf5 в базе — 14/14, strict enforce GREEN exit0,
   полные логи, unexpected/defects/native/dropped=0 (сообщение 6892). Снятие
   `anchored-ignore-collision-prefab` из known-noise.json у владельца ещё не принято/не влито.
2. `accepted-tests`: `Assets/Tests/EditMode/Arsenal/ArsenalCompositionTeardownTests.cs` (+.meta) — вложенный предмет
   не отцепляется отдельно, верхний — отцепляется при живой сцене, при `ReleaseWithScene` — ничего не переподвешивается.
   Установить исход прерванного run_tests через durable TestJob SessionState; затем подтверждённый scoped прогон,
   показ пользователю, вливание. Не повторять неизвестный исход вслепую.

## Ждёт пользователя

- Ничего (этап принят). `accepted-tests` — приёмка после прогона.

## Решения пользователя

- 2026-10-13: «чиним то, что мешает игровой логике и что ждут другие» — задача взята в работу.
- 2026-10-14: «Принимаю, вливай» (AskUserQuestion) — `top-level-detach`, полный вердикт по логам — после вливания.

## Риски и ловушки

- После отцепления при живой сцене у предмета может остаться ссылка на уничтоженный якорь слота (было и до правки).
- `arsenal-generator/acceptance` (отложен) идёт после этого этапа — порядок записан в его plan.json.

## Коммуникация (для Codex)

Inbox gameplay разобран 2026-10-14; устаревший маршрут Claude очищен. Вопросы и приёмка — через
доступный инструмент запроса ввода, решения также записывать в задаче.
