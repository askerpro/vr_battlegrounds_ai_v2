# WeaponSystem: архитектурный план рефакторинга оружия

Предлагаемый путь: `Docs/tasks/weapon-system-refactor-plan.md`.
Дата: 2026-10-07. Статус: **план принят** (В1–В11 по рекомендациям); этап B (машина) принят — п. 5.1; этап C (теневой режим) проверен в шлеме, расхождения S1–S4 — п. 5.2; этап D (`drive`) написан, Unity-проверка ждёт worker — п. 5.3.

Всё ниже прочитано в коде worktree `F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai`. В этом worktree есть незакоммиченная правка другого агента (HoldOpen: `HoldsEmptyActionOpen` / `OwnsActionPose` / `ResetVisuals → Deferred`). План исходит из того, что эта правка применена. Выводы, помеченные «гипотеза», получены чтением кода; их нужно проверить пробой. Прогона не было.

Связанные документы:
- действующий дизайн готовности: `Docs/tasks/weapon-readiness-feedback-design.md`, этапы 1–3 приняты как зависимость;
- патчи SDK: `Docs/UltimateXR/sdk-patches.md` (ledger 2026-10-04, этап 3, патч 49);
- образец единственной таблицы переходов: `RoundPhases.Transitions` в `Docs/gameplay.md`.

---

## 0. Коротко

- **Все решения принимает одна машина `WeaponStateMachine`.** Это чистый C# в отдельной сборке с `noEngineReferences: true`, поэтому «без Unity» гарантирует компилятор. Таблица переходов одна, как `RoundPhases.Transitions`.
- **Учёт остаётся в SDK.** M, C, флаги, revision, синхронизация, replay и поздний вход ведёт `UxrFirearmWeapon.Readiness` (обоснование в п. 2.4). Механическое состояние машины не хранится отдельно, а каждый раз выводится из учёта. Машина сама хранит только жест руки, эпизод спуска и фазу показа. Поэтому машина и учёт не могут разойтись.
- **На оружии один хост `WeaponSystem` (MonoBehaviour).** Датчики и исполнители — обычные C#-объекты внутри хоста, а не компоненты префаба. Поэтому второго исполнителя канала на префабе быть не может.
- **Порядок работы:**
  1. машина;
  2. теневой режим на Herrington и FABARM (уже на ledger) — машина работает рядом и только пишет в лог расхождения;
  3. переключение этих двух стволов на машину;
  4. перенос эпизода спуска из SDK в машину;
  5. перевод остальных стволов волнами по типу затвора.

  Старые этапы 4–5 входят в волны, поэтому каждый ствол переводится и проверяется в шлеме один раз.

---

## 1. Инвентаризация

Обозначения в столбце «Пишет»: П — поза, З — звук, В — вибрация/подсветка, У — учёт, С — сеть.

| Компонент (строк) | Что решает | Что хранит | Пишет | С кем конфликтует |
|---|---|---|---|---|
| `UxrFirearmWeapon` + `.Readiness.cs` (500) + `.AmmoAdmission.cs` (201), SDK | Учёт: проверка каждой фиксации, перенос M→C, расход при выстреле, подача у Semi/Auto. Через `UxrWeapon.Custom.cs:244` `ProcessReadinessLocalTrigger` — эпизод спуска (MustRelease/Accepted). `QueryReadinessDecision` — причина отказа. Legacy: `switch` по режиму огня и `HasReloaded` | `RuntimeTriggerInfo.Readiness` (C, Pending, ActionOpen, PostShotEmptyAction, последовательности, Revision), `HasReloaded`, таймеры | У, С (`ApplyReadinessCommit`, `CommitShotSynced`, `CommitAmmoAdmission`). З: звук выстрела, сухой щелчок в запасной ветке (`UxrWeapon.Custom.cs:300`). В: вибрация выстрела | Два режима в одном классе (legacy и ledger). Звук сухого щелчка имеет запасную ветку SDK, `WeaponAttemptFeedback` и `BarrelObstruction`. Событие `ChamberingRequired` и router выдают одну и ту же причину |
| `UxrFirearmMag` (SDK) | — | `Rounds` — единственное хранилище M | У (через `WriteLedgerRounds`) | — |
| `UxrShotgunPump` (SDK) | Цикл помпы: свои пороги 0,7 и 0,9, вызов `Reload` | `State` | У (`Reload`), З, В | Своя второй порог и свой аудиоканал. На FABARM выключен, работает только у SDK Shotgun |
| `WeaponReadinessController` (845) | Политика (ManualReturn / AutoOnMagazineInsert / TriggerAssist), распознавание ручного цикла (Begin, Extract, Complete, CloseOnly, EmptyRest), отмены, подготовка для ботов, ведение Empty-показа, `HoldsEmptyActionOpen` | ~25 локальных флагов: `_cycleActive`, `_manualContact`, `_manualRear`, `_rearGateReached`, `_manualOpenedRear`, `_emptyReturn`, связка с автоматикой бота и др. | У (команды ledger), П (через return driver и команды показу) | Решения разбросаны по флагам. В одном классе смешаны машина состояний, датчик и режиссура показа |
| `WeaponMechanismVisuals` (892) | Какой клип играть после выстрела (Fire или Empty — по `GetTotalAmmoLeft==0`), привязка ручного хода, legacy «зацеп», фаза `EmptyPresentationWork` (None/Source/Deferred/HoldSettled/RestServiced/ReturnDriver). Плюс фабрика привязок для редактора (`TryBuildPreparedActionMapping`) | `_manual`, `_emptyHeld`, `_catchRequired`, `_emptyShot`, `_ownedChamberReturn`, `_suppressActionCycle`, `_emptyWork`, `_started` | П (Action-детали, нерычажные детали, а через `HoldVisualPose`/`BeginVisualHandoff` — и ручка) | Пружина, return driver и `ResetVisuals` пишут ту же позу. Ветки `HasLedgerAdapter` почти в каждом методе. Здесь возникли ошибки классов 1 и 4 |
| `AutomaticWeaponSlideFeedback` (481) | Датчик хода ручки. Legacy: сам решает цикл и вызывает `Reload`. Ledger: пружина и проброс `RefreshPhysicalActionState` | `_localStart`, `_state`, `_cosmeticHold`, `_pendingClose` | П (пружина, удержание, перехват ручки), З (затвор), В, У (legacy `Reload`) | Пружина против HoldOpen (класс 1). В режиме ledger не играет звук оттяжки (ниже, Н1) |
| `ChamberPoseReturnDriver` (123) | Возврат зафиксированной позы к покою | Зафиксированные позы | П (ручка и привязки) | Четвёртый писатель позы |
| `ChamberActionBinding` (128) | Геометрия покой→зад, прогресс | Данные | — | — |
| `ChamberCompletionEvidence` (55) | Одноразовое подтверждение физики для SDK | — | — | — |
| `PumpGrabFollow` (117) | Помпа следует за смещением руки (обход ошибки SDK Control Parent Direction) | Позы в момент хвата | П (помпа в руке, `ConstraintsApplied`, на каждой машине) | Пересекается с перехватом WMV и driver, если ручка не в руке |
| `MagazineEject` + `MagazineEjectInput` | Какой магазин выбросить (кроме fixed store), проверка автора | — | С (`RemoveObjectFromAnchor`) | — |
| `WeaponTriggerAttemptRouter` (107) | Превращает NotReady в PrepareOnly для TriggerAssist, команда подготовки, публикация попытки | Политика | — (вызывает `RequestChamber`) | Ещё одно место решения о спуске рядом с SDK |
| `WeaponTriggerAttemptContext` | DTO | — | — | — |
| `WeaponAttemptFeedback` (76) + `WeaponFeedbackProfile` | Реакция на причину отказа | Задержка вибрации | З (сухой щелчок), В | Сухой щелчок также у SDK и `BarrelObstruction` |
| `WeaponChamberingReminder` (156) | Защёлка подсказки | `_latched`, руки | В (подсветка Action, вибрация) | Два входа: legacy-событие SDK и typed router |
| `ShotgunPellets` (113) | План дроби (`AdditionalShotPlan`) / legacy-дробь по `ProjectileShot` | — | С (через shot commit) | — |
| `CartridgeIntake` (292, NetworkBehaviour) | Окно приёма: предпросмотр, клиентский запрос, проверка на сервере, удаление принятых патронов | Токены, запрос, удаляемые объекты | С (Mirror Cmd/TargetRpc), У (через `TryAcceptAmmoUnit`) | Класс 5 (имя Cmd → 16-битный хэш) |
| `Cartridge` / `UxrFirearmAmmoUnit` | Свойства единицы патрона / флаг consumed | `LastHandlingAvatar` | П (скрытие израсходованного) | — |
| `WeaponInternalAmmoEjection` | Поза внутреннего патрона при Remove | — | П | — |
| `RecoilAccumulator` | Отдача (поворот корпуса), только у стрелка | `RecoilPattern` | П (корпус) | — (канал отдельный) |
| `BarrelObstruction` + `WeaponUseBlocker` | Препятствие у ствола → `IsUseBlocked` | — | З (сухой щелчок), В | Второй хозяин сухого щелчка; взаимоисключение держится только неявно на `CanUse=false` |
| `WeaponMagazineAnchorHighlight` | Подсветка гнезда | — | В | — |
| `AnchorSound` + `MagazineAnchorSoundDefaults` | Звук вставки/выемки у приёмника | — | З | Класс 2 (`UxrAudioManipulation` на предмете). Держится на тесте `AmmoInsertSoundTests` |
| `UxrMagazinePocket` | Выбор магазина по тегу, размещение по валидаторам | — | — | Класс 3 (тег против валидаторов) |
| `BotGunner` | Подготовка через `RequestAutomationPreparation`, прямой `TryToShootRound` | — | У (через SDK) | — |
| Сборка в редакторе: `HandsPackWeaponBuilder`, `HandsPackLegacyRebuild`, `KinemationWeaponBuilder`/`KinemationFixReview`, `ManualLoadingAuthoring`, `WeaponInteractionInstaller`, `AnchorSoundInstaller`, `WeaponReadinessAuthoring`, `WeaponReadinessMigration` | — | — | Компоненты префаба | Семь мест добавляют оружейные компоненты |

Какие стволы на каких компонентах (по GUID):

| Группа | Состав |
|---|---|
| Ledger | Herrington, FabarmSDASS |
| AWSF + WMV (есть клипы Motion) | AK105, AR15/M16, BrowningHiPower, Mk14, MKR9, SRM12, TR15, Viper |
| Только AWSF | PPK, MP5K, Scar, Uzi, SniperRifle |
| Без затвора | SDKGun, Machinegun |
| Legacy | Revolver, R08 (WMV — барабан), SDK Shotgun (`UxrShotgunPump`) |

