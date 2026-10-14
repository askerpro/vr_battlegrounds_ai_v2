# arsenal-unload-teardown — разборка станций арсенала при выгрузке карты

Владелец — эксперт `gameplay`. Обновлено 2026-10-14 (передача Claude → Codex). Машинный план — [plan.json](plan.json),
подробности — [Details.md](Details.md).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `top-level-detach` | влит, принят пользователем 2026-10-14 | origin/dev ec0887cf5 (fix 66ce3afcf, diag 4a66b6680) |
| `accepted-tests` | не начат | после `top-level-detach ≥ merged` (выполнено) |

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

1. Дождаться штатного прогона launch-infra map-run-startup-route с полным вердиктом по логам на базе после вливания
   (запрошен 6367) и снятия дефекта `anchored-ignore-collision-prefab` из `Tools/e2e/known-noise.json`. Если Warning
   счётчика ненулевой — разобрать (возможен ложный N>0, если сетевые объекты сервера штатно живут вне сцены карты и
   DontDestroyOnLoad).
2. `accepted-tests`: `Assets/Tests/EditMode/Arsenal/ArsenalCompositionTeardownTests.cs` (+.meta) — вложенный предмет
   не отцепляется отдельно, верхний — отцепляется при живой сцене, при `ReleaseWithScene` — ничего не переподвешивается.
   Затем прогон в аренде, показ пользователю, вливание.

## Ждёт пользователя

- Ничего (этап принят). `accepted-tests` — приёмка после прогона.

## Решения пользователя

- 2026-10-13: «чиним то, что мешает игровой логике и что ждут другие» — задача взята в работу.
- 2026-10-14: «Принимаю, вливай» (AskUserQuestion) — `top-level-detach`, полный вердикт по логам — после вливания.

## Риски и ловушки

- После отцепления при живой сцене у предмета может остаться ссылка на уничтоженный якорь слота (было и до правки).
- `arsenal-generator/acceptance` (отложен) идёт после этого этапа — порядок записан в его plan.json.

## Коммуникация (для Codex)

Inbox: `python Tools/agents/coordination.py inbox --owner gameplay --unread --as "<свой адрес>"`; ожидание —
`wait-inbox --owner gameplay --as "<свой адрес>"` фоновой командой. Вопросы пользователю — сообщением «ЖДЁТ ОТВЕТА».
