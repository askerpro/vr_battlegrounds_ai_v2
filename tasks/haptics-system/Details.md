# Подробности: система вибрации

Дизайн утверждён пользователем 2026-10-07; контракт п. 4.2 принят владельцем WeaponSystem. Текущий статус — только в
[Readme.md](Readme.md), машинный план — [plan.json](plan.json). Постановка — [task.md](task.md),
исследование и решения пользователя — [research.md](research.md) (п. 8). Обозначения: **[факт]** — проверено
в коде; **[гипотеза]** — проверить в шлеме. Все числа — стартовые, подбираются в шлеме правкой каталога.

## 0. Пересмотр архитектуры 2026-10-08 — клипы с формой и общая система взаимодействий (утверждён)

Запрос пользователя: открытый реестр преднастроенных паттернов вместо закрытого списка; назначение наших пресетов прямо в
компонентах SDK (`UxrManipulationHapticFeedback` и др.); убрать `PocketHaptics` и подобные «скрипт под сценарий» в пользу
общей системы. Ниже — предлагаемая замена п. 3, 4.1, 8 и части п. 5; классы, смешивание (п. 2), сеть (п. 7), сторож (п. 6)
и контракт по смыслу сохраняются.

### 0.1 Факты (обзор SDK и наших сценариев)

- Вибрацию в SDK задают 13 мест. Клип `UxrHapticClip` (`[Serializable]`, 6 полей: AudioClip, амплитуда, режим, запасной
  тип/сила/длительность) используют `UxrManipulationHapticFeedback` (хват/укладка/отпускание; 6 префабов проекта:
  Tablet_Base, AR15_Magazine, BrowningHiPower_Magazine, FabarmSDASS, FabarmSDASS_Ammo, Herrington), `UxrFirearmTrigger.ShotHapticClip`
  (23 ствола, у всех одинаково ShotBig 1.0), `UxrShotgunPump` (4 клипа, FabarmSDASS), `UxrGrenadeWeapon` (чека, 0 префабов),
  `UxrControlFeedback` в UI (`UxrControlInput`, 0 в проекте).
- Без клипа: `UxrHapticOnImpact` (тип клипа + сила по удару, 0 префабов), `UxrFixedHapticFeedback` (0), `UxrGrabbableResizable`
  (0), непрерывная манипуляция (по скорости, планшет), UI-перетаскивание и клики по умолчанию `UxrPointerInputModule`
  (Click 0.2/0.6), сорванный хват `UxrGrabManager` (Click 1.0).
- **Готового SDK-инспектора для вибрации нет**: клипы рисуются стандартным `PropertyField`, PropertyDrawer'а и пробы нет. Один
  наш `CustomPropertyDrawer(typeof(UxrHapticClip))` дорисует поле пресета во **всех** компонентах SDK сразу.
- Новое поле в `UxrHapticClip` не затрагивает state save и сетевую синхронизацию SDK (клипы нигде не копируются и не
  синхронизируются); старые ассеты без ключа получают null — поведение прежнее.
- Готовность «положить в якорь» уже общая для любого якоря (`AnchorPlacementReadiness`, используется и подсветкой гнезда
  магазина). «Взять из якоря» (`PocketReadiness`) привязана к карманам только фильтром и списком кандидатов; глобального события
  «можно взять» в SDK нет — нужен опрос `GetClosestGrabbableObject` по своей руке, как сейчас.

### 0.2 Принятое устройство (утверждено пользователем 2026-10-08)

**Клип — это форма заданной длины; как и когда её играть, решает точка интеграции.**

1. **`UxrHapticWaveform`** (SDK-патч 66, ассет, *Create → VR Battlegrounds → Haptics → Waveform*) — только форма: отрезки
   «сила 0..1 × мс». Реестр форм — `Assets/Data/Haptics/Waveforms/` (стартовые: Steady, Click, DoublePulse, TriplePulse,
   RecoilPistol/Rifle/Shotgun, Death). Правка формы сразу меняет все клипы, которые на неё ссылаются.
2. **`UxrHapticClip`, расширенный патчем 66** — точка интеграции везде (компоненты SDK и код игры): ссылка на форму, сила,
   приоритет (`Low` — «можно», `Normal` — физика, `High` — отказ/предупреждение, `Critical` — гибель), пауза повтора,
   кулдаун, вторая рука. Прежние поля SDK (AudioClip, запасной тип) остаются для клипов без формы. Повторяет ли клип, решает
   вызов: разовое событие — один раз, состояние (`Begin`) — пока длится, с паузой клипа. Перечень `HapticSignalId`,
   `HapticCatalog` и `HapticPreset` не нужны.
3. **`HapticRoles`** (`Resources/HapticRoles.asset`) — клипы событий, которые поднимает сама игра: готовность якоря по роли
   (карман магазинов, спина, бедро, прочий на аватаре, якорь вне аватара), хват/укладка/отпускание предмета; дальше —
   гибель, стена, часы, запасные клипы для SDK-вызовов без клипа. **`HapticOverride`** — свои клипы для конкретного якоря
   или предмета.
4. **`InteractionHaptics`** — одна общая система вместо `PocketHaptics`: готовность любого якоря (обобщённая
   `AnchorReadiness`, якоря вне аватара — только если их роль задана) и события хвата/укладки/отпускания своей руки.
   Новый сценарий — роль/форма в данных, не новый скрипт. По умолчанию заданы только роли карманов (ровный гул, сила 0.08),
   остальные пустые. `PocketHaptics` до этапа `pocket-removal` — пустая оболочка на префабах аватаров.