### Дополнительно найдено при чтении (тот же класс «разрозненные владельцы»)

| № | Наблюдение | Где | Статус |
|---|---|---|---|
| Н1 | В режиме ledger оттяжка затвора не звучит. Закрытие без досылания тоже молчит. Звучит только закрытие с досыланием C0→C1 | `AutomaticWeaponSlideFeedback.LateUpdate` (ветка ledger выходит до аудио), `NotifyLedgerManualCompletion` | по коду |
| Н2 | Наблюдатели не слышат затвор и помпу: аудио играет только машина автора | AWSF `HasManualContext`, `UxrShotgunPump` | по коду |
| Н3 | На `ReadinessFaulted` никто не подписан. Оружие в состоянии сбоя молча отказывает в каждом спуске | grep по `Assets/Scripts` | по коду |
| Н4 | `RefreshPhysicalActionState` вызывают три места за кадр: AWSF.LateUpdate (порядок 0), контроллер (210), SDK перед выстрелом. Результат зависит от порядка выполнения | AWSF:221, Controller:352, `UxrWeapon.Custom.cs:262/273` | по коду |
| Н5 | «Пусто после выстрела» определяется двумя признаками: `GetTotalAmmoLeft==0` (WMV) и `PostShotEmptyAction` (ledger) | WMV:576 | по коду |
| Н6 | Если досылание пришлось на барьер приёма патрона: `TryCompleteChamber` отклоняется, флаг цикла сгорает, затем Cancel → CloseOnly. Патрон не дослан | Controller `CompleteOwnedCycle` + `CanOwnReadiness` | **гипотеза**, проверить пробой |

---

## 2. Целевая архитектура

### 2.1 Слои и направление зависимостей

```
Данные (ScriptableObject/serialized)   WeaponReadinessProfile (оси), WeaponFeedbackProfile, WeaponMechanismMotion,
                                       WeaponMechanismRig (serialized на хосте), WeaponAudioSet
        ▲
Машина (сборка VrBattlegrounds.Weapons.Core, noEngineReferences)
        WeaponStateMachine, WeaponTransitions.Table, WeaponProfileAxes, LedgerView, ActionSample,
        WeaponEvent, LedgerCommand, WeaponEffect
        ▲ (только хост ссылается на машину; машина не знает SDK/Unity)
Хост (VrBattlegrounds.asmdef)  WeaponSystem : MonoBehaviour  — тик: датчики → Step → команды (только автор) → исполнители
        ├─ порты учёта:   IWeaponLedgerPort → UxrReadinessLedgerPort / UxrMagazineOnlyPort (legacy-хранилище)
        ├─ датчики:       ActionSensor, MagazineSensor, TriggerSensor (через SDK-seam), IntakeSensor (CartridgeIntake), UseSensor (WeaponUseBlocker/BarrelObstruction), ContextSensor (автор/рука/CanUse)
        └─ исполнители:   WeaponPoseExecutor, WeaponAudioExecutor, WeaponFeedbackExecutor  (plain C#, по одному на канал)
        ▼
SDK UltimateXR   UxrFirearmWeapon(.Readiness/.AmmoAdmission), UxrFirearmMag, UxrProjectileSource, state sync
        ▼
Транспорт        NetworkStateRelay (UXR-события), CartridgeIntake (единственные Mirror Cmd оружия)
```

Правила:
- SDK не ссылается на игру.
- Машина не ссылается ни на SDK, ни на Unity. Причины отказа — собственный `WeaponNotReadyReason`, порт переводит его в `UxrFirearmNotReadyReason`.
- Исполнители не читают учёт и не принимают решений: они получают цель от машины.
- Датчики только измеряют.

### 2.2 Ключевые типы и сигнатуры

```csharp
// VrBattlegrounds.Weapons.Core (нет UnityEngine)
public enum WeaponFireMode { Semi, Auto, Manual }               // берётся из UxrShotCycle при Configure, не дублируется в профиле
public enum WeaponAmmoCapability { MagazineOnly, DetachableMagazineChamber, FixedStoreChamber }  // MagazineOnly = нынешний LegacyAmmo
public enum WeaponPhysicalCapability { NoAction, ActionTravel }
public enum WeaponChamberPolicy { ManualReturn, AutoOnMagazineInsert, TriggerAssistPrepareOnly }
public enum WeaponEmptyPose { HoldOpen, ReturnToRest }
public readonly struct WeaponProfileAxes {
    FireMode, Ammo, Physical, Policy, EmptyPose;
    float ExtractionGate, Epsilon, EmptyRearTime, EmptyClipDuration, FireClipDuration;
    bool HasEmptyClip, HasFireClip;
    static bool TryValidate(in WeaponProfileAxes a, out string error);
}
public readonly struct LedgerView {        // снимок SDK, машина его не хранит
    bool Initialized, Chamber, ActionOpen, CyclePending, SlideLocked /*PostShotEmptyAction*/, Faulted, AdmissionPending, MagazinePresent;
    int MagazineRounds, Capacity; uint Revision, CycleSequence, ExtractedCycle, ShotSequence; int MagazineToken;
}
public readonly struct ActionSample { bool Held, HeldByAuthorMainContext; float HandleProgress, MinRequiredProgress; bool AllAtRest, AtValidatedRear; }
public readonly struct WeaponContext { WeaponRole Role; bool CanUse, MainGripLocal, InsideReplay; WeaponBlockReason Blocked; float RofTimer; }
public enum WeaponRole { Author, Observer }
public enum WeaponEventKind { Configured, SnapshotLoaded, LedgerCommitted, CommandRejected, Tick, ActionSampled,
    HandleGrabbed, HandleReleased, MagazineChanged, TriggerPressed, TriggerHeld, TriggerReleased,
    ContextLost, AuthorityChanged, CartridgeOffered, AdmissionResolved, AutomationPrepareRequested, Disabled }
public readonly struct WeaponEvent { WeaponEventKind Kind; LedgerOp Op; /*для LedgerCommitted*/ bool OpChamberBefore, OpChamberAfter; float Dt; int Token; }
public enum LedgerCommandKind { Initialize, BeginAction, Extract, CompleteChamber, CloseOnly, AckEmptyRest, Cancel, Shoot, RefillForAutomation, RequestAdmission }  // Reconcile удалён на этапе C2
public readonly struct LedgerCommand { LedgerCommandKind Kind; uint ExpectedRevision, CycleSequence; int ExpectedMagazineToken; }
public enum WeaponCue { ActionBack, ActionForwardChambered, ActionForwardEmpty, ChamberEjected, SlideLockCatch, DryFire }  // нет Insert/Shot — у них другие владельцы
public readonly struct PoseTarget { PosePresentation Kind; float ClipTime; float ReturnSpeed; }
public interface IWeaponOutput { void Command(in LedgerCommand c); void Pose(in PoseTarget t); void Cue(WeaponCue cue, WeaponNotReadyReason reason); void Haptic(WeaponHapticCue h); void Hint(bool on); }
public sealed class WeaponStateMachine {
    public WeaponStateMachine(in WeaponProfileAxes axes);
    public WeaponMachineState State { get; }                      // только локальные регионы
    public static MechanismState Derive(in LedgerView l, in WeaponProfileAxes a);
    public void Step(in WeaponEvent e, in LedgerView l, in ActionSample s, in WeaponContext c, IWeaponOutput o);  // без аллокаций
}
public static class WeaponTransitions { public static readonly TransitionRow[] Table; }  // каждая строка: From, On, Guard, Apply, Doc

// VrBattlegrounds (Unity)
public interface IWeaponLedgerPort { LedgerView Read(); bool Execute(in LedgerCommand c, in PhysicalEvidence e); event Action<LedgerCommitInfo> Committed; event Action SnapshotLoaded; }
[DisallowMultipleComponent, DefaultExecutionOrder(210)]
public sealed class WeaponSystem : MonoBehaviour {     // файл переименован из WeaponReadinessController, .meta/GUID тот же
    [SerializeField] WeaponReadinessProfile _profile; [SerializeField] WeaponMechanismRig _rig; [SerializeField] WeaponAudioSet _audio;
    [SerializeField] WeaponFeedbackProfile _commonFeedback, _feedbackOverride;
    public bool Accepts(string itemTag);                       // статическое «подходит» (класс 3)
    public bool CanAcceptNow(UxrGrabbableObject item);         // динамическое «можно сейчас» (машина + ёмкость + барьер)
    public bool RequestAutomationPreparation();                // контракт BotGunner сохранён
    public MechanismState Mechanism { get; }                   // для тестов и диагностики
}
```

### 2.3 Граница SDK и игры

| Остаётся в SDK | Уходит в игру (машину) |
|---|---|
| Хранилище M (`UxrFirearmMag.Rounds`) и C/флагов (`RuntimeTriggerInfo.Readiness`) | Политика досылания, распознавание ручного хода, порог извлечения |
| Проверка каждой фиксации (revision, identity, ёмкость, точный after-state по операции) | Эпизод спуска (MustRelease/Firing/Consumed), этап E |
| Атомарный выстрел `CommitShotSynced` + вложенный `Source.Shoot`, дробь `AdditionalShotPlan`, звук выстрела | Классификация NoMagazine/EmptyMagazine/ChamberingRequired (машина считает по `LedgerView`; `QueryReadinessDecision` остаётся только для обратной совместимости) |
| `CommitAmmoAdmission`, барьер приёма | Все звуки механизма, подсказки, вибрации отказа |
| Снимок, replay, отбрасывание дублей, IL2CPP `[Preserve]` | Вся поза Action |

Новые правки SDK (каждая — запись в `sdk-patches.md`):
- **E1, этап E.** Заменить `ProcessReadinessLocalTrigger` одним портом `Func<int, UxrGrabber, UxrTriggerInput, bool> DecideLocalTrigger`, где `UxrTriggerInput` = pressed/started/ended. Эпизод уходит в машину, объём патча SDK уменьшается.
- **E2, этап D, если проба покажет, что `StateChanged` у компонента не поднимается при replay.** Событие `ReadinessCommitted(int trigger, UxrFirearmReadinessCommit commit, bool replay)` после `WriteReadinessCommit` во всех трёх sink.

### 2.4 Учёт в SDK или в игре — рекомендация

| Вариант | Плюсы | Цена и риски |
|---|---|---|
| **A. Учёт в SDK (рекомендуется)** | Уже реализован и принят (этапы 1–3). Выстрел и снаряд атомарны в одном пакете; дубли отбрасываются; снимок позднего входа и `[Preserve]` под IL2CPP проверены. Хранилище одно, записывает его один писатель | Правила операций SDK повторяет при проверке (after-state по операции). Расхождение машины и SDK даёт отказ команды, а не молчаливое расхождение. Закрывается контрактным тестом «каждая команда машины принимается SDK». Патчи переносятся при обновлении SDK |
| B. Учёт в игре (свой `IUxrStateSave`/NetworkBehaviour) | Все правила в одном коде; SDK-патч меньше | Путь выстрела SDK всё равно читает боезапас: нужен provider-seam в выстреле, replay и снимке. Учёт окажется в двух компонентах, их снимки придётся сводить, атомарность «расход + снаряд» теряется. По сути это переделка этапа 1 (недели) с заново доказываемой сериализацией |

