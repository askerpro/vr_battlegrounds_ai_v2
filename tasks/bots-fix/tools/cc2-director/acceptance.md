# cc-2 DirectorEquip — пакет приёмки, 2026-10-15

Короткий case проверен в Unity738, остаётся NeedsReview. Первый lifecycle-пакет747
завершил один полный матч, но не выполнил второй из-за ошибки диагностического
условия roundWon>=4: ничья завершает раунд без RoundWonServer. Исторический пакет
остаётся incomplete. Ниже готовится новый опыт; пользовательская приёмка не выполнена. Это опциональная
диагностика штатного директора; `RunAll` её не запускает. Сценарий P/покупка стенда с
`AutoEquip=false` не доказывает путь Director → Buy → OfferPurchase.

## Отдельный короткий case

Запускать только в своей RUNNING аренде, через существующий PlayLaunch/B4 стенда после
готовности live Respawn. Текущий runner уже вызывает GoLive; дополнительное переключение
live не требуется. Перед запуском чужих ботов быть не должно.

```csharp
var handle = BotCombatStand.RunCases(new BotStandRunOptions
{
    OutputDirectory = outputDirectory,
    Seeds = new[] { 101 },
    CaseSeconds = 15f,
    Capture = false
}, BotStandScenarioId.DirectorEquip);
```

Пространство имён: `VrBattlegrounds.DevTools.BotCombatStand`. `outputDirectory` — абсолютная
папка reports вне Assets. Тело и оружие не подменяются: `Equipment=null`, только Subject с
`AutoEquip=true`, без контрольной цели. Observer подписан до AddBot. Запрещены timed purchase,
kill/respawn и прочие overrides; неверные опции отклоняются до запуска очереди.

Проверять `summary.json → cases[].metrics.directorEquip`, HTML показывает тот же журнал.
Для проверки subscribe cleanup: после завершения/Cancel/ошибки журнал не должен продолжать
накапливаться. Ошибка, отмена, переполнение и dropped явно означают incomplete.

В `events` сохраняются реальные `contextReset`, `buy` (включая `noPurchase`), `offer` с
accepted/refused и эффективный переход `ArmRequested=false→true`. Последний фиксируется
до/после полного участка OfferPurchase + Arm(null), потому что OfferPurchase сам вызывает
Arm. Повторные идемпотентные Arm(null) не считаются переходами или спавнами.

## Непрерывная lifecycle-проба

`BotStandMatchProbeRequest.DirectorEquip=true` включает тот же пассивный журнал; default
false не подписывает observer и не выделяет журнал, чтобы не искажать perf-замеры. Configure
применяет opt-in после OnEnable; выполнить его до AddBot. Создать probe до AddBot и оставить
один экземпляр живым на весь опыт. Он не начинает/завершает матчи, не убивает и не возрождает
игроков. Управление матчем/картой — штатные UI и gameplay-механизмы. Короткий case не доказывает
следующие пункты:

1. Live Respawn: реальные контекст, Buy/Offer, ArmRequested и сетевой инвентарь.
2. Elimination: минимум четыре естественных раунда. По паре `(session, runKey, modeEpoch,
   shopToken)` решение о покупке одно; по `(bodyId, контекст закупки)` Offer не повторяется.
   Сопоставить с transaction/money/phase journal пробы. Призрак не покупает повторно в том же
   раунде; новое живое тело получает допустимый комплект.
3. Второй матч, первый раунд: та же session и тот же mindId, новый modeEpoch/контекст и новое
   решение. Не останавливать probe между матчами: mindId локален одному журналу.
4. Reload той же карты, затем другой: новые runKey, контекст не переносит решение прежнего
   запуска. Смена карты идёт только через `MapLoader.Instance.LoadMap(sceneName)`.
5. Штатные pause/resume: журнал непрерывен; продолжение не создаёт лишних Buy/Offer в том же
   контексте. Time.time и realtime позволяют отличить паузу.
6. Новая жизнь Respawn: новый bodyId/shopToken, тот же session/mindId, новое решение.
7. Естественный отказ OfferPurchase: только реально наблюдавшийся `accepted=false` проверяет
   ветку. Не создавать seam, дополнительный Offer или искусственный invalid descriptor ради
   PASS. Не встретившиеся ветки — `UNKNOWN/notExercised`.

## Что именно доказывает инвентарь

Финальная запись сама по себе не означает завершение окна. Только после выполнения подготовленных
шагов владелец вызывает `BotStandMatchProbeGate.FinishActive("cc2-lifecycle-completed", true)` до
Cancel PlayLaunch. Default второго аргумента false: disable/quit/ошибка/неподтверждённый стоп оставляют
журнал incomplete. Явный completed означает окончание наблюдения, а не PASS всех веток или gameplay.