5. **Подбор** — drawer `UxrHapticClip` во всех инспекторах (форма, ползунки, картинка формы, проба на руках в Play),
   инспектор формы, окно `Tools/VR Battlegrounds/Haptics/Вибрация` (живое состояние рук, формы, роли). Запись — при выходе из Play.
6. **Компоненты SDK** получают поле формы через тот же drawer; играть форму начнут после перехвата (`sdk-routing`). Клики
   хвата на 6 префабах переводятся отдельным этапом `item-clicks` по согласованию с WeaponSystem.

### 0.3 Цена и риски

- Переделка пункта 1 (каталог → пресеты, роли, окно) и `InteractionHaptics` с удалением `PocketHaptics` — около 1,5 дня;
  поле в SDK и drawer — в этапе `sdk-routing`.
- Префабы аватаров (PlayerBase, Cyborg): снять компонент — пересечение с задачами аватаров, нужна координация.
- Опрос «можно взять» по всем якорям дороже карманного: предварительный дешёвый фильтр кандидатов сохраняется; замер на Quest.
- Контракт WeaponSystem — ревизия 2 (`HapticPreset` вместо `HapticSignalId`; пресеты отказа/отдачи — в данных ствола).
  Этап D у них не начат, переход дешёвый, нужна ACK.
- Двойной хват-клик: если роль «хват предмета» задать, а `UxrManipulationHapticFeedback` с клипом оставить — сработают оба. До
  миграции префабов роль «хват» пустая.

## 0.4 Отклик взаимодействий (утверждено пользователем 2026-10-09, этап `interaction-feedback`)

Заменяет пункты 3–4 из 0.2 (`HapticRoles`, `HapticOverride`, `InteractionHaptics`, `AnchorReadiness` удалены).

- **Отклик — префаб-GO из обычных компонентов Unity** (`Assets/Prefabs/Feedback/`): `HapticPlayer` (клип + режим «пока
  активен» / «один раз при включении»; как `AudioSource` для звука), звук, подсветка, частицы. Включение GO = отклик играет,
  выключение = стихает. Своих слотов и автоплея нет — работает жизненный цикл Unity.
- **Клип и проигрыватель разделены:** `UxrHapticClip` — форма и правила (сила, приоритет, пауза повтора, кулдаун);
  `HapticPlayer` решает, когда играть, и получает руку от исполнителя до включения GO (экземпляр лежит под неактивным
  контейнером пула). Без руки — тишина.
- **Выбор — самое частное побеждает:** `InteractionFeedbackOverride` предмета → якоря → `InteractionFeedbackConfig`
  (`Resources/InteractionFeedbackConfig.asset`: готовность по роли якоря, «предмет в досягаемости», разовые «взят /
  уложен / отпущен»). Чужие префабы правок не требуют: по умолчанию все готовности ведут на `Feedback_GrabReady`
  (Steady, 0.08, Low, «пока активен») — готовность взять предмет ощущается так же, как готовность кармана. Вторая рука
  к предмету, который уже держит другая рука (или к его части — цевьё, затвор), получает роль `Secondary`: сила ×
  `SecondaryHandGain` клипа, у готовности 0.5 (решение пользователя после проверки в шлеме 2026-10-09). Разовые
  события пока пустые (этап `item-clicks`).
- **Один исполнитель `InteractionFeedback`:** своя пустая рука — кандидат хвата SDK (патч 67, тот же расчёт, что решит
  grip): предмет в якоре или прокси кармана → отклик якоря у якоря, свободный предмет или его деталь → «в досягаемости» у
  предмета. Своя рука с предметом — кандидат якоря SDK (`AnchorPlacementReadiness`). Своих расчётов близости нет; проход
  патча 26 (`GrabberNear`) остаётся выключенным ради производительности. События `UxrGrabManager` (взят, уложен,
  отпущен) — разовый экземпляр на `OneShotLifetime`.
- **Сеть:** метка `NetworkedFeedback` на дочернем объекте префаба — слот играет и при событии чужого игрока (звук вставки
  магазина). Остальное (вибрация, подсветка) — только свой игрок; готовности всегда локальные. Префаб без сетевых слотов
  для чужого события не создаётся.
- **Подбор в Play:** окно «Вибрация» показывает конфиг и клипы всех `HapticPlayer` префабов отклика; правка конфига или
  префаба пересоздаёт активные экземпляры, значения слышны сразу.
- **Покрытие:** `ManipulationFeedbackCoverageTests` вычисляет отклик готовности каждого якоря и предмета игры тем же
  порядком; исключение — только с причиной. Сейчас исключений нет.
- **Дальше:** перенос существующих откликов (`WeaponMagazineAnchorHighlight`, `AnchorSound`, `Activate On …`,
  `UxrManipulationHapticFeedback`) в префабы отклика — отдельными пунктами с владельцами областей.

## 1. Суть

- У вибромоторов один владелец — `HapticService`. Он единственный пишет в `InputDevice`.
- Остальной код просит **сигнал по Id** из каталога `HapticCatalog`. Класс, рисунок и кулдаун — свойства сигнала
  в каталоге, вызывающий их не задаёт.
- Вибрация только на руке локального игрока (`UxrAvatar.LocalAvatar`). Сервис сам отбрасывает запросы для чужого
  грабера, бота и лежащего предмета.