**Рекомендация A** с двумя условиями:
1. машина никогда не хранит копию учёта — механическое состояние выводится заново из `LedgerView` на каждом шаге;
2. из SDK убираются **решения** (эпизод спуска — этап E). Тогда SDK = хранилище, транспорт и выпуск снаряда.

### 2.5 Сеть

| Вопрос | Решение |
|---|---|
| Кто автор события | `StateEventAuthority.IsAuthorOfItem(weapon)`: машина держателя, без держателя — сервер или офлайн. Хост вызывает `IWeaponOutput.Command` только при `Role=Author && !InsideReplay`. В строках таблицы для `Observer` команд нет (инвариант И9) |
| Что реплицируется | Только фиксации ledger (UXR state sync → `NetworkStateRelay`), выстрел с дробью и `CommitAmmoAdmission` от сервера. Жест руки, эпизод спуска и фаза показа не реплицируются |
| Ручка, которую держит рука | Поза руки синхронизируется аватаром; SDK grab и `PumpGrabFollow` считают позу на каждой машине |
| Ручка и детали, которые не держат | Каждая машина считает позу сама: `WeaponPoseExecutor` по цели машины, выведенной из реплицированного учёта. Результат детерминирован (HoldOpen → зад, цикл → возврат) |
| Наблюдатель | Получает `LedgerCommitted` (фиксация по replay) → машина в роли Observer → поза и звук (ActionBack/Forward/SlideLock слышны всем, закрывает Н2). Сухой щелчок и подсказка только у автора |
| Поздний вход и загрузка | `ChangesSinceBeginning` уже содержит ledger. Событие `SnapshotLoaded` сбрасывает жест, ставит эпизод в Consumed (нужно новое нажатие) и выводит показ: `SlideLocked && HoldOpen` → сразу зад; иначе покой. Миграция не повторяется |
| Смена автора | `AuthorityChanged`: Cancel незавершённого цикла (если ещё автор), сброс жеста. Новый автор начинает с чистого жеста |
| Mirror | WeaponSystem не добавляет Mirror Cmd/Rpc. Единственные Cmd остаются у `CartridgeIntake`, имена не менять (класс 5) |
| Бот | `RequestAutomationPreparation` → событие `AutomationPrepareRequested` (только мир) → `RefillForAutomation`. Прямой `TryToShootRound` бота проходит проверку ledger; фиксация приходит машине как `LedgerCommitted` |

---

## 3. Спецификация машины

### 3.1 Оси профиля

| Ось | Значения | Источник | Недопустимые сочетания (`TryValidate`) |
|---|---|---|---|
| FireMode | Semi, Auto, Manual | `UxrShotCycle` спуска при Configure | — |
| AmmoCapability | MagazineOnly, DetachableMagazineChamber, FixedStoreChamber | профиль | FixedStore без fixed store или без CartridgeIntake |
| PhysicalCapability | NoAction, ActionTravel | профиль + проверка rig | NoAction + ManualReturn; NoAction + HoldOpen |
| ChamberPolicy | ManualReturn, AutoOnMagazineInsert, TriggerAssistPrepareOnly | профиль | MagazineOnly + любая политика, кроме AutoOnMagazineInsert (досылания нет) |
| EmptyPose | HoldOpen, ReturnToRest | профиль | HoldOpen без проверенной задней позы rig |
| ReleasedAction | Spring, Stay | профиль (до этапа D — `AutomaticWeaponSlideFeedback.AutoReturnOnRelease`) | NoAction + Stay |

Данные конкретного ствола (порог извлечения — бывший `_slideThreshold`, скорость возврата, ε, время задней позы Empty, длительности клипов) лежат в `WeaponMechanismRig` на хосте, не в общем профиле.

### 3.2 Переменные состояния

| Переменная | Хозяин | Сохраняется/реплицируется |
|---|---|---|
| M, C, ActionOpen, CyclePending, SlideLocked, CycleSeq, ExtractedSeq, ShotSeq, Revision, Faulted, AdmissionPending, магазин | SDK ledger (`LedgerView`) | да |
| `Gesture` ∈ {None, Contact, Pulling, PastGate, Released}, `Baseline`, `OpenedRear`, `RearEvidence`, `CycleSeqOwned`, `ExpectedMagazineToken` | машина (только автор) | нет, сброс на Snapshot/Authority |
| `Origin` ∈ {Manual, Insert, Assist, Automation} | машина (только автор) | нет |
| `Episode` ∈ {Armed, Firing, Consumed} | машина (только автор) | нет |
| `Presentation` ∈ {Rest, FollowHand, FireClip, EmptyClip, HoldRear, ReturnToRest}, `ClipTime`, `ClipShot` | машина (все роли) | нет; выводится заново при Snapshot/Disable |
| `HintLatched`, `HintHand` | машина (только автор) | нет |

### 3.3 Механические состояния (выводятся из учёта: `Derive(L, P)`)

| Состояние | Условие по учёту | Смысл |
|---|---|---|
| Uninitialized | ¬Initialized | до однократной миграции |
| Ready | C ∧ ¬Open ∧ ¬Pending | можно стрелять, если нет блокировки и таймера |
| Empty | ¬C ∧ ¬Open ∧ ¬Pending ∧ ¬Locked | причина: нет магазина → NoMagazine; M=0 → EmptyMagazine; иначе ChamberingRequired |
| HoldOpen | ¬C ∧ Locked ∧ ¬Open ∧ ¬Pending ∧ EmptyPose=HoldOpen | затвор на задержке |
| EmptyAwaitRest | ¬C ∧ Locked ∧ ¬Open ∧ ¬Pending ∧ (ReturnToRest ∨ NoAction) | ждёт покоя и подтверждения EmptyRest |
| CycleLoaded | Open ∧ Pending ∧ C ∧ ExtractedSeq≠CycleSeq | открыт до порога извлечения, патрон ещё в патроннике |
| CycleCleared | Open ∧ Pending ∧ ¬C | после порога или патронника не было; при Origin≠Manual это Preparing |
| OpenIdle | Open ∧ ¬Pending | цикл отменён, ждёт физического закрытия |
| Faulted | Faulted | команды запрещены до resync |

Поверх любого состояния: `Blocked` (CanUse=false: препятствие, стена) и `AdmissionPending`.

### 3.4 События

| Событие | Источник | Роль |
|---|---|---|
| Configured | хост `Start`/`OnEnable` | обе |
| SnapshotLoaded | `UxrStateSaveImplementer.StateSerialized` (reading) | обе |
| LedgerCommitted(op, before/after) | порт: свои и replay-фиксации | обе |
| CommandRejected | порт: `Execute`=false | автор |
| Tick(dt) | хост `LateUpdate` | обе |
| ActionSampled | `ActionSensor` (один раз за кадр + повторный замер перед выстрелом) | обе (в выводах автора используется только у автора) |
| HandleGrabbed/Released | `UxrGrabbableObject.Grabbing/Released` ручки | обе |
| MagazineChanged | `MagazineSensor` (anchor Placed/Removed после SDK) | обе |
| TriggerPressed/Held/Released | `TriggerSensor` через SDK-порт (этап E) | автор |
| ContextLost | потеря основной руки, `CanUse`, Disable | автор |
| AuthorityChanged | `StateEventAuthority` | обе |
| CartridgeOffered / AdmissionResolved | `IntakeSensor` (CartridgeIntake) | автор |
| AutomationPrepareRequested | `BotGunner` | автор-мир |
| Disabled | хост `OnDisable` | обе |

### 3.5 Таблица переходов

Столбец «Цель позы» — только при изменении. Все команды выдаются только при `Role=Author`, кроме явно отмеченных строк. Новое механическое состояние всегда = `Derive(L)` после фиксации; столбец «→» показывает ожидаемый результат.

**Инициализация и снимок**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T01 | Uninitialized | Configured [автор, anchor активен] | `Initialize(prepared = M>0 ∧ AllAtRest)` | Ready / Empty |
| T02 | Uninitialized | SnapshotLoaded [L.Initialized] | — (миграции нет) | Derive |
| T03 | любое | SnapshotLoaded | Gesture=None, Episode=Consumed, Hint off, Presentation=Derive (HoldOpen→HoldRear, иначе Rest) | Derive |
| T04 | любое | Disabled | Gesture=None; `Cancel`, если Pending; Presentation=Derive | Derive |

**Магазин**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T10 | Ready | MagazineChanged | — (SDK сам фиксирует MagazineChanged, C сохраняется) | Ready |
| T11 | Empty, HoldOpen, EmptyAwaitRest | MagazineChanged [M>0, AutoOnMagazineInsert] | `BeginAction(Insert)`; если AllAtRest — `CompleteChamber` в том же шаге | Preparing → Ready |
| T12 | Empty, HoldOpen, EmptyAwaitRest | MagazineChanged [иная политика или M=0] | пересчёт причины; Hint off, если причина ≠ ChamberingRequired | то же |
| T13 | CycleLoaded, CycleCleared | MagazineChanged | (SDK: Pending=false) Gesture=None | OpenIdle |
| T14 | OpenIdle | MagazineChanged [AutoInsert, M>0, ¬C] | `BeginAction(Insert)` — новый запрос, а не оживление старого | Preparing |

