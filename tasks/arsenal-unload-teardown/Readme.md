# arsenal-unload-teardown — разборка станций арсенала при выгрузке карты

Владелец — эксперт `gameplay`. Обновлено 2026-10-15. Машинный план — [plan.json](plan.json),
подробности — [Details.md](Details.md).

## Статус

| Этап | Состояние | Где |
|---|---|---|
| `top-level-detach` | влит, принят пользователем 2026-10-14 | origin/dev ec0887cf5 (fix 66ce3afcf, diag 4a66b6680) |
| `accepted-tests` | код принят пользователем 2026-10-15; Unity EditMode 8/8 PASS | 668 DONE, job c47d9b1953e24feaafbb0eaea853438e; result faf8227276fc, diff пустой |

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
   Scoped прогон выполнен в новой аренде668 после worker_ready7257: 8/8 PASS, skipped0; до/после
   managers/live/previews0, setup[]. Тестовый код принят пользователем; выполняется штатное FF-вливание.
   Старые640/653 остаются без подтверждённого NUnit verdict; это не replay неизвестной операции.
3. По infra_status7375 подготовлена [спецификация владельца](contracts/accepted-tests-spec.v1.json):
   источник/мета SHA256,8/8, XML/log outputs и cleanup; immutable checkpoint dc5952dfe6223663f6cacffbd6ec264f305c617f.
   Это подготовка данных к будущему executor, не исполняемый CLI-контракт и не новая заявка/приёмка.

## Ждёт пользователя

- Ничего: 2026-10-15 пользователь принял `accepted-tests` и разрешил FF-публикацию в origin/dev.

## Решения пользователя

- 2026-10-13: «чиним то, что мешает игровой логике и что ждут другие» — задача взята в работу.
- 2026-10-14: «Принимаю, вливай» (AskUserQuestion) — `top-level-detach`, полный вердикт по логам — после вливания.
- 2026-10-15: «Принять и влить (рекомендуется)» — 8 тестов `accepted-tests`, их Unity-generated .meta и документация этапа; тесты каталога не включены.

## Риски и ловушки

- После отцепления при живой сцене у предмета может остаться ссылка на уничтоженный якорь слота (было и до правки).
- `arsenal-generator/acceptance` (отложен) идёт после этого этапа — порядок записан в его plan.json.

## Коммуникация (для Codex)

Inbox gameplay разобран 2026-10-15; собственный маршрут Codex опубликован. Вопросы и приёмка — через
доступный инструмент запроса ввода, решения также записывать в задаче.