- Вибрации SDK (отдача, помпа, хват, UI, сорванный хват) перехватываются SDK-патчем 65 и тоже идут через сервис.
- Параметры сигналов — настройка **проекта**, а не игрока. Настроек вибрации для игрока нет (решение пользователя
  2026-10-07). Подбор — инспектором каталога в Play Mode (п. 8).

```
игровой код ─ HapticService.Play/Begin(Id, рука)┐
WeaponSystem (этап D) ─ WeaponFeedbackExecutor ┤
SDK UltimateXR ─ патч 65: UxrHapticRouting ────┤──► HapticService ──► HapticMixer (чистый C#) ──► IHapticDevice
                                              │      локальная рука,       классы, приоритет,        UnityXRHapticDevice
инспектор каталога ── правит HapticCatalog ──┘      каталог               пауза, кулдауны           (InputDevice)
```

## 2. Модель

### 2.1 Классы и приоритет

| Ранг | Класс | Смысл | Рисунок | Внутри класса |
|---|---|---|---|---|
| 4 | **Отрицательный критический** (`NegativeCritical`) | гибель | длинный, на максимуме | не прерывается ничем |
| 3 | **Отрицательный** (`Negative`) | действие не выполнено, предупреждение | **двойной импульс**, короткий и сильный | новый заменяет старый |
| 2 | **Физический** (`Physical`) | отдача, ход механизма, щелчки хвата/укладки, SDK | один всплеск со спадом | **максимум огибающих** (отдача автомата не рвётся) |
| 1 | **Положительный** (`Positive`) | «можно»: карман готов, тихое уведомление | тихий; может быть непрерывным | разовый поверх непрерывного; новый заменяет старый |

Критический — подвид отрицательного, отдельный ранг нужен только гибели (решение п. 8.4: максимум, 1,5 с), чтобы
её не оборвал случайный отказ оружия в момент смерти.

### 2.2 Голоса и смешивание

Сигнал, запущенный на руке, — **голос**. У каждой руки свой список голосов (до 8, лишние вытесняют самые старые того же
класса). Все голоса идут по настоящему времени, независимо от того, слышны ли они.

1. **Слышен только высший ранг**, в котором есть голос в «занятом» отрезке (п. 2.3). Суммы нет: она стирает рисунок
   (паузы двойного импульса заполнялись бы чужим сигналом).
2. **Внутри ранга:** физический — максимум амплитуд всех голосов; остальные — самый новый голос.
3. **Разовый сигнал низшего ранга не откладывается.** Пока звучит высший, он идёт беззвучно и заканчивается по своему
   времени. Запоздавшая вибрация теряет причинность.
4. **Непрерывный положительный встаёт на паузу.** Пока звучит высший ранг, голос кармана беззвучен и продолжает жить; после
   окончания высшего снова слышен.
5. **Кулдаун** — на пару (сигнал, рука): повторный запрос раньше срока отбрасывается.
6. **Обе руки** (гибель, стена) — два независимых голоса, по одному на руку.

### 2.3 Рисунок сигнала

`Segments` — список «амплитуда 0..1, длительность мс». Пауза — сегмент с амплитудой 0. Весь рисунок, **включая паузы
внутри него**, занимает мотор: пауза двойного импульса — часть смысла.

`RepeatInterval` (мс, 0 — разовый) делает сигнал непрерывным: рисунок перезапускается каждые `RepeatInterval` мс от
своего начала, пока его не остановят. **Хвост между концом рисунка и следующим повтором мотор не занимает** — в нём
слышны низшие ранги. Так периодическое предупреждение стены не глушит отдачу между своими двойными импульсами.

Кривая (`AnimationCurve`) не нужна: импульсный API дискретен, а список сегментов точно описывает двойной импульс
[факт: `SendHapticImpulse(channel, amplitude, duration)` без частоты].

### 2.4 Вывод на устройство

- Сервис считает амплитуду руки: `слышимая амплитуда × gain вызова`, `Clamp01`.
- Запись в устройство — **сразу при `Play`** (отдача синхронна с выстрелом, без ожидания кадра) и в `LateUpdate`
  при смене амплитуды или сегмента.
- Постоянная амплитуда держится импульсом длиной «остаток сегмента + 1 кадр». У бесконечного сегмента — импульс 150 мс,
  обновляется за 50 мс до конца. Нулевая амплитуда — `StopHaptics`.
- Точность границ сегмента — кадр (11–14 мс при 72–90 Гц). Для импульсов от 30 мс и пауз от 60 мс этого достаточно
  [гипотеза: подбор в шлеме].
- Частоты нет и не будет до перехода на OpenXR (решение п. 8.5). Сигналы различаются амплитудой, длительностью и ритмом.

## 3. Каталог сигналов

### 3.1 Устройство

- **`HapticCatalog`** — ScriptableObject, один ассет `Assets/Resources/HapticCatalog.asset`. Загрузка — как у
  `MenuTheme`: ленивый `Instance` через `Resources.Load`, при отсутствии — `GameLog.Error` и пустой каталог (тишина,
  без исключений) [факт: `MenuTheme.cs:73-90`].
- **`HapticSignalId`** — enum с **явными** числами (`PocketReady = 100` и т. п.). Ассет хранит число, поэтому переименование
  и перестановка не ломают данные. Новый сигнал — новое число.
- Запись **`HapticSignal`**: `Id`, `Class`, `Segments`, `RepeatInterval`, `Cooldown`, `SecondaryHandGain`, `Description`
  (для инспектора — что и когда).
