# Use context: план реализации и бриф исполнителю

Дата: 2026-10-10. Задача: `weapon-system`; владелец: `weapon-system-expert` (Codex).

> Для исполнителя: выполнять план по задачам с `superpowers:executing-plans`; основной агент
> делегирует срез и проверяет доказательства. Канонические правила — `AGENTS.md`.
> Этот документ разрешает подготовку; продуктовая запись требует отдельного допуска `use-context`.

**Цель:** вооружённый NPC действительно готовит и использует тот же огнестрел через WeaponSystem,
без SDK-руки; VR-пользователь продолжает использовать свой штатный хват.

**Архитектура:** один resolver выбирает VR-руку или read-only equipment binding. Единственный вход
автоматики — `RequestAutomationPreparation`, право ограничено текущим `(Kind, User, Epoch)`.
Машина получает чистые факты контекста; SDK сохраняет учёт и выпуск снаряда, а исполнители — свои каналы.

**Стек:** Unity 6 URP, C#, UltimateXR, Mirror, Quest 2/3.

**Спецификация:** [weapon-use-context@2](contracts/weapon-use-context-2.json), согласованный в хабе обеими сторонами;
история API — [@1](contracts/weapon-use-context-1.json);
[архитектура](../../Docs/weapons/architecture.md), [инварианты](../../Docs/weapons/invariants.md),
[решения](../../Docs/weapons/decisions.md). Запрос bots-fix: события 2085/2185; ответ владельца: 2246.

## Исходное состояние и допуск

- Подготовка зарегистрирована этапом `use-context-planning`: документация и бриф, без Unity и игровых правок.
- В текущем checkout нет `Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs`.
  Исходник есть у bots-fix; это непринятый кандидат, а не разрешение его копировать.
- `weapon-equipment-binding@1` и `weapon-use-context@2` согласованы (`agreed`), binding ещё не `active`.
- План r31 переносит `ejection-manual` после use-context; DEVELOPMENT использует agreed-зависимости.
  SDK70 technically verified; `use-context` RUNNING epoch1. Production acceptance этим не заменяется.
- До продуктовой записи нужен доказанный источник VR grab-lifetime: обычные `ObjectGrabbed/ObjectReleased`
  могут не приходить при `propagateEvents=false`. VR sentinel 0 с такими событиями не закрывает stale grant.
- Bots-fix предоставил exact11 c05eabbfb77ebc68a197593c54d1567ee3ab68da, разрешённый только для
  isolated DEVELOPMENT reports composition. Manifest/base/hashes проверены, binding остаётся у bots-fix.
- Equipment resolver/grant/Core/queue реализован и reviewed, checkpoint exact9 `3ab0beaf`; финальная
  overlay-компиляция exit0 Core/SDK554/game434/DebugBootstrap6/Editor262/Tests231, без запуска тестов.
- Пользователь выбрал SDK lifetime-порт для полного VR grant. SDK71 зарезервирован; конкретный
  [weapon-grab-lifetime@2](contracts/weapon-grab-lifetime-2.json) уточняет source getter0 во время
  незавершённого setter/restore, чтобы callbacks не получали grant по частично восстановленному state.
  План r33 выделяет lifetime planning/SDK стадии перед full-context; owner ACK haptics пока ожидается.
  VR automation в Equipment DEV candidate временно false, это не accepted production поведение.
- У брокера на момент подготовки `active=null`, `blocked=false`, `recovery_required=false`.
  Перед Unity необходимы свежие status/inbox, fetch/rebase принятой `origin/dev` с сохранением чужих изменений,
  технический checkpoint и собственная аренда. Старые результаты компиляции не считаются текущими.
- SDK: для ключа `use-context` через `patch-reserve` получен номер **70**.
  Исторический номер 63 из бэклога не использовать без отдельного подтверждения его прежней резервации.

## Границы и владение файлами

| Файл | Ответственность |
|---|---|
| `Assets/Scripts/Weapons/WeaponUseContext.cs` и `.meta` | Единственный resolver вида использования и пользователя |
| `Assets/Scripts/Weapons/WeaponSystem/Sensors/WeaponContextSensor.cs` | Преобразование resolver в чистый контекст шага, роль/replay/CanUse |
| `Assets/Scripts/Weapons/WeaponSystem/WeaponSystem.cs` | Жизненный цикл права автоматики и событий, запрос подготовки |
| `Assets/Scripts/Weapons/Core/WeaponEvents.cs` | Чистые `HasAuthorContext`, `AutomationAllowed`, сохранение VR-факта |
| `Assets/Scripts/Weapons/Core/WeaponStateMachine.cs` | Контекстные guards существующей таблицы |
| `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/IUxrFirearmRootRecoilPolicy.cs` и `.meta` | Минимальный SDK-порт разрешения отдачи корня |
| `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs` | Потребление SDK-политики только вокруг записи root transform |
| `tasks/weapon-system/changelog/2026-10-10-sdk-70.md` | Журнал фактической SDK-правки после реализации |
| `tasks/weapon-system/reports/use-context-implementation/` | Локальные результаты подготовки/проверок вне Git |