**Ручной ход (ActionTravel)**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T20 | Ready, Empty, HoldOpen, EmptyAwaitRest, OpenIdle | HandleGrabbed [контекст автора] | Gesture=Contact (Baseline=progress, RearEvidence=HoldOpen ∧ AtValidatedRear) | Presentation=FollowHand |
| T21 | Contact (Ready/Empty/EmptyAwaitRest) | ActionSampled [progress > Baseline+ε ∧ progress > ε] | `BeginAction(seq+1)`, Gesture=Pulling, OpenedRear=true | CycleLoaded / CycleCleared |
| T22 | Contact (HoldOpen) | ActionSampled [RearEvidence ∧ progress < Baseline−ε] | `BeginAction(seq+1)`, Gesture=PastGate (без извлечения) | CycleCleared |
| T23 | Contact (Ready, ручка не в покое после Fire-клипа) | ActionSampled [движение вперёд > ε] | `BeginAction(seq+1)`, Gesture=Pulling, OpenedRear=false | CycleLoaded |
| T24 | Contact | ActionSampled [движение < ε] | — (сам хват не цикл) | то же |
| T25 | Pulling | ActionSampled [MinRequiredProgress ≥ Gate ∧ OpenedRear] | `Extract(seq)`, если ExtractedSeq≠seq; Gesture=PastGate; вибрация зада | CycleCleared |
| T26 | PastGate | ActionSampled [AllAtRest ∧ ¬AdmissionPending] | `CompleteChamber(ExpectedMagazine)` | Ready (была подача) / Empty |
| T27 | Pulling | ActionSampled [AllAtRest] (порог не достигнут) | `Cancel` + `CloseOnly`; если ручку ещё держат — Gesture=Contact (Baseline=0) | Ready (C сохранён) / Empty |
| T28 | Pulling, PastGate | HandleReleased | Gesture=Released (признак прохождения порога сохраняется) | Spring: ReturnToRest(скорость пружины); Stay: Action остаётся (`Stay`), цикл ждёт хвата (S4) |
| T29 | Released | ActionSampled [AllAtRest] | как T26, если порог пройден, иначе как T27 | как T26/T27 |
| T30 | OpenIdle | ActionSampled [AllAtRest ∧ контекст] | `CloseOnly` | Ready / Empty |
| T31 | EmptyAwaitRest | ActionSampled [AllAtRest ∧ клипа нет (Empty, а без него Fire — S1) ∧ контекст] | `AckEmptyRest` | Empty |
| T32 | CycleLoaded, CycleCleared | ContextLost / AuthorityChanged | `Cancel` (если ещё автор); Gesture=None | OpenIdle |
| T33 | Gesture≠None | LedgerCommitted чужой фиксации [CycleSeq/Revision/магазин ≠ ожидаемых] | Gesture=None; `Cancel`, если Pending | Derive |
| T34 | PastGate / Released в покое | ActionSampled [AdmissionPending] | ничего (отложено, флаг цикла не сгорает; закрывает Н6) | то же |
| T35 | любое с Gesture≠None | CommandRejected | Gesture=None; `GameLog.WeaponSystem.Warning` (C2: сверки магазина нет — учёт его не хранит) | Derive |

**Подготовка (Origin ≠ Manual)**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T40 | Empty [причина ChamberingRequired] | TriggerPressed [новое нажатие, TriggerAssist, RofTimer≤0] | NotReady-отклик один раз; Episode=Consumed; `BeginAction(Assist)`; если AllAtRest — `CompleteChamber` | Preparing → Ready, но эпизод Consumed (И5) |
| T41 | Preparing | Tick [¬Held] | — | Presentation=ReturnToRest(AutoReturnSpeed) |
| T42 | Preparing | ActionSampled [AllAtRest] | `CompleteChamber` | Ready |
| T43 | Preparing | HandleGrabbed | возврат на паузе, после отпускания продолжается с текущей позы | Presentation=FollowHand |
| T44 | Empty, EmptyAwaitRest | AutomationPrepareRequested [мир, бот] | EmptyAwaitRest → возврат + `AckEmptyRest`; затем `RefillForAutomation` | Ready |

**Спуск (эпизод)**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T50 | Ready, Episode=Armed | TriggerPressed [¬Blocked, контекст] | повторный замер датчика; `Shoot`; Episode=Firing | по фиксации (T60–T62) |
| T51 | Ready, Episode=Firing | TriggerHeld [Auto, RofTimer≤0] | `Shoot` | то же |
| T52 | ¬Ready, Episode=Firing | TriggerHeld | Episode=Consumed (очередь останавливается, без серии откликов) | — |
| T53 | Empty, HoldOpen, EmptyAwaitRest | TriggerPressed [новое, RofTimer≤0, ¬Assist ∨ причина ≠ ChamberingRequired] | NotReady(причина) один раз → DryFire + вибрация по профилю; Hint latch при ChamberingRequired; Episode=Consumed | то же |
| T54 | CycleLoaded/Cleared, OpenIdle | TriggerPressed | Решение пользователя 2026-10-09: открытый или недовозвращённый Action — та же проблема, что недосланный патрон: NotReady(причина) как T53 — DryFire, вибрация, при ChamberingRequired подсветка Action; таймер темпа — T57 | то же |
| T54u | Uninitialized | TriggerPressed | OtherDenied, без отклика о патронах; Episode=Consumed | то же |
| T55 | любое | TriggerPressed [Blocked=Obstructed] | DryFire(Obstructed) + вибрация препятствия; Episode=Consumed | то же |
| T56 | любое | TriggerReleased | Episode=Armed | — |
| T57 | Ready [Semi/Manual], Empty, HoldOpen, EmptyAwaitRest | TriggerPressed [RofTimer>0] | Отказ `RateOfFire` (S2): звук `Refusal`, вибрация `RateOfFire` (отрицательный класс); не DryFire, не подготовка, без подсказки; Episode=Consumed | то же |
| T57a | Ready | TriggerPressed [Auto, RofTimer>0] | Episode=Firing без выстрела, очередь продолжит T51 (не отказ: выстрел ждёт темпа) | — |

**Результат выстрела (обе роли)**

| № | Из | Событие [условие] | Эффекты | → |
|---|---|---|---|---|
| T60 | Ready | LedgerCommitted(Shot) [после выстрела C=1] | Presentation=FireClip(0) | Ready |
| T61 | Ready | LedgerCommitted(Shot) [Manual, C=0, M>0] | FireClip(0) | Empty (ChamberingRequired) |
| T62 | Ready | LedgerCommitted(Shot) [Locked] | EmptyClip(0, ShotSeq); без Empty-клипа — FireClip(0) (S1); без клипов — сразу HoldRear/возврат | HoldOpen / EmptyAwaitRest |
| T63 | Presentation=EmptyClip | Tick [ClipTime ≥ (HoldOpen ? EmptyRearTime : EmptyClipDuration)] | HoldRear (+ SlideLockCatch) / ReturnToRest | — |
| T64 | Presentation=FireClip | Tick [ClipTime ≥ FireClipDuration] | Rest | — |
| T65 | FireClip / EmptyClip | HandleGrabbed | FollowHand для Action; нерычажные детали доигрывают клип | — |
| T66 | LedgerCommitted любой операции | — | звук по таблице 4.2 (обе роли) | Derive |

**Приём патрона и сбой**

| № | Из | Событие [условие] | Команды / эффекты | → |
|---|---|---|---|---|
| T70 | инициализировано, ¬Faulted, ¬AdmissionPending | CartridgeOffered [держатель-автор, Total < Capacity] | `RequestAdmission(token)` (барьер, Cmd через CartridgeIntake) | + AdmissionPending |
| T71 | AdmissionPending | AdmissionResolved | снятие барьера; повторная проверка отложенной T34 | Derive (M+1 или без изменений) |
| T80 | любое | LedgerCommitted с Faulted / событие ReadinessFaulted | команды запрещены; `GameLog.WeaponSystem.Error`; запрос resync | Faulted |
| T81 | Faulted | resync подтверждён (`TryAcknowledgeFixedAmmoResynchronization` / снимок) | — | Derive |

**Наблюдатель:** строки T03, T04, T60–T66, T20/T28/T43 (только `Presentation`) и T80/T81 без команд. Строки T21–T57 и T70 для `Observer` объявлены явными no-op.

**Полнота таблицы:** для каждой пары (MechanismState × EventKind × Role) есть хотя бы одна строка, включая явное `Ignore("причина")`. Это проверяет тест.

### 3.6 Цель позы — функция состояния (без собственной памяти исполнителя)

| Условие | Ручка, если не в руке | Связанные Action-детали | Нерычажные детали |
|---|---|---|---|
| Ручку держат | рука (SDK grab / PumpGrabFollow) | FollowHand: путь покой→зад по прогрессу, непрерывный поворот (логика `ApplyOwnedManualRotation`) | клип или покой |
| FireClip / EmptyClip | дорожка клипа | дорожка клипа | дорожка клипа |
| HoldOpen | **HoldRear** (проверенный зад) | HoldRear | покой |
| Preparing — без руки | ReturnToRest(AutoReturnSpeed) | ReturnToRest | покой |
| CycleLoaded/Cleared, OpenIdle, EmptyAwaitRest — без руки | Spring: ReturnToRest(скорость пружины); Stay: `Stay` (остаётся; в покое — Rest) | так же | покой |
| Ready, Empty | Rest | Rest | покой |

### 3.7 Инварианты (проверяются перебором достижимых состояний)

| № | Инвариант |
|---|---|
| И1 | M+C меняется только командами Shoot (−1), Extract (−1 в «извлечено»), Admission (+1), RefillForAutomation (только при Total=0) |
| И2 | C ∈ {0,1}; для FixedStore M+C ≤ Capacity |
| И3 | Ready ⇔ C ∧ ¬Open ∧ ¬Pending ∧ Initialized ∧ ¬Faulted ∧ ¬AdmissionPending |
| И4 | Semi/Manual: не больше одного Shoot на нажатие. Auto: Shoot только при Episode=Firing без отпускания с момента принятого нажатия |
| И5 | Нажатие, запустившее подготовку, никогда не стреляет |
| И6 | Не больше одного Extract на CycleSeq |
| И7 | Не больше одного NotReady-отклика на новое нажатие; ни одного при OtherDenied, у наблюдателя и при replay |
| И8 | Полнота таблицы (state × event × role) |
| И9 | Role=Observer не выдаёт `LedgerCommand` |
| И10 | При HoldOpen ∧ ¬Held цель позы ∈ {EmptyClip, HoldRear}; Rest и ReturnToRest не выдаются (класс 1) |
| И11 | EmptyClip/FireClip всегда завершается по времени; после Snapshot/Disable показ выводится заново, «зависшей фазы» нет (класс 4) |
| И12 | Gesture ∈ {Pulling, PastGate, Released} ⇒ Mechanism ∈ {CycleLoaded, CycleCleared} ∧ CycleSeqOwned = L.CycleSequence; иначе сброс (T33) |
| И13 | За шаг выдаётся ровно одна цель позы |

---

## 4. Владение каналами и классы ошибок

### 4.1 Каналы