- Подбор значений — инспектор ассета в Play Mode через Quest Link (п. 8).
- Тест каталога (структурный, пишется сразу): каждый Id ровно одна запись; амплитуды 0..1; импульсы ≥ 30 мс; у
  `Negative` не меньше двух импульсов; непрерывный `RepeatInterval` ≥ длины рисунка.

### 3.2 Стартовые значения

Рисунки записаны как `амплитуда×мс`, `0×мс` — пауза. Спад записан ступенями по 20–45 мс.

| Id | Класс | Рисунок | Повтор | Кулдаун | Вторая рука | Источник |
|---|---|---|---|---|---|---|
| `PocketReady` | Положительный | `0.10×100` | 100 мс (ровный гул) | — | — | `PocketHaptics`, решение п. 8.1: **10 %**, подбор в шлеме |
| `WatchInfo` | Положительный | `0.3×40` | — | 0.2 с | — | часы: Low / Normal |
| `WeaponActionRear` | Физический | `0.5×40` | — | — | — | машина: `ActionRear` |
| `RecoilPistol` | Физический | `0.7×20, 0.5×20, 0.3×20, 0.15×20` | — | — | ×0.5 | WeaponSystem: выстрел |
| `RecoilRifle` | Физический | `0.9×25, 0.65×25, 0.4×25, 0.2×25` | — | — | ×0.5 | то же |
| `RecoilShotgun` | Физический | `1.0×45, 0.75×45, 0.45×45, 0.2×45` | — | — | ×0.5 | то же |
| `DebugModeOn` / `DebugModeOff` | Физический | `0.8×250` / `0.3×100` | — | — | — | `DebugGestureInput` (числа прежние) |
| `Sdk*` (7 шт.) | Физический | повторяют корутины SDK: `Click` 50 мс; `Shot` 120 мс; `ShotBig` 10×25 мс, вторая половина — спад; `ShotBigger` 10×40 мс; `Slide` 10×50 мс, линейный спад; `Explosion` 10×50 мс; `Rumble` — ровно | — | — | — | перехват SDK (п. 5.3), амплитуда запроса → gain |
| `SdkRaw` | Положительный | ровно, длительность и амплитуда из запроса | — | — | — | сырые непрерывные SDK: манипуляция (планшет), перетаскивание UI |
| `WeaponNotReady` | Отрицательный | `0.8×50, 0×70, 0.8×50` (170 мс) | — | 0.4 с | — | машина: `NotReady` |
| `WeaponObstructed` | Отрицательный | как `WeaponNotReady` | — | 0.4 с | — | машина: `Obstructed` |
| `WeaponRateOfFire` | Отрицательный | `0.7×50, 0×70, 0.7×50` | — | 0.25 с | — | машина: отказ по темпу (S2) + звук `UI_Error_Subtle_Deep` |
| `WeaponFaulted` | Отрицательный | `1.0×60, 0×80, 1.0×60, 0×80, 1.0×60` (тройной) | — | 0.6 с | — | машина: `Faulted` |
| `WallPassHint` | Отрицательный | `0.4×50, 0×70, 0.4×50` | 1500 мс | — | — | стена: подсказка |
| `WallPassViolating` | Отрицательный | `0.8×50, 0×70, 0.8×50` | 750 мс | — | — | стена: нарушение |
| `WatchWarning` | Отрицательный | `0.8×50, 0×70, 0.8×50` | — | 0.4 с | — | часы: High |
| `WatchCritical` | Отрицательный | тройной, как `WeaponFaulted` | — | 0.6 с | — | часы: Critical |
| `PlayerDeath` | Отрицательный критический | `1.0×1500` | — | — | — | гибель, обе руки (решение п. 8.4) |

Отдача по типам — стартовый набор. Какой сигнал у конкретного ствола, решает данные WeaponSystem (п. 4.2), не каталог.

## 4. API

### 4.1 Игровой код

Статические методы `HapticService` — вызовы безопасны без сервиса (headless-сервер, batch, EditMode): тогда это no-op.
Класс назван не `Haptics`: такое имя внутри пространства `VrBattlegrounds.Haptics` конфликтовало бы у вызывающих.

```csharp
namespace VrBattlegrounds.Haptics
{
    public enum HapticClass { Positive = 1, Physical = 2, Negative = 3, NegativeCritical = 4 }
    public enum HapticHandRole { Primary, Secondary }   // Secondary умножает на SecondaryHandGain сигнала

    public sealed class HapticService : MonoBehaviour   // API — статические методы
    {
        // Разовый. Рука чужого аватара, бота или null — тихо игнорируется (п. 7).
        public static void Play(HapticSignalId id, UxrGrabber hand, float gain = 1f, HapticHandRole role = HapticHandRole.Primary);
        public static void Play(HapticSignalId id, UxrHandSide side, float gain = 1f);   // рука локального аватара
        public static void PlayBoth(HapticSignalId id, float gain = 1f);

        // Непрерывный: живёт, пока не вызван End() или пока owner не выключен/не уничтожен.
        public static HapticHandle Begin(HapticSignalId id, UxrHandSide side, UnityEngine.Object owner, float gain = 1f);
    }

    public readonly struct HapticHandle { public bool IsActive { get; } public void End(); }
}
```

- Сервис сам завершает дескриптор, если `owner` уничтожен или выключен, или сменился `UxrAvatar.LocalAvatar`.
  Висящей вибрации быть не может.