Новая asmdef не нужна: resolver/host — `VrBattlegrounds`, SDK-порт — `UltimateXR`, Core остаётся
`noEngineReferences`. Не менять `Equipment/**`, Bots, haptics, генератор арсенала, аватары, префабы,
общие журналы и индексы. Основной агент единолично пишет `Readme.md`, `Details.md`, `plan.json` и этот план.
Исполнитель не перепривязывает задачу, маршрут или checkout и не ACK-ает контракты от имени основного агента.

## Задача 1. Единый resolver и чистый контекст

Интерфейс потребителя: `WeaponUseContext.Resolve(UxrFirearmWeapon weapon, int trigger)` возвращает
`Kind { None, VrHand, Equipment }`, `UxrAvatar User`, `UxrGrabber Hand`, `bool AuthoredHere`, `int Epoch`.
Вход equipment: root `WeaponEquipmentBinding.IsActive/Owner/OwnerAlive/Epoch` из точной согласованной ревизии.

- [ ] Найти основную рукоять через `TryGetTriggerGrip(trigger)` и SDK `GetGrabbingHand(grip, point)`;
  поддержку не считать самостоятельным пользователем оружия.
- [ ] Проверять одновременный raw SDK-хват и активный equipment claim **до** фильтрации локальности,
  CanUse и живости. Конфликт всегда `None` + `GameLog.WeaponSystem.Error`; повторный спам не допускается.
- [ ] Equipment допустим только при активном binding, разрешённом owner и `OwnerAlive`;
  `Hand=null`, `Epoch=binding.Epoch`, авторство — текущая `StateEventAuthority`.
  При неразрешённом owner контекст `None`, но equipment claim продолжает запрещать competing root recoil.
- [ ] VrHand берёт реальную руку/аватара SDK, `AuthoredHere` — по авторству аватара.
  Каждое release/regrab и смена основной руки отзывает старое право, включая тихие SDK операции
  `propagateEvents=false` в одном кадре. На подготовке подтвердить источник полного grab-lifetime в SDK;
  если его нет, вынести минимальный owner-side SDK adapter на согласование до продуктового допуска.
  Не вводить постоянный epoch или второй локальный lifecycle как замену отсутствующего источника.
- [ ] Добавить `HasAuthorContext` и `AutomationAllowed` в `Core.WeaponContext` без Unity/SDK-типов.
  Старый шестипараметрический constructor сохранить и делегировать полному overload:
  `HasAuthorContext=mainGripLocal`, `AutomationAllowed=isWorldAuthority && mainGripLocal`.
  `MainGripLocal` остаётся фактом физической VR-руки.
- [ ] В машине `Ctx => C.HasAuthorContext`, `AutomationContext => C.IsWorldAuthority && C.AutomationAllowed`.
  `Contacting` и `CanOfferCartridge` дополнительно требуют `C.MainGripLocal`: Equipment не выдаёт ручной жест
  и не предлагает патрон через несуществующую руку.

## Задача 2. Право автоматики в одном хосте

Интерфейс: существующий `public override bool RequestAutomationPreparation()` остаётся единственным входом.
Потребитель bots-fix не получает отдельную prepare/refill/chamber реализацию.

- [ ] Перед запросом проверить configured/active, `Kind != None`, автора предмета/пользователя,
  `IsWorldAuthority`, `CanUse`, отсутствие replay/SyncCallDepth и reentrancy. Observer и конфликт возвращают false.
- [ ] Право выдать по свежему ключу `(Kind, User, Epoch)`; это временное разрешение, не копия M/C, binding
  или readiness. Equipment получает author context для продолжения подготовки только после допустимого запроса.
- [ ] Запустить существующий `AutomationPrepareRequested` и Drain. Вернуть true только при сохранённом том же
  допустимом ключе и SDK readiness; сырой `IsReadyToFire` при потерянном контексте не разрешает стрельбу.
- [ ] Согласовать ключ **перед** Drain в Update и запросе автоматики. Потеря, transfer/epoch, death, SDK main-grip
  release/regrab, authority change, Disable, Configure и Teardown отзывают право и сбрасывают trigger/цикл
  существующими `ContextLost`/`AuthorityChanged`; второго automation lifecycle и G1-флага не добавлять.
- [ ] Pending события старого ключа не исполняют авторские команды. Presentation реплицированных фиксаций
  наблюдателя сохраняется; удалять фиксации ради сброса grant нельзя.
- [ ] Capture передаёт руку и ручной ActionSample только для VrHand. У Equipment нет VR fallback в Dispatch.
  IsIdle учитывает действующий контекст/подготовку. Отклик остаётся у действующих исполнителей.