| Канал | Единственный владелец | Кто пишет сейчас | Как обеспечено устройством |
|---|---|---|---|
| Ручка, которую держат | SDK grab (+ `PumpGrabFollow` для помп) | SDK, PumpGrabFollow, `AWSF.BeginVisualHandoff`, `WMV.HandleGrabbing` | Исполнитель не пишет ручку при Held; перехват удаляется (рука берёт текущую позу) |
| Ручка без руки + все связанные и нерычажные детали | `WeaponPoseExecutor` | AWSF (пружина, `HoldVisualPose`), `ChamberPoseReturnDriver`, WMV (5 путей), `ResetVisuals` | Ссылки на Transform деталей есть только в rig хоста; исполнитель — plain C# внутри хоста, вторым компонентом его не добавить |
| Корпус (отдача) | `RecoilAccumulator` + SDK recoil | — | без изменений |
| Звук выстрела | SDK (`CommitShotSynced` / `Source_ShotFired`) | SDK | без изменений; в `WeaponCue` нет Shot |
| Звуки механизма и сухой щелчок | `WeaponAudioExecutor` | AWSF, `UxrShotgunPump`, `WeaponAttemptFeedback`, `BarrelObstruction`, запасная ветка SDK | После этапа E SDK сам сухой щелчок не играет; у BarrelObstruction остаётся только датчик |
| Звук вставки/выемки | `AnchorSound` на приёмнике | AnchorSound; ранее ещё `UxrAudioManipulation` предмета | В `WeaponCue` нет Insert; тест `AmmoInsertSoundTests` расширяется на все предметы, принимаемые оружием |
| Вибрация механизма, отказа, препятствия; подсветка подсказки | `WeaponFeedbackExecutor` | AWSF, `WeaponChamberingReminder`, `WeaponAttemptFeedback`, `BarrelObstruction`, `UxrShotgunPump` | Один исполнитель; вызывает сервис вибрации `VrBattlegrounds.Haptics.HapticService.Play(HapticSignalId, UxrGrabber, gain, HapticHandRole)` (контракт — п. 4.2 [`tasks/haptics-system/Details.md`](../haptics-system/Details.md)). Вибрация отдачи тоже у WeaponSystem (решение пользователя 2026-10-07): новый `WeaponHapticCue` выстрела, Id отдачи — в данных ствола; хост реализует `IWeaponRecoilHapticsOwner`, после чего отдача SDK этого ствола гасится |
| Подсветка гнезда | `WeaponMagazineAnchorHighlight` | — | без изменений |
| Учёт M/C | SDK ledger | — | — |
| `IsUseBlocked` | `WeaponUseBlocker` | — | — |

### 4.2 Звуки по фиксациям (одинаково у автора и наблюдателя)

| Операция фиксации | Звук | Слышат |
|---|---|---|
| Extract (порог пройден) | ActionBack; плюс ChamberEjected, если C был 1 | все |
| CompleteChamber, C 0→1 | ActionForwardChambered | все |
| CompleteChamber без подачи / CloseOnly | ActionForwardEmpty | все (закрывает Н1) |
| Показ HoldRear после EmptyClip | SlideLockCatch (если задан) | все |
| Shot / MagazineChanged / AmmoAdmission | — (SDK / AnchorSound) | — |
| NotReady-попытка, препятствие | DryFire(reason) | только автор |

Клипы задаются на ствол в `WeaponAudioSet`: помпа FABARM, затвор пистолета и т. д. Перенос делает writer из полей AWSF/`UxrShotgunPump`.

### 4.3 Почему пойманные классы ошибок становятся невозможными

| Класс | Причина сейчас | Почему невозможно в WeaponSystem | Чем закреплено |
|---|---|---|---|
| 1. HoldOpen закрывает пружина | Четыре писателя одной позы координируются предикатом `OwnsActionPose`, который каждый обязан спросить | Пружины как отдельного писателя нет: ReturnToRest — одна из целей единственного исполнителя, цель выдаёт только машина, при HoldOpen ∧ ¬Held цель = HoldRear | И10 (перебор); префаб-тест «нет AWSF/WMV/ReturnDriver на перенесённых стволах» |
| 2. Двойной звук вставки | Звук задавали и предмет, и гнездо | В перечне звуков исполнителя нет Insert; звук вставки — только у приёмника (AnchorSound) | тип `WeaponCue`; расширенный `AmmoInsertSoundTests` |
| 3. Карман: «подходит» против «можно сейчас» | Два разных вопроса назывались одинаково | Хост отвечает двумя разными методами: `Accepts(tag)` (статика) и `CanAcceptNow(item)` (машина + ёмкость + барьер). `CartridgeIntake.CanPreviewPlacement` → `CanAcceptNow`; карман → `Accepts` | контрактный тест кармана/окна приёма |
| 4. Зависшая Source/Empty-фаза | Фаза показа хранилась в WMV отдельно от учёта; `ResetVisuals` её обнулял | Фаза показа и её время — состояние машины. Для Snapshot/Disable/Grab есть явные строки; исполнитель без состояния; `ResetVisuals` нет | И8, И11 |
| 5. Коллизия хэша Mirror | Переименование Cmd | WeaponSystem не вводит Mirror-вызовов, вся передача — фиксации UXR. Cmd остаются только у `CartridgeIntake` с прежними именами | `RemoteCallHashTests` (уже на всех NetworkBehaviour) |

---

## 5. Этапы

Общие правила:
- коммит этапа — только после проверки пользователем;
- каждый этап заканчивается `AndroidCompileGate.Run()` PASS и чистой консолью;
- временные пробы лежат в `tmp/`, постоянные тесты — по п. 6;
- правки SDK записываются в `sdk-patches.md` в том же этапе.

| Этап | Цель | Файлы | Что удаляется | Готовность | Откат |
|---|---|---|---|---|---|
| **A. Решение** | Пользователь принимает план и отвечает на вопросы п. 7 | этот документ, ссылка в `Docs/README.md` и статус в `weapon-readiness-feedback-design.md` | — | ответы пользователя | — |
| **0. Заморозка** | Дождаться приёмки в шлеме HoldOpen-правки другого агента (Herrington). Волну HoldOpen на старой архитектуре **не применять** | — | — | правка принята или откачена | — |
| **B. Машина** | Чистая машина и таблица (п. 3) | new `Assets/Scripts/Weapons/Core/VrBattlegrounds.Weapons.Core.asmdef` (`noEngineReferences`), `WeaponStateMachine.cs`, `WeaponTransitions.cs`, `WeaponProfileAxes.cs`, `LedgerView.cs`, `WeaponEvents.cs`; ссылка из `VrBattlegrounds.asmdef` и тестовой asmdef | — | Компиляция и Android. Временная проба: полнота, И1–И13 перебором, сценарии S01–S27 событиями. Контрактная проба с настоящим `UxrFirearmWeapon` (без сцены): каждая команда машины принимается ledger | удалить новую сборку (ничего не подключено) |
| **C. Теневой режим** | Датчики + машина рядом со старым контроллером на Herrington и FABARM; сравнение команд, расхождения пишутся в `GameLog.WeaponSystem.Warning`. Переключатель — `Tools/VR Battlegrounds/Debug/Weapon System Shadow` (EditorPrefs) | new `WeaponSystem/Sensors/*.cs`, `WeaponShadowComparer.cs`; точки вызова в `WeaponReadinessController` (только чтение) | — | Шлем: обычная игра Herrington и FABARM (заряжание, все циклы, HoldOpen, смена магазина/патрона, бросить и поднять), ноль расхождений в логе. Пробы E2 (`StateChanged` при replay) и Н6 | выключить переключатель |
| **C2. Учёт без копий** | Учёт не хранит физическое состояние, которое уже есть у гнезда и магазина: расхождение невозможно по устройству, а не латается сверкой. Решения пользователя 2026-10-07 — п. 8 [анализа](ledger-single-source.md) | SDK `UxrFirearmReadinessTypes.cs`, `UxrFirearmWeapon.Readiness.cs`, `UxrFirearmWeapon.cs`, `UxrFirearmWeapon.AmmoAdmission.cs` (патч 53); `WeaponReadinessController`; Core: строка T36, `MagazineMismatch`, `LedgerCommandKind.Reconcile`; `sdk-patches.md` | `CurrentMagazine` в учёте, `MagazineChanged`, `Reconcile`, ledger-ветка `SyncAmmoLeft` | Проба форка ревизии (п. 7.1 анализа) RED до правки и GREEN после; Android; шлем — смена магазина и стрельба Herrington/FABARM. Расхождение M → `GameLog.WeaponSystem.Error` + серверная поправка, стрельба не блокируется. **Статус 2026-10-07: реализован, ждёт шлема** (п. 9 анализа) | revert коммита этапа |
| **D. Пилот** | Herrington и FABARM работают на машине | `WeaponReadinessController.cs` → **`WeaponSystem.cs`** (тот же .meta/GUID, `FormerlySerializedAs`); new `WeaponPoseExecutor.cs`, `WeaponAudioExecutor.cs`, `WeaponFeedbackExecutor.cs`, `UxrReadinessLedgerPort.cs`, `WeaponMechanismRig.cs`, `WeaponAudioSet.cs`; Editor: `WeaponReadinessAuthoring` → `WeaponSystemAuthoring` (единственный writer: rig, аудио, профили; переносит данные из AWSF/WMV/`UxrShotgunPump` и удаляет их с префаба); `ManualLoadingAuthoring` вызывает его; префабы Herrington, FabarmSDASS; при необходимости SDK E2 | С двух префабов: AWSF, WMV, Router, AttemptFeedback, Reminder, выключенный `UxrShotgunPump`. Из кода: `ChamberPoseReturnDriver`, `ChamberCompletionEvidence` (переходят в порт), `WeaponTriggerAttemptRouter/Context`, `WeaponAttemptFeedback`, все ветки `HasLedgerAdapter` в AWSF/WMV/Reminder (они снова только legacy) | Компиляция, Android, readback префабов (GUID/fileID/NetworkIdentity/масштаб/хваты не изменились). **Шлем, чек-лист D:** см. под таблицей | git revert коммита этапа (данные префаба и код вместе) |
| **E. Спуск в машину** | Эпизод спуска и классификация — в машине; SDK-патч E1 | SDK `UxrWeapon.Custom.cs` (замена `ProcessReadinessLocalTrigger` портом `DecideLocalTrigger`), `UxrFirearmWeapon.cs` (вызов порта); `WeaponSystem` TriggerSensor; `sdk-patches.md` | `LocalTriggerEpisode`, `PrepareLocalTriggerAttempt`, `CaptureLocalTriggerPolicyId`, `LocalTriggerAttemptDecided` (SDK) | Компиляция, Android, повтор пробы S05–S07/S12–S15. Шлем: Auto очередь, Semi по нажатию, TriggerAssist (по профилю-стенду), сухой щелчок ровно один, препятствие | revert SDK и игровой части вместе |
| **F1. Волна HoldOpen** (бывший этап 4) | Browning/«Gun», Viper, TR15 | `WeaponSystemMigration.cs` (бывший `WeaponReadinessMigration`, список волн), профиль `DetachableHoldOpenReadiness.asset`, `WeaponInfo`, префабы | AWSF/WMV/Reminder с трёх префабов | Preflight → Apply → readback → повторный Apply без изменений; бот стреляет. Шлем: последний патрон → затвор сзади; смена магазина; front-only; Viper как стартовый пистолет у всех игроков | revert волны |
| **F2. ReturnToRest с Motion** | AK105, AR15/M16, Mk14, MKR9, SRM12 (затвор Manual) | то же | то же | то же + Manual: цикл между выстрелами (SRM12) | revert |
| **F3. Затвор без Motion** | PPK, MP5K, Scar, Uzi, SniperRifle | то же (scalar rig из `TryBuildPreparedActionMapping`) | AWSF | то же | revert |
| **F4. NoAction** | SDKGun, Machinegun (AutoOnMagazineInsert) | то же | — | стрельба сразу после вставки, бот | revert |
| **F5. Legacy** | Revolver, R08 → MagazineOnly (`UxrMagazineOnlyPort`); SDK Shotgun → Detachable + ActionTravel (помпа) + Manual | то же; порт MagazineOnly | `UxrShotgunPump` с SDK Shotgun | барабан как прежде, помпа SDK Shotgun | revert |
| **G. Приёмка** (бывший этап 5) | Все 20 стволов, сеть | постоянные тесты (п. 6), `Docs/gameplay.md`, `troubleshooting.md`, `CHANGELOG.md`, `README.md` | — | `run_tests` EditMode GREEN, Android. Два клиента: наблюдатель видит и слышит затвор, HoldOpen после позднего входа, нет двойного выстрела/дроби | — |
| **H. Чистка** | Удалить старый код | `AutomaticWeaponSlideFeedback.cs`, `WeaponMechanismVisuals.cs` (фабрика привязок переезжает в editor `WeaponMechanismRigBuilder`), `WeaponChamberingReminder.cs`, `AutomaticWeaponSlideFeedbackEditor.cs`, `KinemationFixReview`/`WeaponInteractionInstaller` (переходят на writer), обзорные префабы `SightReview/*`, `SightCalibrationDrafts/*` (перенести или удалить — п. 7) | ~2,5 тыс. строк | grep: старые типы не используются; тесты GREEN | revert |