- `HapticService` — MonoBehaviour, ставит себя сам (`RuntimeInitializeOnLoadMethod(AfterSceneLoad)`, `DontDestroyOnLoad`),
  как `WatchNotificationRunner` [факт]. В batch-режиме не ставится. Статики сбрасываются в `SubsystemRegistration`.
- Логика смешивания — чистый класс `HapticMixer` (время подаётся снаружи, устройство — `IHapticDevice`). Это делает
  поведение проверяемым без шлема.
- Логи — `GameLog.Player.Verbose` на запуск и отклонение сигнала (Id, рука, причина отклонения).

### 4.2 Контракт для WeaponSystem (этап D)

Передаётся владельцу WeaponSystem до вливания первого пункта.

1. **Исполнитель.** `WeaponFeedbackExecutor.Haptic(WeaponHapticCue cue)` вызывает
   `HapticService.Play(<Id по таблице>, <рука>)`. `ControllerInput` и `UxrHapticClip` в исполнителе не используются — их
   запрещает тест-сторож (п. 6).
2. **Рука.** Отказы (`NotReady`, `Obstructed`, `Faulted`, отказ по темпу) — рука на спуске. `ActionRear` — рука на
   ручке затвора/помпы. Отдача — рука на спуске (`Primary`) и вторая рука на том же оружии (`Secondary`).
3. **Таблица cue → Id:**

   | `WeaponHapticCue` | Id | Класс |
   |---|---|---|
   | `ActionRear` | `WeaponActionRear` | Физический |
   | `NotReady` | `WeaponNotReady` | Отрицательный |
   | `Obstructed` | `WeaponObstructed` | Отрицательный |
   | `Faulted` | `WeaponFaulted` | Отрицательный |
   | **новый** отказ по темпу (S2) | `WeaponRateOfFire` | Отрицательный; звук `UI_Error_Subtle_Deep` играет `WeaponAudioExecutor` |
   | **новый** выстрел (`Shot`) | Id отдачи из данных оружия (`RecoilPistol` и т. п.) | Физический |

   Новые значения `WeaponHapticCue` добавляет владелец WeaponSystem. Таблица живёт в исполнителе, Id отдачи — в данных
   WeaponSystem (например, поле `HapticSignalId Recoil` в `WeaponFeedbackProfile`).
4. **Авторство.** Машина зовёт `Haptic` только у автора и не при replay [факт: `AuthorOnly`]. Фильтр сервиса «локальная
   рука» — вторая, независимая страховка, она не заменяет авторскую проверку события.
5. **Отдача SDK отключается по стволу.** Хост WeaponSystem реализует интерфейс игрового кода

   ```csharp
   public interface IWeaponRecoilHapticsOwner { bool OwnsRecoilHaptics { get; } }
   ```

   Пока ствол не переведён (или `false`), сервис проигрывает отдачу SDK, как сейчас (`SdkShotBig`). Когда `true`, сервис
   отбрасывает отдачу SDK этого ствола, а отдачу выдаёт машина. Переход идёт волнами D → F5 без единой правки SDK; после
   этапа H путь отдачи SDK пуст. Отвергнутая альтернатива — обнулять `ShotHapticClip` в префабах через
   `WeaponSystemAuthoring`: владение разошлось бы между данными префаба и кодом.
   Согласовано с владельцем WeaponSystem 2026-10-07: где лежит Id отдачи (профиль ствола или набор откликов), он решает
   на этапе D; `OwnsRecoilHaptics` постоянен для экземпляра ствола (из данных), во время игры не переключается;
   `WeaponFaulted` остаётся в каталоге до этапа G (после решения В-Л5 сбой учёта может перестать блокировать стрельбу).
6. **Уходят вместе со старыми источниками.** Вибрации `AutomaticWeaponSlideFeedback`, `WeaponAttemptFeedback`,
   `WeaponChamberingReminder`, `BarrelObstruction` и помпы `UxrShotgunPump` haptics не трогает. Их удаляют или переводят
   этапы D, F5 и H. До удаления они в белом списке сторожа со ссылкой на этап (п. 6).

## 5. SDK-патч 65

Номер выдан `coordination.py patch-reserve` 2026-10-08 (ключ `sdk-patch-57-haptic-routing`); в постановке и прежнем
`pipeline.md` он назывался 57. Далее везде — 65.

### 5.1 Почему нужен

- На Quest каждый вызов обрывает текущую вибрацию; `Mix` ничего не смешивает [факт по коду + спецификация OpenXR].
- События `HapticRequesting`/`GlobalHapticRequesting` у наших контроллеров не поднимаются: переопределения
  `UxrUnityXRControllerInput` не зовут `base` [факт].
- Клик сорванного хвата и клики UI зашиты в коде SDK, данными их не выключить [факт].

### 5.2 Изменение

Новый файл SDK `Haptics/UxrHapticRouting.cs`:

```csharp
// VR Battlegrounds patch 65: единая точка перехвата вибрации.
public enum UxrHapticRequestKind { Clip, ClipType, Raw, Stop }
public readonly struct UxrHapticRequest
{
    public readonly UxrHapticRequestKind Kind;
    public readonly UxrControllerInput Source;      // чей компонент вызвали (→ Avatar)
    public readonly UxrHandSide Side;
    public readonly UxrHapticClip Clip;             // Kind == Clip
    public readonly UxrHapticClipType ClipType;     // Kind == ClipType
    public readonly float Frequency, Amplitude, DurationSeconds;
    public readonly UxrHapticMode Mode;
}
public static class UxrHapticRouting
{
    // true — запрос принят игрой, SDK устройство не трогает. null — SDK работает как без патча.
    public static Func<UxrHapticRequest, bool> Router;
    public static bool TryRoute(in UxrHapticRequest request) => Router != null && Router(request);
}
```

