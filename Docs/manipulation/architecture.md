# Архитектура манипуляций и вибрации

Состояние на 2026-10-10 (origin/dev `8e3c513f`). Пути кода — от `Assets/Scripts/`, если не сказано иное.
Подробные места — [sources.md](sources.md), правила — [invariants.md](invariants.md), решения — [decisions.md](decisions.md).

## 1. Слои

```
XR-ввод (grip) ─► SDK UxrGrabManager / UxrGrabber / UxrGrabbableObject(Anchor)      — физика хвата, якоря, события
                    │  CanGrabDelegate (патч 2)        │ события ObjectGrabbed/Placed/Released, Grabbing…
                    ▼                                   ▼
        Решение «можно взять»                Отклик (только локально)        Сеть
        PlayerGrabManager → GrabRules        InteractionFeedback → префабы   NetworkStateRelay ← StateEventAuthority
                                             HapticService → HapticMixer → мотор
```

SDK владеет состоянием хвата (кто что держит, где лежит предмет). Игра не держит копий этого состояния: решает
допуск, слушает события и синхронизирует их. Новая фича, которой нужна «своя» копия grab-state, — архитектурный конфликт.

## 2. Решение «можно взять» (только машина хватающего)

- `Player/PlayerGrabManager.cs` — по одному `UxrGrabber.CanGrabDelegate` на руку (вкл/выкл в OnEnable/OnDisable);
  мёртвому игроку — `Ghost/GhostGrabRule` (только свой планшет). Освобождение рук — `ReleaseAllGrabbedObjects`,
  `ReleaseLocalOnlyItems`.
- `Player/GrabRules.cs` — **единственное место правил**, порядок фиксирован: `MapRunAdmission.IsLocalPlayable` →
  `GrabOnlyWhenParentHeld` (деталь — только если тот же аватар держит родителя) → `SupportGripRequiresMain` (точки ≠ 0 —
  только при удержании точки 0 другой рукой) → `AnchoredItemGrabRule` (магазин в оружии рукой не берётся, только
  `MagazineEject`) → `ArsenalGrabRule` (экономика стены; настоящая проверка — сервер `ServerRejectTake`) →
  `TwoHandGrabPolicy` (занятая другой рукой точка уступает свободной; `ManipulationConstraints.AllowHandTransfer`).
- Тот же делегат управляет подсветкой: заблокированная точка не подсвечивается и не станет кандидатом хвата (патч 67).
- `Interaction/GrabbableHierarchyCache.cs` — покадровый кэш иерархии для правил.

## 3. Якоря, карманы, размещение

- Карман магазинов — `Interaction/UxrMagazinePocket.cs`: скрытое хранилище с лимитом по типу (`Tag`), выдача через
  `GrabProxy` якоря (патч 3/4). Содержимое пишет только сервер — `Player/PlayerLoadoutManager.cs` (`SyncList`).
  Описание — `Docs/magazine-pocket.md`.
- Роли якорей — `Interaction/AnchorRole.cs` (имена `Anchor_Back`/`Anchor_Hip` — контракт).
- Готовность положить — `AnchorPlacementReadiness` поверх SDK `GetAnchorPlacementCandidate` (T-39).
- Зоны досягаемости (для гизмо/отладки, та же математика, что SDK) — `Interaction/AnchorReachZones.cs`.
- Арсенал: `Arsenal/ArsenalSlotController`, `ArsenalAnchorPlaceZone`, `ArsenalMagazineOffer/Supply`, `DogTagController`
  — `IsGrabbable` и размещение кодом пишет только world authority.
- Оружие: `Weapons/MagazineEject.cs` (`IsAuthorOfItem`), `Weapons/CartridgeIntake.cs`;
  `Interaction/AnchoredItemCollisionIgnore.cs` гасит столкновения корпуса с вставленным (PHY-01).

## 4. Отпускание и мир

`Interaction/OutOfWorldGuard.cs` (чистое `Decide`, сервер уничтожает сетевые), `LooseItems`/`LooseItemSweeper`,
`Player/EquipmentStrip.cs`, `Player/Avatars/AvatarTeardown.cs`, `Player/Corpse/DeathDropEjection.cs`.

- **Смерть** (факт прогонов 2026-10-12, `death-body-defer` gameplay + network 4303): руки отпускает только сервер —
  `AvatarTeardown.ReleaseBeforeDestroy`/`ReleaseHandsOnServer` на верхнем уровне, в отложенном шаге смены тела
  (`AvatarManager.CanReplaceBodyNow`), до `NetworkServer.Destroy`. Обработчики смерти (`Died`/`PlayerDied`) ничего
  синхронизируемого не зовут; `PlayerGrabManager` на смерть не подписан.
- **Удержание** (SDK-патч 73): сопротивление предмета (`UxrInterpolator`) — только если все держатели-аватары
  `Local`; у бота на сервере и чужого игрока у наблюдателя предмет жёстко в кисти (кость кисти уже несёт
  отставание автора). Держатель без аватара — прежнее поведение (`HeldResistanceAuthorityTests`).

## 5. Сеть

- Единственный транспорт — `Network/NetworkStateRelay.cs` (Cmd → сервер применяет → Rpc остальным).
  Фильтры: `DespawnedObjectEventFilter` → `StateEventAuthority.ShouldSend` → `AvatarStateEventGate`.