**Чек-лист в шлеме для этапа D:**
- FABARM: патрон в окно (+1), помпа назад и вперёд досылает, звук помпы назад и вперёд слышен (Н1);
- Herrington: последний выстрел → затвор остаётся сзади (класс 1); патрон → толчок вперёд досылает; поворот затвора непрерывный;
- частичная оттяжка без извлечения;
- отпускание посреди цикла: пружина закрывает;
- бросить открытым и поднять;
- выстрел во время приёма патрона;
- Empty-клип прерван хватом;
- выключить и включить оружие (кобура, смерть).

**Как старые этапы 4–5 вливаются в новые:**

| Старое | Куда переходит |
|---|---|
| Профиль-ассет, `WeaponInfo.ReadinessProfile`/Feedback, фабрика привязок, `WeaponReadinessAuthoring` как единственный writer, список волн | используются как есть; writer становится `WeaponSystemAuthoring` |
| Контракт бота (`RequestAutomationPreparation`) и отчёт E2E по `GetTotalAmmoLeft` | сохраняются |
| Матрица S01–S27 | сценарии временной пробы этапа B, чек-лист шлема по волнам и позже постоянные тесты G |
| Компоненты префаба | ставятся один раз, сразу новые |
| Herrington и FABARM | переводятся один раз (D), потому что GUID хоста сохранён |

### 5.1 Этап B — машина: статус

Решения В1–В11 приняты по рекомендациям. Машина написана и **не подключена**: ни один компонент, префаб,
сцена и SDK не менялись. Проверки в шлеме этапу B не нужны.

**Файлы** (`Assets/Scripts/Weapons/Core/`, сборка `VrBattlegrounds.Weapons.Core`, `noEngineReferences: true`,
`autoReferenced: false`; на неё ссылаются `VrBattlegrounds.Tests.EditMode` и, с этапа C, `VrBattlegrounds.asmdef`
— первый потребитель, теневой режим п. 5.2):

| Файл | Что внутри |
|---|---|
| `WeaponProfileAxes.cs` | Оси п. 3.1, числа rig (порог, ε, скорости пружины и подготовки, клипы, время задней позы), `TryValidate` |
| `LedgerView.cs` | Снимок учёта, `LedgerOp`, `MechanismState`/`MechanismSet`, `WeaponMechanism.Derive/ReasonOf/CanFire` |
| `WeaponEvents.cs` | 19 событий, `ActionSample`, `WeaponContext` (роль, контекст автора, replay, блокировка, темп, мир) |
| `WeaponOutput.cs` | `LedgerCommand`, `WeaponCue`, `WeaponHapticCue`, `PoseTarget`, `WeaponReport`, `IWeaponOutput` |
| `WeaponTransitions.cs` | Единственная таблица: 83 строки (28 с правом команды, 31 локальная, 24 явных «игнорировать»); T36 удалена на этапе C2; T57a добавлена правкой S2 |
| `WeaponStateMachine.cs` | `Step` (без аллокаций, без повторного входа), `ComputePose`, локальные регионы |

**Как устроен шаг.** `Step(e, L, s, c, out)`: роль = автор только вне replay; механическое состояние = `Derive(L)`,
где L уже содержит фиксацию, вызвавшую событие (поэтому T60–T62 различаются состоянием после выстрела);
`PreStep` (время клипа, наблюдение хода назад, звук фиксации по п. 4.2) → первая подошедшая строка таблицы →
`PostStep` (снятие подсказки, ровно одна цель позы). Команды выдают только строки с правом команды, а такие
строки объявлены только для автора: наблюдатель не может выдать команду по построению (И9). Вторая команда
того же шага рассчитана на revision после первой: если первая отклонена, SDK отклонит и вторую.

**Отличия от п. 2.2 и 3 (уточнения реализации, без новых архитектурных решений):**

| Что | Как сделано | Почему |
|---|---|---|
| `Presentation` | Хранится только клип (вид, время, конец, отпущен ли Action рукой). Остальное — функция `ComputePose(Derive(L), клип, замер, Origin)` | П. 3.6 «цель позы — функция состояния»; И10/И11 следуют из устройства |
| `PoseTarget` | Два канала: Action и нерычажные детали (`AuxiliaryPose`) | T65: рука забирает Action, нерычажные доигрывают клип |
| Ready/Empty не в покое без руки | `ReturnToRest` со скоростью пружины вместо мгновенного `Rest` | Ручку отпустили после хвата во время Fire-клипа — без телепорта |
| `LedgerView` | `MagazineToken` — магазин в гнезде (единственный), `CycleMagazineToken` — магазин цикла; `CyclePending` — действительный Pending | C2: учёт не хранит магазин, Pending привязан к магазину цикла |
| `IWeaponOutput` | + `Report` (отказ, сбой, запрос resync, ошибка таблицы) | У машины нет `GameLog` |
| Эпизод спуска | `TriggerPressed` — фронт нажатия, всегда новое нажатие; `TriggerHeld` — уровень; эпизод гейтит только очередь | И4/И5 без зависимости от того, видел ли датчик отпускание после снимка |
| `MagazineOnly` | `Derive`: Ready ⇔ M>0, патронника нет | Порту не нужно подделывать C |
| В8 против T54 | В сбое нажатие даёт сухой щелчок с вибрацией `Faulted` (строка T82) | Принятое В8 приоритетнее «без отклика» в T54 |
| T57/T57a | Таймер темпа на нажатии: Auto — `Firing` без выстрела (T57a, продолжит T51); Semi/Manual и пустое — отказ `RateOfFire` (T57, S2) | Auto в SDK стреляет по истечении темпа, пока спуск держат, — это не отказ |

**Строки «+B»** (пробелы таблицы п. 3.5, заполнены по поведению прежнего контроллера; в Doc строки помечены «+B»):
T15/T15s — вставка без контекста автора не теряется, досылание при появлении контекста (иначе NoAction-ствол со вставленным
на столе магазином никогда не дошлёт); T18/T19 — контакт снимается и восстанавливается по замеру, пока ручку держат
(после T13/T27/снимка/смены автора); T21 — и из HoldOpen не с проверенного зада, и из OpenIdle; T28r — подхват
отпущенной посреди цикла ручки; T32s — осиротевший цикл (снимок, чужой автор) отменяется на замере; T34 — пока идёт
барьер приёма патрона, никакие команды не выдаются (SDK их всё равно отклонит), жест не сгорает; T36 (Reconcile при
расхождении магазина) удалена на этапе C2; T40 — TriggerAssist и из HoldOpen/EmptyAwaitRest; T46 — бот с HoldOpen спускает затвор
`BeginAction(Automation)` (сохраняет И10); T52a — блокировка посреди очереди; T58 — иная блокировка без отклика.
Порог извлечения засчитывается и в шаге `BeginAction` (рывок за кадр), и в момент отпускания (T28) — найдено пробой:
одна строка на шаг иначе теряла порог.

**Проверка.** Постоянные структурные тесты (В2) — `Assets/Tests/EditMode/Weapons/WeaponStateMachineStructureTests.cs`:
полнота таблицы (И8), отсутствие мёртвых строк, команды только у автора (И9), обход достижимых состояний на шести
профилях (И7 — не больше одного сухого щелчка, И9, И10, И11, И13, без нарушений таблицы), `ComputePose` при HoldOpen
(И10), `Derive` по всем сочетаниям флагов и И3, `TryValidate`, запрет повторного входа. Полный обход в ширину не
замыкается (ортогональные регионы ×10⁴ состояний на ×3·10⁴ входов), поэтому обход — случайное блуждание с
фиксированным зерном. Мутационные контроли (команда из строки наблюдателя, `Rest` при HoldOpen) дают RED.
Unity (worker, 2026-10-07): консоль без ошибок, `AndroidCompileGate` PASS, `WeaponStateMachineStructureTests` 14/14.
Временная проба с поддельным учётом и 17 сценариями — `tmp/weapon-core-probe/` (локально, не в Git); поведенческие
тесты S01–S33 пишутся после приёмки пилота D.

**Не входит в B и остаётся этапам C/D:** корреляция бота с выстрелом (сейчас `IsAutomationCorrelationCurrent`
контроллера) — проверка хоста/порта; какую руку подсвечивать подсказкой; перевод старых enum `WeaponReadinessProfile`
в оси машины (одинаковые имена в разных пространствах имён; при общем `using` — квалифицировать).


### 5.2 Этап C — теневой режим: статус

Реализован и проверен в шлеме (ниже). Машина работает рядом со старым `WeaponReadinessController` на Herrington и
FabarmSDASS и **ничего не исполняет**: команды учёту, цель позы, звуки, вибрации и подсказка только записываются и
сравниваются с тем, что сделал старый код. Префабы, сцены, SDK и сеть не менялись.

**Файлы** (`Assets/Scripts/Weapons/WeaponSystem/`, сборка `VrBattlegrounds`, ссылается на Core):