Точки вызова `if (UxrHapticRouting.TryRoute(...)) return;` — первой строкой:

| Файл | Метод | Что ловит |
|---|---|---|
| `Devices/UxrControllerInput.cs:413` | `SendHapticFeedback(side, UxrHapticClipType, …)` — до `StartCoroutine` | все `ClipType` до разворота в корутину: UI-клики, сорванный хват, `UxrHapticOnImpact`, запасной путь клипов |
| `Devices/Integrations/UxrUnityXRControllerInput.cs:167` | override `SendHapticFeedback(side, UxrHapticClip)` | выстрел, помпа, хват/укладка, `UxrControlFeedback` |
| там же `:246` | override `SendHapticFeedback(side, freq, amp, dur, mode)` | сырые: манипуляция планшета, перетаскивание UI, `UxrFixedHapticFeedback` |
| там же `:316` | override `StopHapticFeedback(side)` | остановки SDK |

- **Правило маршрутизатора:** установленный маршрутизатор принимает **каждый** запрос (возвращает `true`), в том числе
  чтобы отбросить чужой. Поэтому корутина SDK для принятых запросов не стартует, и её шаги повторно не маршрутизируются.
  Без маршрутизатора (`null`) всё как до патча: текущая отдача не ломается, пока сервиса нет.
- Сервис не зовёт SDK для вывода — у него свой `UnityXRHapticDevice`. Рекурсии нет.
- `UxrFirearmWeapon`, `UxrShotgunPump`, `UxrWeapon.Custom.cs` патч **не трогает** (это области WeaponSystem, патчи 53 и E).
- SteamVR (`UxrSteamVRControllerInput`) не патчится: не входит в Android-сборку. Запись об этом — в `changelog/<дата>-sdk-65.md` задачи.

### 5.3 Перевод запросов SDK в сигналы (игровой код, `SdkHapticTranslator`)

1. `Source.Avatar != UxrAvatar.LocalAvatar` → отбросить. Это заодно чинит класс ошибки: клик сорванного хвата
   `UxrGrabManager:749` шёл через `grabber.Avatar.ControllerInput` без проверки локальности [факт].
2. `Clip` с `FallbackClipType ∈ {Shot, ShotBig, ShotBigger}`, и рука держит `UxrFirearmWeapon` → **отдача SDK**. Если у
   оружия `IWeaponRecoilHapticsOwner.OwnsRecoilHaptics` → отбросить; иначе `Sdk<тип>` с gain = амплитуда клипа.
3. Прочие `Clip`/`ClipType` → `Sdk<тип>` (физический), gain = амплитуда. `None` → отбросить.
4. `Raw` → `SdkRaw` (положительный) с амплитудой и длительностью запроса.
5. `Stop` → завершить голоса SDK этой руки, игровые голоса не трогать.

Известное ограничение: по типу клипа нельзя отличить клик сорванного хвата (отрицательный по смыслу) от клика помпы — оба
`Click 1.0` и оба станут физическими. Если в шлеме это мешает — отдельная метка источника в `UxrGrabManager`
(следующий свободный номер патча).

## 6. Правило «единственный владелец» и тест-сторож

**Предложение правила** (общие правила меняет их сопровождающий по поручению пользователя; до этого правило держат
тесты-сторожа ниже): «Вибрация — только `HapticService.Play/PlayBoth/Begin` с Id из `HapticCatalog`.
Прямые `SendHapticFeedback`, `SendGrabbableHapticFeedback`, `StopHapticFeedback`, `SendHapticImpulse`,
`SendHapticBuffer`, `StopHaptics` вне `UnityXRHapticDevice` запрещены. Проверка — `HapticOwnershipTests`».

**`HapticOwnershipTests`** (EditMode, структурный — пишется сразу, по образцу `MenuPermissionsTests` [факт]):

1. Регулярное выражение по `Assets/Scripts/**/*.cs` и `Assets/Editor/**/*.cs`, строки-комментарии пропускаются.
   Разрешён только `Haptics/UnityXRHapticDevice.cs`.
2. **Временный белый список** — файлы, которые удаляет или переводит WeaponSystem: `AutomaticWeaponSlideFeedback`,
   `WeaponAttemptFeedback`, `WeaponChamberingReminder`, `BarrelObstruction`, у каждого ссылка на этап. Тест падает, если
   файл из списка больше не содержит вызова или удалён: устаревшие исключения не копятся.
3. Патч 57 на месте: в четырёх точках п. 5.2 есть `UxrHapticRouting.TryRoute` (проверка текста файлов SDK).
4. Каталог полон и корректен (п. 3.1).

Поведенческие тесты `HapticMixer` (приоритет, пауза непрерывного, максимум физических, кулдаун, локальная рука,
уничтоженный owner, выключенная вибрация) — **после** проверки механики в шлеме (решение 2026-10-02).

## 7. Сеть