- `Network/StateEventAuthority.cs` — событие удерживаемого предмета шлёт только автор держащего аватара; события
  `UxrGrabManager` проходят всегда. Код из Update/таймера/физики/RPC проверяет `IsAuthorOfItem`/`IsWorldAuthority`
  (Issue 23 — двойные выстрелы/урон).
- `Network/NetworkUxrIdentity.cs` — уникальный id для спавненных в рантайме (NET-16).

## 6. Вибрация

- `Haptics/HapticService.cs` — единственный владелец моторов; `HapticMixer` решает, что звучит; `UnityXRHapticDevice`
  пишет `InputDevice.SendHapticImpulse`/`StopHaptics`. Вызов — клип `UxrHapticClip` (патч 66): форма
  `UxrHapticWaveform` + сила, приоритет, пауза повтора, кулдаун, вторая рука. Рука чужого/бота — тишина.
- Голоса: разовый (`Play`) и непрерывный (`Begin` → `HapticHandle`, живёт пока жив owner). До 8 голосов на руку.
- Подбор: окно `Tools/VR Battlegrounds/Haptics/Вибрация`, drawer клипа с пробой на руках в Play, автозапись при выходе.
- Вибрации SDK перехватывает патч 65 (`Haptics/UxrHapticRouting.cs`, первой строкой в
  `UxrControllerInput.SendHapticFeedback(ClipType)` и трёх override `UxrUnityXRControllerInput`): маршрутизатор
  `SdkHapticRouter` (ставит `HapticService`) переводит запрос в голос сервиса и принимает каждый запрос — мотор SDK не
  трогает. Чужой аватар — тишина; клип с формой — как есть; без формы и тип SDK — запасной клип типа из
  `Resources/SdkHapticFallbacks.asset` (числа подбирает пользователь); сырой запрос и гул Rumble — один продлеваемый голос
  со сроком; `Stop` гасит только голоса маршрутизатора. Отдачу Shot* гасит ствол с `IWeaponRecoilHapticsOwner`
  (контракт `haptics-api`). Router без сервиса — `null`, SDK как до патча; сброс в `SubsystemRegistration`.
  Временный `WeaponHapticOutput` weapon-system и прочие прямые вызовы из `Pending` звучат через маршрутизатор.

## 7. Отклик взаимодействий

- Отклик — префаб-GO (`Assets/Prefabs/Feedback/`) из обычных компонентов: `HapticPlayer` (клип + режим
  «пока активен»/«один раз», рука и роль — от исполнителя), звук, подсветка. `NetworkedFeedback` на дочернем слоте —
  играет и при событии чужого игрока.
- Выбор: `InteractionFeedbackOverride` предмета → якоря → `InteractionFeedbackConfig` (готовность по роли якоря,
  «в досягаемости», разовые взят/уложен/отпущен; сейчас все готовности → `Feedback_GrabReady`, разовые пустые).
- `Haptics/InteractionFeedback.cs` — один исполнитель. Каждый кадр по своей руке: пустая — кандидат хвата SDK
  (предмет в якоре или прокси → отклик якоря у якоря; свободный или его деталь → «в досягаемости» у предмета; часть
  удерживаемого другой рукой → роль `Secondary`); с предметом — якорь, куда SDK положит. События `UxrGrabManager`
  (`IsGrabbedStateChanged`) → разовый экземпляр. Экземпляры из пула под неактивным контейнером.
- Правка конфига или префаба в Play пересоздаёт экземпляры — подбор слышен сразу.

## 8. Кандидаты на проверку (гипотезы по чтению кода, 2026-10-10, не подтверждены прогоном)

- ~~Дубль отпускания при смерти~~ — опровергнуто прогоном 2026-10-12: отпускание и сама смерть до клиентов не
  доходили; исправлено в `death-body-defer` (см. §4).
- Сервер применяет клиентские события хвата без проверки правил: `GrabRules` работают только на клиенте; серверная
  проверка есть только у арсенала (`ServerRejectTake`).
- `UxrMagazinePocket.OnAnchorPlaced` на каждой машине зовёт `RemoveObjectFromAnchor(…, true)` — безопасно лишь потому,
  что вложенные события внутри `ExecuteStateSyncEvent` не пересылаются.
- Руки освобождают несколько мест: `PlayerGrabManager`, `AvatarTeardown`, `EquipmentStrip`,
  `PlayerLoadoutManager.ServerDropEquipment`, `ServerRejectTake`.
- `GrabbableHierarchyCache` видит смену дальнего предка на кадр позже; `AllowHandTransfer` — изменяемое static-состояние.
- Комментарий патча 18 в `UxrGrabManager.cs` называет подписчиком удалённый `PocketReadiness` — устарел.

## 9. Соседние владельцы

- **weapon-system** (`weapon-system-expert`, `Docs/weapons/`) — механика оружия, машина состояний, данные стволов,
  отклик ствола (клипы отдачи/отказа в данных ствола), `weapon-grab-lifetime@1` (stamp жизни хвата в `UxrGrabber`).
- **hand-rig-quality** (`avatar-grip-expert`, `tasks/hand-rig-quality/expert/`) — позы кистей, риг, Hand Pose Fit.
- **bots-fix**, **legs-ik**, **avatar-renderer-regression** — префабы аватаров и `AvatarLoadoutTests`; порядок через хаб.