| Файл | Что внутри |
|---|---|
| `WeaponShadowComparer.cs` | Хост тени (MonoBehaviour, порядок 205 — до контроллера 210), сопоставление, карантин, лог, сводка |
| `WeaponShadowSettings.cs` | Переключатель (EditorPrefs `VrBattlegrounds.WeaponSystemShadow`, по умолчанию выключен; в сборке игрока всегда выключен) |
| `Sensors/WeaponActionSensor.cs` | Ход ручки, мин. прогресс обязательных деталей, покой, проверенный зад Empty, хват (`Grabbed/Released`) |
| `Sensors/WeaponContextSensor.cs` | Роль, replay, контекст автора (правило `HasContext`), блокировка, таймер темпа, авторство мира |
| `Sensors/WeaponTriggerSensor.cs` | Фронты спуска автора из `SyncTriggerPressStates` (событие `StateChanged`) |
| `Sensors/WeaponMagazineSensor.cs`, `WeaponIntakeSensor.cs` | Смена магазина опросом; окно приёма патрона и барьер SDK |
| `Sensors/WeaponLedgerReader.cs` | Порт учёта только на чтение: `LedgerView`, разбор фиксаций (`ApplyReadinessCommit`, `CommitShotSynced`, `CommitAmmoAdmission`) |
| `Sensors/FirearmIntrospection.cs` | Чтение закрытых полей SDK-спуска отражением (режим огня, таймер темпа, сбой учёта) — правка SDK ради диагностики запрещена |
| `WeaponStatePanels.cs`, `WeaponStatePanelSettings.cs` | Дебаг-панель в шлеме над стволами в 3 м: учёт SDK, ход Action, состояние тени и последнее расхождение; галочка `Tools/VR Battlegrounds/Debug/Weapon State Panel` (EditorPrefs, только редактор, независимо от тени) |

Точки наблюдения в `WeaponReadinessController` (поведение не меняют): статическое `ConfiguredAny` (тень подключается
во время игры к настроенному контроллеру через `AddComponent`, `HideFlags.DontSave`), `AutomationPreparationRequested`,
`TriggerIndex`, `TryGetRequiredActionProgress`, `IsActionAtValidatedEmptyRear`. Переключатель —
`Tools/VR Battlegrounds/Debug/Weapon System Shadow`; включение во время Play подключает тень к уже настроенным стволам.

**Как сравнивается.** Замер датчиков до контроллера: команды машины — ожидания; фиксация автора гасит ожидание того же
вида. Решение старого кода вне его `LateUpdate` (замер SDK перед выстрелом, окно приёма, бот) машина досчитывает тем же
замером на учёте «до фиксации». Не совпало — `GameLog.WeaponSystem.Warning` (раз в 10 с на вид, с числом повторов),
затем карантин: машина с нуля, сравнение ждёт спокойного состояния. Сухой щелчок сравнивается с
`WeaponTriggerAttemptRouter.NotReadyAttempted`, звук досылания — с `AutomaticWeaponSlideFeedback.ManualCycleCompleted`;
остальные звуки и вибрации машины — новые выводы без сравнения (Н1/Н2), считаются в сводке. Поза — по устойчивому
результату (HoldRear дольше 0,35 с без проверенного зада; возврат в покой дольше 1 с без покоя). Reconcile без
расхождения магазина — фиксация самого SDK (`SyncAmmoLeft` у FullyAutomatic), не решение. Выключение оружия и
снимок — карантин без сравнения (порядок `OnDisable` компонентов не определён).

**Пробы 2026-10-07** (worker, Play в лобби, программные хваты локального аватара, ход ручки задаётся после стадии
UltimateXR; отчёты — `tmp/weapon-shadow/`, локально):
- Herrington: частичный ход, 7 выстрелов до HoldOpen, толчок вперёд из HoldOpen, отпускание на заднем упоре (пружина
  досылает), Н6 — 20 совпадений (BeginAction×4, Extract×2, Complete×2, Cancel, CloseOnly, Shoot×7, RequestAdmission,
  звук досылания×2), 0 расхождений поз (HoldRear у проверенного зада); расхождения — только Н6 и Reconcile SDK (класс найден пробой, исправлен в сравнении).
- FABARM: 5 полных и 1 частичный цикл помпы, 3 выстрела — 20 совпадений, 0 расхождений команд; последний выстрел — S1.
- Шум пробы, не логика: программный хват левой рукой срабатывает не каждый раз — помпа, двигаемая без руки, дала
  расхождение позы ReturnToRest (в игре невозможно); сухой щелчок от подставного фронта спуска без пути SDK.
- **E2:** `StateChanged` компонента поднимается при replay (`UxrManager.ExecuteStateSyncEvent`) и для `ApplyReadinessCommit`,
  и для `CommitShotSynced`; учёт применяется, тень видит фиксацию как наблюдатель. Патч SDK E2 не нужен.
- **Н6 подтверждена:** при барьере приёма в момент досылания `TryCompleteChamber` отклонён, флаг цикла сгорел, после
  снятия барьера — Cancel + CloseOnly, патрон не дослан (C=0). Машина ждёт (T34) и досылает (T71).

**Расхождения спецификации и решения пользователя 2026-10-07** (S1, S2, S4 внесены в машину одним пакетом, ждут шлема):

| № | Что | Старый код | Машина | Решение |
|---|---|---|---|---|
| S1 | Последний выстрел ствола без Empty-клипа (FABARM) | Играет Fire-клип как Empty-показ; `AckEmptyRest` после клипа (~0,9 с) | Клипа нет (T62); `AckEmptyRest` сразу (T31); курок не анимируется | **Решено: как старый код.** Без Empty-клипа последний выстрел играет обычный Fire-клип; T31 ждёт конца любого клипа |
| S2 | Нажатие спуска во время таймера темпа (на заряженном и на пустом) | SDK молчит (ни щелчка, ни подготовки) | T53/T40 — сухой щелчок/подготовка | **Решено: новый отклик.** Отрицательная вибрация (класс «отрицательный» будущей системы хаптиков, `Docs/tasks/haptics-research.md`) и **отдельный искусственный звук отказа** — не щелчок пустого магазина и не щелчок недосланного патрона; подсветки нет. В машине — своя причина/сигнал (например, `WeaponNotReadyReason.RateOfFire` или `WeaponCue.Refusal`), не DryFire; подготовки (T40) нет. Звук выбран пользователем: `UI_Error_Subtle_Deep_stereo.wav` (0,21 с) из внешней библиотеки `_SoundLibrary/Universal Sound FX/USER_INTERFACES/Errors/` (`Docs/sound-library.md`); импорт (wav + .meta, моно) — при подключении сигнала на этапе D |
| S3 | T36 Reconcile | Только в контексте автора (основная рукоять) | Без контекста | **Решено архитектурно (этап C2):** расхождение учёта с гнездом — признак второго источника правды. Учёт не хранит копию магазина, а выводит её из гнезда; Reconcile, T36 и `MagazineMismatch` удаляются. Анализ и решения — `Docs/tasks/weapon-ledger-single-source.md` |
| S4 | Отпущенная посреди цикла ручка (помпа FABARM) | Остаётся на месте: у FABARM `AutomaticWeaponSlideFeedback._autoReturnOnRelease = 0` (у Herrington 1), пружины нет | Released → ReturnToRest со скоростью пружины (T28/T29, В7) | **Решено пользователем 2026-10-07: старый код прав.** Новая ось профиля «отпущенный Action: пружина / остаётся» (Herrington — пружина, FABARM — остаётся); значение переносит writer этапа D из `_autoReturnOnRelease` |
| Н6 | Досылание на барьере приёма | Цикл отменяется, патрон не дослан | Ждёт и досылает | Принять машину (исправление Н6) |

**S1, S2, S4 внесены в машину** (одним пакетом, ждут проверки тени в шлеме):
- S1 — T62 играет Fire-клип, если Empty-клипа нет; T31 и T44 ждут конца любого клипа (`ClipNow == None`).
- S2 — новые `WeaponNotReadyReason.RateOfFire`, `WeaponCue.Refusal`, `WeaponHapticCue.RateOfFire`. Строка T57
  (Ready Semi/Manual и Empty/HoldOpen/EmptyAwaitRest при `RofTimer>0`) стоит перед T50/T40/T53 и даёт отказ без
  подсказки; T57a — Auto ждёт темпа. В тени отказ — новый вывод машины без пары (SDK молчит), не расхождение.
- S4 — ось `WeaponReleasedAction {Spring, Stay}` (`WeaponProfileAxes.ReleasedAction`, `TryValidate`: NoAction + Stay
  недопустимо). При Stay `ComputePose` вместо возврата пружиной выдаёт новую цель `PosePresentation.Stay` (в покое —
  Rest); возврат подготовки (Insert/Assist/Automation) не меняется. Тень читает ось из `AutoReturnOnRelease`.

S3 закрыт этапом C2.

**Звук оттягивания Action (шлем 2026-10-07).** У стволов на учёте готовности старый код не играет звук заднего упора:
`AutomaticWeaponSlideFeedback.LateUpdate` для них выходит раньше `PlayForwardFeedback`. Слышен только возврат.
Решение пользователя — чинить на этапе D: машина уже выдаёт `WeaponCue.ActionBack`, его играет исполнитель звука.

**Карантин тени.** Известное S2 (сигнал без расхождения учёта) больше не включает карантин: в первой версии одно
раннее нажатие на FABARM выключило сравнение ствола до конца сессии (336 пропущенных сопоставлений).

**Идея для этапов D/E (заявка пользователя): сигнал «патроны заканчиваются», как в CS2.** На последних N выстрелах к
звуку выстрела добавляется отдельный слой — `WeaponCue.LowAmmo`, решается по учёту после выстрела, порог N — в профиле
ствола. Слышат все игроки, включая противников (элемент тактики): это оформление синхронизированного выстрела — каждая
машина играет слой у себя по реплицированной фиксации Shot, 3D у ствола, как сам звук выстрела. Отдельного сетевого
вызова нет, проверки автора нет. Детали — позже.

**Шлем 2026-10-07 (пользователь):** «всё, кроме автовозврата оттянутой помпы, работает» — это S4. Сводка тени
(локально `tmp/weapon-shadow/report-headset.txt`): FABARM — 105 совпадений, 7 расхождений (S1×5, S4×2 `pose:ReturnToRest`);
Herrington — 42 совпадения, 2 расхождения (S2: сухой щелчок при таймере темпа). Других расхождений нет.
После правки S1/S2/S4 пометки «известное S1/S2/S4» из тени убраны; цель следующего шлема — ноль расхождений, кроме Н6.