- Вибрация строго локальна и по сети не передаётся.
- Цель — рука `UxrAvatar.LocalAvatar` с `AvatarMode == Local`. `Play(id, grabber)` с грабером чужого аватара или бота —
  no-op. Поэтому обработчики синхронизируемых событий (идут на каждой машине) могут звать сервис без своей проверки.
- `StateEventAuthority.IsAuthorOfItem` как фильтр **не используется**: на хосте он даёт true для ничейного предмета и
  ботов [факт: `StateEventAuthority.cs:85`].
- Авторская проверка остаётся обязанностью источника события, чтобы не продублировать само событие. Фильтр руки — вторая
  страховка.
- Dedicated/headless-сервер: сервис не ставится, маршрутизатор `null`, все вызовы — no-op.

## 8. Настройка сигналов: инспектор каталога в Play Mode

**Настроек вибрации для игрока нет** (решение пользователя 2026-10-07, п. 8.3 исследования). Все параметры — настройка
проекта в `HapticCatalog`. Подбирает их разработчик в Play Mode через Quest Link, держа контроллер.

Экран планшета для этого отменён (решение пользователя 2026-10-08: кликать шагами по 10 мс неудобно). Вместо него —
**инспектор ассета** `HapticCatalogEditor` (`Assets/Editor/VR_Battlegrounds/Haptics/`), меню
`Tools/VR Battlegrounds/Haptics/Каталог вибрации`:

- вверху в Play — живое состояние рук: виден ли контроллер, сколько голосов, какая амплитуда уходит в мотор;
- сигналы списком, у каждого: ползунки силы (0..1) и длительности (10..3000 мс) каждого отрезка, добавить/убрать отрезок,
  повтор (0..3000 мс), кулдаун, множитель второй руки; проба «Левая/Правая/Обе», у непрерывного — «Старт/Стоп»;
- правка сразу действует на контроллеры: `HapticCatalog.Changed` → `HapticMixer.Rebind` перепривязывает звучащие голоса
  к пересозданным записям (Unity пересоздаёт объекты записей при правке ассета), поэтому карман меняет силу под рукой;
- запись на диск — при выходе из Play (`HapticCatalogAutoSave`) и кнопкой «Записать ассет сейчас»;
- класс сигнала в инспекторе не меняется: это смысл сигнала, его правят в коде вместе с решением.

## 9. Все источники вибрации

Проверено по планам в `.agent-state/coordination/*.md` на 2026-10-07: кроме WeaponSystem, никто не правит перечисленные
файлы. WeaponSystem забирает оружие ([план](../../Docs/tasks/weapon-system-refactor-plan.md): таблица исполнителей, строка «Вибрация
механизма…», и этапы D, F5, H). Строка плана «вибрация выстрела остаётся у SDK» и комментарий `WeaponHapticCue`
устарели после решения п. 8.2 исследования — их правит владелец WeaponSystem при согласовании контракта п. 4.2.

| # | Источник | Сигнал(ы) | Статус |
|---|---|---|---|
| 1 | `Interaction/PocketHaptics` | `PocketReady`, непрерывный `Begin/End` на руку, пока рука в зоне готового кармана | **переводит haptics**, пункт 1 |
| 2 | `Weapons/WeaponAttemptFeedback` | → `WeaponNotReady` через исполнитель | **удаляет WeaponSystem** (D) |
| 3 | `Weapons/WeaponChamberingReminder` | → `WeaponNotReady` через исполнитель | **удаляет WeaponSystem** (H) |
| 4 | `Weapons/AutomaticWeaponSlideFeedback` | → `WeaponActionRear` | **удаляет WeaponSystem** (D, H) |
| 5 | `Weapons/BarrelObstruction` | → `WeaponObstructed`; у компонента остаётся только датчик | **переводит WeaponSystem** (D, `UseSensor`) |
| 6 | `Player/WallPass/WallPassFeedback` | `WallPassHint` / `WallPassViolating`, непрерывные на обе руки | **переводит haptics**, пункт 4 |
| 7 | `UI/HUD/WristDisplay` | `WatchInfo` / `WatchWarning` / `WatchCritical` по приоритету уведомления | **переводит haptics**, пункт 4 |
| 8 | `Player/Ghost/GhostViewEffect` | `PlayerDeath` на обе руки | **переводит haptics**, пункт 4 |
| 9 | `Debug/DebugMode/DebugGestureInput` | `DebugModeOn` / `DebugModeOff` | **переводит haptics**, пункт 4 |
| 10 | `Weapons/Core/WeaponOutput` → `WeaponFeedbackExecutor` | таблица п. 4.2 | **WeaponSystem** (D) по контракту п. 4.2 |
| 11 | SDK `UxrFirearmWeapon` (отдача) | `SdkShotBig` → по стволам `Recoil*` через WeaponSystem | **haptics**: перехват (пункт 3); **WeaponSystem**: отдача по стволам (D–F5) |
| 12 | SDK `UxrShotgunPump` | `SdkClick` / `SdkSlide` | **haptics**: перехват (пункт 3); **WeaponSystem** снимает помпу (D, F5) |
| 13 | SDK `UxrManipulationHapticFeedback` (хват/укладка, планшет) | `SdkClick`, `SdkRaw` | **haptics**: перехват (пункт 3) |
| 14 | SDK `UxrGrabManager` (сорванный хват) | `SdkClick` + фильтр чужих | **haptics**: перехват (пункт 3) |
| 15 | SDK `UxrPointerInputModule` (UI планшета) | `SdkClick`, `SdkRaw` | **haptics**: перехват (пункт 3) |
| 16 | SDK `UxrGrenadeWeapon`, `UxrHapticOnImpact`, `UxrFixedHapticFeedback`, `UxrGrabbableResizable` | `Sdk*` | перехват (пункт 3); в префабах не используются [факт по исследованию] |

