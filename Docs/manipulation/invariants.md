# Инварианты системы манипуляций

Нарушение любого пункта — архитектурный конфликт: не обходить, назвать и предложить решение (AGENTS.md).
Чем закреплён пункт — указано в скобках; тесты — [sources.md](sources.md#тесты-assetstestseditmode-только-editmode).

## Хват

1. **Состоянием хвата владеет SDK** (`UxrGrabManager`: кто что держит, где лежит предмет). Игра не хранит копий
   grab-state: решает допуск, слушает события, синхронизирует их.
2. **Правила хвата — в одном месте:** `GrabRules`, подключённый `PlayerGrabManager` через `UxrGrabber.CanGrabDelegate`
   (патч 2) на каждой руке. Порядок правил фиксирован; новое правило — шаг в `GrabRules`, не отдельный делегат.
   Тот же делегат управляет подсветкой и кандидатом хвата — заблокированное не подсвечивается.
3. **Деталь берётся только при удержании родителя** тем же аватаром (`GrabOnlyWhenParentHeld` на каждой вложенной
   детали `UxrGrabbableObject`; `WeaponPartGrabTests`). Вставленное в якорь внутри предмета (магазин в оружии) рукой
   не берётся — только через свой механизм (`AnchoredItemGrabRule`, `MagazineEject`; `AnchoredItemGrabTests`).
4. **Две руки:** точка, занятая другой рукой, уступает свободной точке того же предмета (`TwoHandGrabPolicy`);
   опорные точки — только при удержании основной (`SupportGripRequiresMain`); перехват огнестрела своей второй
   рукой запрещён флагом `ManipulationConstraints.AllowHandTransfer` (решение weapon-system).
5. **Хват решается на машине хватающего.** Сервер применяет события без повторной проверки правил; где цена ошибки
   высока (экономика арсенала), есть серверный отказ (`ServerRejectTake`).

## Якоря и карманы

6. **Содержимое кармана пишет только сервер** (`PlayerLoadoutManager`, `SyncList`); выдача — через `GrabProxy`
   якоря (`UxrMagazinePocket`, `Docs/magazine-pocket.md`).
7. **Размещение кодом и `IsGrabbable`** — только сервер / world authority (лоадаут, жетон, арсенал).
8. **Совместимость якоря** — по тегу (`IsCompatibleObjectTag`, патч 49); `IsCompatibleObject` означает «можно положить
   сейчас», а не «подходит» (Issue 35). Пустые `Compatible Tags` принимают только предметы без тега (Issue 16).
9. **Иерархия:** арсеналы не вкладываются в масштабируемый `TeamSpawnZone`; якорь и предмет на карте — в `Gameplay`
   (AGENTS.md, `Docs/scene-hierarchy.md`).
10. **Префабы предметов:** не-trigger коллайдер на `Rigidbody`, `Collision Detection` не `Discrete`, `OutOfWorldGuard`;
    у оружия с якорем магазина — `AnchoredItemCollisionIgnore` (AGENTS.md; `WeaponDropPhysicsTests`, `OutOfWorldGuardTests`).

## Сеть

11. **Единственный транспорт** — `NetworkStateRelay`; фильтр — `StateEventAuthority`.
12. **Сетевые действия — один автор:** код из Update/таймера/физики/RPC/хука, вызывающий синхронизируемый метод
    (`ReleaseObject`, `IsGrabbable`, `PlaceObject`, `Shoot`…), проверяет `StateEventAuthority.IsAuthorOfItem` /
    `IsWorldAuthority` (Issue 23 — двойные выстрелы и урон; `StateEventAuthorityTests`).
13. **Синхронный вызов из обработчика Placed/Removed/Released в сеть не уходит** (Issue 38) — побочное действие
    делать на каждой машине детерминированно или отдельной командой автора.

## SDK

14. **Патч 26 не откатывать:** проход «рука рядом с якорем» выключен (upstream пишет `GrabberNear = null`, поля
    `Activate On Hand Near And Grabbable` / `PlacedObjectRange*` мертвы — Issue 15). Отключение дало прирост
    производительности (решение пользователя).
15. **Патчи 16 и 18:** подсказки хвата (`UpdateAffordances`) считаются только для рук локального аватара
    (`IsLocalAffordanceGrabber`, патч 16) — поэтому и кандидат хвата патча 67 есть только у своих рук. Подсказка
    «можно положить в якорь» — только для предмета в своей руке (`HasLocalAffordanceGrab`, патч 18).
16. **Патч 67:** три вызова в первом проходе `UpdateAffordances` + partial `UxrGrabManager.GrabCandidates.cs`. Новых
    расчётов нет, только сохранение результата. Чужие правки `UxrGrabManager.cs` обязаны их сохранить (условие ACK
    контракта `weapon-grab-lifetime@1`).
17. **Патчи 25–28 ускоряют поиск хвата без смены результата** (`GrabQueryPatchTests`). Правка порядка проверок в
    `CanBeGrabbedByGrabber` — только с этим тестом.
18. **Правка SDK** — номер через `coordination.py patch-reserve`, запись `tasks/<задача>/changelog/<дата>-sdk-<N>.md`,
    метка `VR Battlegrounds patch <N>` в коде.

## Отклик взаимодействий

19. **Один исполнитель** `InteractionFeedback` (ставит себя сам, `DontDestroyOnLoad`, не в batch mode). Второй
    «читатель близости» или опрос якорей не добавлять.
20. **Готовность — ответ SDK, тот же, что решит grip:** пустая рука — `UxrGrabManager.TryGetGrabCandidate` (патч 67),
    рука с предметом — `AnchorPlacementReadiness` поверх `GetAnchorPlacementCandidate`. Своих расстояний нет.
21. **Самое частное побеждает:** `InteractionFeedbackOverride` предмета → якоря → `InteractionFeedbackConfig`.
    Пустое поле — «как дальше по цепочке»; пустой конфиг — без отклика.
22. **Рука задаётся до включения.** Экземпляр отклика лежит под неактивным пулом; `HapticPlayer.Bind(side, role)`
    вызывается до переноса к цели, иначе `OnEnable` сыграет без руки (тишина) или не той рукой.
23. **Готовность локальна; сетевыми бывают только события.** Для чужого события включаются только дочерние слоты с
    `NetworkedFeedback`; префаб без них для чужого события не создаётся.
24. **Покрытие:** каждый якорь и корневой хватаемый предмет игры имеет отклик готовности или исключение с причиной
    (`ManipulationFeedbackCoverageTests`, сейчас исключений нет). Каждый якорь имеет звуки (`AnchorSoundCoverageTests`).

## Вибрация

25. **Один писатель мотора.** Мотор пишет только `UnityXRHapticDevice` по команде `HapticMixer` внутри `HapticService`.
    Остальные — `HapticService.Play/PlayBoth/Begin` с `UxrHapticClip`. Сторож — `HapticOwnershipTests` (временные
    исключения — список `Pending` с владельцем задачи). Вибрации SDK идут в сервис через маршрутизатор патча 65 —
    прямой вызов SDK в игровом коде всё равно запрещён (сторож проверяет и 4 точки `TryRoute`).
26. **Только своя рука.** Рука чужого аватара, бота или `null` — no-op (`HapticService.IsLocalHand`). Сетевых
    вибраций не бывает.
27. **Клип без формы — тишина** (кроме запросов SDK: им запасной клип по типу из `SdkHapticFallbacks`, решение
    2026-10-11). Форма — общий ассет `Assets/Data/Haptics/Waveforms/`; клип ссылается только на формы
    проекта (`HapticWaveformTests`). Импульс короче 30 мс на LRA Quest 2 не ощущается.
28. **Приоритеты:** Low < Normal < High < Critical. Слышен высший; `Normal` смешивается по максимуму; разовый поверх
    непрерывного; непрерывный `Low` под высшим — на паузе; пауза повтора не занимает мотор; кулдаун — на (клип, рука)
    (`HapticMixerTests`).
29. **Вторая рука** на том же предмете — роль `Secondary`, сила × `SecondaryHandGain` клипа (`InteractionFeedbackTests`).
30. **Перехват SDK (патч 65) принимает каждый запрос** и не зовёт SDK для вывода; без установленного маршрутизатора SDK
    ведёт себя как до патча. Правка 4 точек в `Devices/` — только по ACK avatar-ik (`SdkHapticRouterTests`, `HapticOwnershipTests`).
31. **Инерция удерживаемого предмета — только у локального держателя** (патч 73): сопротивление сглаживает позу
    лишь когда все держатели-аватары `Local`; на машинах-наблюдателях и у бота на сервере предмет жёстко в кисти
    (`HeldResistanceAuthorityTests`).
32. **При смерти отпускает только сервер** на верхнем уровне до уничтожения тела; обработчики смерти не вызывают
    синхронизируемых методов SDK и Spawn/Destroy (network 4303, Issue 38).
33. **Размещение в якоре доезжает до опоздавшего** (патч 75): `_currentAnchor`/`_currentPlacedObject` в снимке всегда;
    раскладку после загрузки делает один локальный согласователь в `UxrGrabManager`, без событий и второго хранилища
    (`LateJoinAnchorStateTests`).
34. **Снятие с якоря синхронизирует позу от автора снятия** (патч 75); на клиенте при повторе события — ничего.
    Заспавненный сетевой предмет на сервере не деактивировать (`SetActive(false)`): Mirror не спавнит неактивные
    опоздавшему (network 5458; карман — этап `pocket-active-hide`).