До AddBot сохраняется baseline. Далее sampled полный набор firearm из `NetworkServer.spawned`:
netId/instanceId, имя/WeaponInfo, текущие Owner actor/body/session, selectedInstanceId,
новые и исчезнувшие netId. Короткий case снимает его на Tick (~0,1 с), match probe — раз в
секунду. Между выборками могут существовать краткоживущие объекты: это не полный spawn trace.

`ownedSpawnedWeaponCount` — число текущих firearm, чей `UxrWeapon.Owner` равен actor Subject.
В непрерывной пробе нет одного Subject: поле null, атрибуция остаётся у каждой строки оружия.
Стартовый пистолет легитимен отдельно от покупки. Owner ещё не захваченного ствола может быть
null; selected не доказывает единственность покупки. Поэтому `onePurchasedSpawn` автоматически
остаётся UNKNOWN; нельзя объявлять PASS по одному SelectedFirearm или числу Arm-вызовов.

Статический путь проверяется отдельно: BotGunner.Arm идемпотентен по `_armRequested`;
OfferPurchase отказывает при уже назначенной/созданной покупке; SpawnPurchase обнуляет
`_armWith` после создания. Это статическое свидетельство, не runtime-доказательство.

## Ограничения и приёмка

Журнал ограничен 8192 событиями, 4096 inventory-выборками, 256 mind и 256 firearm на выборку.
`droppedEvents`, `droppedInventory`, `errors`, `incomplete` должны быть нулевыми/false для
полного наблюдения. `completed=true` означает только штатное закрытие окна наблюдения.
`automaticVerdict=UNKNOWN`, NeedsReview и отсутствие ошибок сами по себе не принимают работу.
После пользовательской проверки обновить существующие ожидания тестов в отдельном
разрешённом шаге этой задачи; до неё новые NUnit не создаются.

## Подготовленный bounded lifecycle-пакет M2

Файлы `b0-preflight.cs`, `b3-runtime-launch.cs`, `b4-runtime-watch.cs`,
`b5-runtime-status.cs`, `b6-runtime-stop.cs` и `package.json` — следующий отдельный опыт.
Корень `tasks/bots-fix/reports/cc2-lifecycle-round2`, run M2, attempt-1,
owner `bots-fix.cc2-lifecycle`. Main компилирует C#6 payload-ы и проводит checkpoint /
request / claim / begin / guard. Подготовка файлов не разрешает их самостоятельный запуск.

Это ServerOnly, 2 бота, 0 людей/клиентов, TestMap1, Elimination, AutoGoLive=true,
MinPlayers=2. Публичного Seed в PlayLaunchConfiguration нет: UNKNOWN/notControlled;
reseed после AddBot не вводится. Одна immutable DirectorEquip=true request до Play,
тот же probe остаётся живым между матчами. Первый матч начинает штатный AutoGoLive,
запасного GoLive нет.

После actual Round>=5 и подтверждения roundScored1..4, включая ничьи, по probe B4
один раз вызывает штатный admin Stop (firstCompletionKind=adminStop). RoundWon
сохраняется только диагностически. Если первый mode штатно завершился раньше,
ветка naturalEnd требует readonly Finished-latch точного referee с key/epoch/mode,
четыре scored раунда и durable matchFinished+modeFinished той же непрерывной probe.
Ожидание обычного flush ограничено120с и общим cap720с. Неизвестная смена mode
не принимается. NaturalEnd не подтверждает проверку admin Stop.
После двух наблюдений готового собственного Warmup descriptor — один GoLive. Каждый
intent пишется CreateNew + Flush(true) до вызова; запись с неизвестным исходом не
переисполняется. Ready нового режима может предшествовать Round1 (штатный Round0);
watcher ждёт Round1 в пределах NoLive=120 с.

Во втором матче B4 ждёт Round>=2 и реальные Round1 Buy/contextReset с прежним mindId
для тех же двух session netId/instanceId. Затем FinishActive(normal,true) до Cancel.
Любой cap=720 с, noLive=120 с, quit, error, dropped или loss приводит к completed=false;
cancel допускается только для собственного owner/RunId. Это функциональный опыт,
его диагностические GC/CPU не подходят для perf-сравнения.

Main удерживает RUNNING lease/guard до cleanup. B5 только читает. B6 не вызывает
force exit, не удаляет ботов, не повторяет неизвестный Cancel и очищает request только
при Idle и точном совпадении с immutable копией. Release: editor не в Play, launch Idle,
CleanupPassed=true, свой request удалён, probe final; затем сразу штатные finish/receive
с полными reports и PlayLaunch log archives. Неизвестный исход — durable files + B5,
без повторной установки B4/запуска B3/автоматического recovery.

Pause/resume, reload той же/другой карты, Respawn new life, refusal, invalid descriptor,
Quest и N8/N10/S8/performance остаются отдельными pending-гейтами. Естественный конец
первого матча наблюдался в747; naturalEnd новой последовательности оценивается отдельно.