## 10. Этапы реализации

Каждый пункт: компиляция и `AndroidCompileGate` на worker → проверка в шлеме → вливание в `origin/dev`.

| # | Пункт | Области | Шлем |
|---|---|---|---|
| 1 | **Сервис, каталог, карман 10 %.** `HapticService`, `HapticMixer`, `IHapticDevice`/`UnityXRHapticDevice`, фасад `Haptics`, `HapticCatalog` + ассет, перевод `PocketHaptics`. Тест-сторож и тест каталога (структурные) | новые `Assets/Scripts/Haptics/**`, `Assets/Resources/HapticCatalog.asset`, `Interaction/PocketHaptics.cs`, `Assets/Tests/EditMode/Haptics/**` | карман: ровная вибрация всё время, пока рука у готового кармана; клик хвата обрывает её не дольше чем на 100 мс (до пункта 3) |
| 2 | **Инспектор каталога** (п. 8) | `Assets/Editor/VR_Battlegrounds/Haptics/HapticCatalogEditor.cs`; `HapticCatalog.Changed`, `HapticMixer.Rebind`, диагностика `HapticService` | **подбор силы кармана** в Play через Link ползунками; правка сразу слышна; значения остаются в ассете после выхода из Play |
| 3 | **SDK-патч 65 и перевод SDK.** `UxrHapticRouting`, 4 точки, `SdkHapticTranslator`, `IWeaponRecoilHapticsOwner` | SDK `Devices/UxrControllerInput.cs`, `Devices/Integrations/UxrUnityXRControllerInput.cs`, новый `Haptics/UxrHapticRouting.cs`; `Assets/Scripts/Haptics/**`; `changelog/<дата>-sdk-65.md` | отдача, помпа FABARM, хват/укладка, UI планшета ощущаются как раньше; карман больше не обрывается кликом хвата; отдача автомата не «рвётся» |
| 4 | **Остальные источники.** `GhostViewEffect` (гибель 1,5 с), `WallPassFeedback`, `WristDisplay`, `DebugGestureInput` | файлы из таблицы п. 9 | гибель — 1,5 с на максимуме; стена: двойные импульсы, отдача слышна между ними; часы: тихое vs двойное |
| 5 | **Поведенческие тесты** принятой механики `HapticMixer` и `SdkHapticTranslator` | `Assets/Tests/EditMode/Haptics/**` | — |
| 6 | **Перенос в Docs** принятого поведения (отдельный этап с владельцем области, п. 12) | `Docs/troubleshooting.md` (карман) | — |
| — | **WeaponSystem (не haptics):** исполнитель по контракту п. 4.2, новые cue `Shot`/отказ по темпу, `IWeaponRecoilHapticsOwner` на хосте; удаление пунктов 2–5 таблицы п. 9 и их строк белого списка | области WeaponSystem | чек-лист этапа D |

Пункты 1 и 2 — один этап плана `service-pocket-tuning`; первыми: пользователь сразу подбирает силу кармана в шлеме. Пункт 3 не зависит от WeaponSystem и не трогает
его файлы.

## 11. Проверки

| Что | Когда | Как |
|---|---|---|
| Компиляция | каждый пункт | worker: `AndroidCompileGate.Run()` |
| Сторож, каталог, наличие патча 65 | с пункта 1 (патч — с 3) | `HapticOwnershipTests`, `HapticCatalogTests` (EditMode) |
| Меню | пункт 2 | `MenuDesignRulesTests`, `MenuContainmentTests`, `MenuWiringTests` |
| Механика | каждый пункт | чек-лист шлема в п. 10; диагностика — `GameLog.Player.Verbose` сервиса (Id, рука, причина отказа) |
| Поведение смешивания | после приёмки | пункт 5 |
| Возможности устройства | пункт 1 | один `Verbose` при старте: `TryGetHapticCapabilities` (каналы, `supportsBuffer`) — закрывает гипотезы исследования п. 1.1–1.2 |

## 12. Перенос в Docs и общие правила (после приёмки)

Общие индексы, журналы и правила задача не правит. После приёмки поведения — отдельный этап `docs-transfer` с владельцем
области; без истории разработки:

- `Docs/troubleshooting.md`, симптом «Карман не вибрирует»: вибрация непрерывная, пока рука у готового кармана (сигнал
  `PocketReady` через `HapticService`; сила — в `HapticCatalog`, под отдачей и отказами на паузе). Диагностика — Verbose
  канала `Player`: `[PocketHaptics] … готов` и `[Haptics] PocketReady → …`.
- Сопровождающему правил — предложение правила п. 6 («Вибрация — один владелец мотора») в `Asset rules`
  `.agents/rules/project-workflows.md`; индекс `Docs/README.md` — строки классов `Assets/Scripts/Haptics/`,
  `HapticCatalogEditor`, тестов `Haptics/*` (тексты — в `changelog/` задачи).

Решения пользователя 2026-10-07: модель, числа и API утверждены; настроек вибрации для игрока нет, подбор — дебаг-экраном,
с 2026-10-08 — инспектором каталога в Play Mode (п. 8); сырые непрерывные SDK (`SdkRaw`) — положительный класс.