## Задача 3. SDK-политика отдачи корня

Сигнатура нового SDK-порта: `IUxrFirearmRootRecoilPolicy { bool AllowsRootRecoil { get; } }`.
SDK не ссылается на игровые `WeaponUseContext` или `WeaponEquipmentBinding`.

**Решение пользователя 2026-10-09, согласованное bots-fix в @2:** binding реализует
политику `AllowsRootRecoil => !IsActive`; код adapter принадлежит bots-fix. Активный claim запрещает отдачу
при unresolved/dead owner, выключенном компоненте и teardown WeaponSystem. Подключение policy должно находиться
на фактическом root grabbable, который использует SDK `RootGrabbable`.

Provider в WeaponSystem не добавлять: пользователь выбрал binding. Точный root lookup SDK должен учитывать
тот же предметный root, на котором находится binding, а не root аватара или дочернюю рукоять.

- [ ] После решения согласовать owner-side срез через хаб и точную ревизию контракта, если меняется API/ownership.
  Игровой исполнитель не правит чужой binding.
- [ ] SDK root handler применяет position/rotation recoil, только если все providers разрешили.
  Отсутствие политики сохраняет прежний VR-путь; отключённый MonoBehaviour с активным claim не исключается из policy.
- [ ] Не менять recoil timer, выпуск снаряда, учёт, звук выстрела и остальные эффекты.
  Запрет затрагивает только конкурирующую запись root transform.
- [ ] Записать SDK70 в журнал задачи; общие SDK-журналы не редактировать.

## Задача 4. Проверка и совместный preview

Перед реализацией исполнитель возвращает readiness-разбор и список точных входных файлов/контрактов.
Principal проверяет дифф/допуск и выдаёт отдельное разрешение на продуктовую запись.

- [ ] Офлайн-компиляция через найденный `tasks/weapon-system/tools/compile.ps1`.
  Проверить актуальность машинных путей и охват новых `.cs`; отчёт должен подтверждать Core, SDK, игру, Editor
  и существующие тестовые сборки, а не просто успешный запуск PowerShell.
- [ ] После broker lease: текущий AndroidCompileGate и применимые **существующие**
  `WeaponStateMachineStructureTests`, `WeaponStateMachineDriveTests`, `WeaponInitializeRetryTests`,
  pilot/wave prefab checks и ejection regression. Исторические PASS не использовать как результат этапа.
- [ ] Новые поведенческие тесты до подтверждения механики пользователем не писать/не переписывать.
  Устаревшие ожидания перечислить отдельно; failures вне среза отделить от failures этапа.
- [ ] DEVELOPMENT composition собирает bots-fix в явно согласованном пакете с обоими кандидатами;
  непринятый код не вливается в origin/dev и контракт преждевременно не активируется.
- [ ] До request подготовить точные действия/cleanup/release conditions. Unity — только в собственной
  RUNNING аренде: checkpoint → request → watch-ticket → claim → begin → guard → execute/wait → finish → receive.
  После выполненного пакета finish до диагностики и ожидания, кроме явно согласованной проверки в шлеме.
- [ ] MCP raw output сохранить в reports, наружу вернуть counts/до 10 примеров/путь.

Пять обязательных областей review/шлема: cold chamber и Empty/HoldOpen; конфликт и unresolved owner;
смена epoch/death/drop/disable; observer/replay и queued old-key commands; неизменность VR-отдачи и отсутствие
второго writer root NPC. Проверить обе модели NPC и как минимум ручной/самозарядный профиль.
Подтверждение — реальный projectile/урон, а не animation или true от API.

После подтверждения пользователя в этой же задаче добавить тесты resolver/grant/epoch/recoil и применимые avatar
проверки, затем фактические прогоны, принятый commit и хаб verify → accept → merge-request → merge-execute.
Production cutover/activation выполняются владельцами после принятого вливания, ff-only.

## Что вернуть основному агенту

1. Подготовка: `reports/use-context-implementation/preflight.md` — точные методы/файлы, missing inputs,
   owner-side вопрос, предлагаемый состав preview; статус READY либо BLOCKED с конкретной причиной.
2. После отдельного допуска: дифф и changed files; фактические команды/exit codes/число проверок/пути отчётов;
   SDK70; необновлённые тестовые ожидания; чек-лист шлема; оставшиеся риски.
3. Не выдавать подготовку или согласованный контракт за реализацию; механика до шлема остаётся непринятой.

Текущий бриф исполнителю: подготовить этот срез со свежим контекстом; запись только в своём подкаталоге reports.
Продуктовая реализация назначена ему, но **не начинается**, пока principal не подтвердит входные зависимости,
owner-side policy/VR-lifetime, ревизию контракта и действующий допуск. Не менять `Readme.md`, `plan.json`,
чужие файлы, инфраструктуру и Unity.