**Проверка:** офлайн-компиляция всех сборок; Unity — консоль без ошибок, `AndroidCompileGate` PASS,
`WeaponStateMachineStructureTests` 14/14. Постоянных тестов этап C не добавляет (правило 2026-10-02). Правка S1/S2/S4
обновила структурные тесты: профиль StayPump в обходе, контекст «темп без блокировки», инвариант «нажатие до конца
темпа не выдаёт команд и подсказки», `ComputePose` при Stay без возврата пружиной, `TryValidate` для новой оси.

**Шлем (чек-лист этапа C):** галочка включена; Herrington и FABARM из арсенала: заряжание окном, все циклы,
частичный ход, отпускание посреди цикла, HoldOpen и толчок вперёд, сухой щелчок пустым/без досылания, смена
патрона, бросить и поднять. В консоли — строки `[WeaponShadow]`; цель — ноль расхождений, кроме помеченных
«известное Н6» (S1/S2/S4 машина теперь повторяет; отказ темпа — в «новых выводах»). Итог — сводка при выходе из Play.

### 5.3 Этап D (`drive`) — статус

Код написан, не влит. Все сборки компилируются офлайн. В Unity не проверено: worker не на чистой базе, claim
отклонён. Решения реализации и открытые вопросы — [Details.md](Details.md), раздел «Этап drive».

| Файл (`Assets/Scripts/Weapons/WeaponSystem/`) | Что |
|---|---|
| `WeaponSystem.cs` | Хост — бывший `WeaponReadinessController.cs`, тот же `.meta`/GUID. Датчики → шаг → порт → исполнители; очередь событий SDK со снимком; простой лежащего ствола без шага |
| `UxrReadinessLedgerPort.cs` | Команды → `Try*` SDK; порты SDK; физическое доказательство одной командой (бывшие `ChamberCompletionEvidence`, резерв контроллера) |
| `WeaponPoseExecutor.cs` | Единственный писатель позы: FollowHand (непрерывный поворот, перехват ручки), клипы Fire/Empty, HoldRear, ReturnToRest, Stay |
| `WeaponAudioExecutor.cs`, `WeaponAudioSet.cs` | Звуки `WeaponCue`, включая `ActionBack` и `Refusal` (`UI_Error_Subtle_Deep_stereo.wav`, моно) |
| `WeaponFeedbackExecutor.cs`, `WeaponHapticOutput.cs` | Вибрация (одна точка до сервиса haptics) и подсветка-подсказка |
| `WeaponMechanismRig.cs` | Данные механизма: ручка, Action-привязки (покой/зад), локальный покой, клипы, детали, числа хода |
| `WeaponReadinessController.cs` | Абстрактное переходное имя хоста для файлов вне области этапа |

Удалены `ChamberPoseReturnDriver`, `ChamberCompletionEvidence`, `WeaponTriggerAttemptRouter/Context`,
`WeaponAttemptFeedback` и `WeaponShadowComparer`. Ветки `HasLedgerAdapter` удалены из AWSF, WMV и Reminder —
это снова legacy остальных 21 ствола. Writer — `Editor/.../WeaponSystemAuthoring.cs`, его вызывают
`ManualLoadingAuthoring` и сборщики. Пилот переводит `MigratePilots` с readback.

**Чек-лист D** — под таблицей п. 5. Что ещё нужно для готовности: аренда (миграция, readback, Android,
EditMode), затем шлем.

---

## 6. Стратегия тестов

| Уровень | Что | Когда постоянный |
|---|---|---|
| Чистый (машина, без Unity, миллисекунды) | **Структура:** полнота таблицы, И9 (наблюдатель без команд), И10, И11, И13, `TryValidate` осей. **Поведение:** перебор достижимых состояний по всем допустимым сочетаниям осей (M∈0..2, флаги, жест, эпизод × все события) с проверкой И1–И7, И12; сценарии S01–S27 и новые S28–S33 (классы 1 и 4, Н1, Н6, прерывание Empty-клипа, снимок при HoldOpen) как скрипты событий; мутационные контроли (второй Extract, выстрел тем же нажатием, rest при HoldOpen) | Структура — сразу на этапе B, если пользователь согласится (вопрос В2): это архитектурные контракты, как `GameTagRules`, а не настройка механики. Поведение — после приёмки пилота D в шлеме, затем дополняется после каждой волны |
| EditMode с настоящим SDK (без сцены) | Контракт «каждая команда машины принимается `UxrFirearmWeapon` из соответствующего состояния»; replay фиксации на второй копии; снимок → загрузка → `Derive` совпадает; `CommandRejected` → Derive | После приёмки D |
| EditMode с префабами | Ровно один `WeaponSystem`, на перенесённых стволах нет AWSF/WMV/Reminder/Router/AttemptFeedback/включённого `UxrShotgunPump`; rig проходит preflight (`WeaponMechanismRigBuilder.Validate`); `WeaponAudioSet` заполнен; на предметах оружия нет звука размещения; Mirror-вызовы только у `CartridgeIntake`; `RemoteCallHashTests` | Композиция — после каждой волны |
| Устаревшие ожидания (переписать после приёмки, с комментарием причины) | `WeaponFeedbackTests.Звуки_перезарядки_*` (читает поля AWSF), `WeaponSlideTravelTests` (AWSF.TryGetSlideTravel), `WeaponSpreadTests` (ручная запись HasReloaded), `ManualLoadingPrefabTests` (компоненты), `PumpGrabFollowTests`/`PumpAimTests` (останутся), `KinemationWeaponTests`/`HandsPackWeaponTests` (WMV) | после соответствующей волны |
| Только шлем / два клиента | Ощущение перехвата ручки, скорость пружины, непрерывность поворота Herrington, тайминг звука, вибрации, вид наблюдателя, HoldOpen после позднего входа | — |

Перед каждой правкой — временная проба RED (как в этапах 1–3), после — GREEN. Постоянные тесты механики пишутся только после подтверждения пользователя (правило 2026-10-02).

---

## 7. Риски, цена, открытые вопросы

### Риски

| Риск | Мера |
|---|---|
| Ухудшится ощущение перехвата и поворота (Herrington, помпа) | Теневой режим C, пилот на двух стволах, математика FollowHand и вращения переносится из WMV без изменений |
| `StateChanged` не поднимается при replay | Проба на этапе C; запасной вариант — узкий SDK-патч E2 |
| Перф Quest: шаг машины для каждого оружия на каждой машине | Шаг без аллокаций (struct, заранее выделенный буфер вывода); стволы без руки и не в Cycle/Clip пропускают шаг; замер `Perf` на этапе D |
| Параллельные правки другого агента в тех же файлах | Этап 0 — заморозка |
| Старые временные пробы (55/67/…) привязаны к внутренностям контроллера и WMV | Новые пробы этапа B; старые объявляются устаревшими |
| Расползание задачи (кнопка затворной задержки TR15, поворотный затвор SRM12, свободный живой патрон) | Вне плана; для них в таблице зарезервированы события |
| Отказ команды из-за повторной проверки SDK | Строка T35 + контрактный тест |

### Цена (порядок величины)

| Этап | Код | Агентское время | Сессии в шлеме |
|---|---|---|---|
| B | ~700 строк машины + пробы | 2–3 д | 0 |
| C | ~350 | 2 д | 1 |
| D | ~1100 нового, −1400 с пилота | 5–7 д | 1–2 |
| E | ~150 SDK ±, −200 | 1–2 д | 1 |
| F1–F5 | данные и writer | ~1 д на волну | 5 |
| G | ~1000 тестов | 3–4 д | 1 (два клиента) |
| H | −2500 | 1 д | 0 |
| **Итого** | | **~3–4 недели агента** | **~9–10 сессий** |

Сейчас решения и показ занимают ~2,9 тыс. строк (контроллер, WMV, AWSF, driver, router, feedback, reminder). Цель — ~2,3 тыс. строк плюс тесты.

### Открытые вопросы к пользователю

| № | Вопрос | Рекомендация |
|---|---|---|
| В1 | Применять ли подготовленную волну HoldOpen (Browning, Viper, TR15) на старой архитектуре до рефактора? | **Нет.** Иначе две приёмки в шлеме; перевести один раз в F1 |
| В2 | Можно ли писать структурные тесты машины (полнота, наблюдатель без команд, нет rest при HoldOpen) до проверки в шлеме? | **Да, только структурные.** Поведенческие сценарии — после приёмки пилота |
| В3 | Переносить ли эпизод спуска из SDK в машину (этап E, SDK-патч)? | **Да.** Все решения в одном месте, патч SDK меньше |
| В4 | Револьверы и SDK Shotgun — под машину (MagazineOnly / помпа) или оставить legacy? | **Под машину**, последней волной. Без этого «одна система» не полная |
| В5 | Должны ли наблюдатели слышать затвор и помпу? | **Да** — звуки выводятся из реплицированных фиксаций без нового трафика |
| В6 | Сухой щелчок у наблюдателей? | **Нет**, только у стрелка |
| В7 | Отпускание ручки посреди цикла: пружина досылает? | **Да**, как сейчас (T28/T29) |
| В8 | Поведение при сбое учёта (Faulted)? | `GameLog.Error`, автоматический запрос resync, сухой щелчок с отдельной вибрацией до восстановления |
| В9 | Звук вставки остаётся за гнездом (AnchorSound)? | **Да**, принцип «звучит приёмник» |
| В10 | Обзорные префабы `SightReview/*`, `SightCalibrationDrafts/*` с AWSF/WMV | Переводить вместе с исходным стволом; черновики удалить на этапе H |
| В11 | Пилот — Herrington и FABARM (уже на ledger, сложные), а не простой пистолет? | **Да**: только они дают теневой режим без новой миграции и покрывают помпу, затвор с поворотом, HoldOpen и FixedStore |

---

### Critical Files for Implementation
- F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/Assets/Scripts/Weapons/WeaponReadinessController.cs (станет `WeaponSystem.cs`, тот же GUID)
- F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/Assets/Scripts/Weapons/WeaponMechanismVisuals.cs (источник математики для `WeaponPoseExecutor` и фабрики rig)
- F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.Readiness.cs (учёт; команды порта)
- F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrWeapon.Custom.cs (seam эпизода спуска, этап E)
- F:/CodexWorktrees/shotgun-per-shell/Vr_Battlegrounds_ai/Assets/Editor/VR_Battlegrounds/Gameplay/WeaponReadinessAuthoring.cs (станет единственным writer `WeaponSystemAuthoring`)

Также затронуты:
- `Assets/Scripts/Weapons/AutomaticWeaponSlideFeedback.cs`
- `Assets/Scripts/Weapons/CartridgeIntake.cs`
- `Assets/Editor/VR_Battlegrounds/Gameplay/ManualLoadingAuthoring.cs`
- `Assets/Editor/VR_Battlegrounds/Gameplay/WeaponReadinessMigration.cs`
- `Docs/tasks/weapon-readiness-feedback-design.md`
- `Docs/UltimateXR/sdk-patches.md`