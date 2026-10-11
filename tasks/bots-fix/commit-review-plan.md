# Первый небольшой review packet: NPC body / Unarmed / Cyborg rest

2026-10-09. Независимый read-only аудит относительно `origin/dev`:
`6c0473fda8faa5e217117da0e5f6a4ed2866b634`.

**Вердикт: граница APPROVED как предлагаемый review packet после отделения указанных hunks.**
Текущие whole-file WIP не являются готовым accepted commit. Исходники, индекс, HEAD,
Unity и чужие изменения этим аудитом не изменялись. Принципал готовит изолированное
предложение и проверяет его; эта записка не принимает код или ассеты за пользователя.

## Пакет A: 19 точных product paths

1. `Assets/Scripts/Bots/BotClipBody.cs`
2. `Assets/Scripts/Bots/BotClipBody.cs.meta`
3. `Assets/Scripts/Bots/BotCombatAssets.cs`
4. `Assets/Scripts/Player/PlayerSession.cs`
5. `Assets/Scripts/Managers/PlayersManager.cs`
6. `Assets/Editor/VR_Battlegrounds/Bots/BotCombatSetup.cs`
7. `Assets/Editor/VR_Battlegrounds/Bots/BotClipComposition.cs`
8. `Assets/Editor/VR_Battlegrounds/Bots/BotClipComposition.cs.meta`
9. `Assets/Resources/Bots/BotCombatAssets.asset`
10. `Assets/Resources/Bots/BotUnarmed.controller`
11. `Assets/Resources/Bots/BotUnarmed.controller.meta`
12. `Assets/Resources/Bots/ClipBodies.meta`
13. `Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab`
14. `Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab.meta`
15. `Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab`
16. `Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab.meta`
17. `Assets/Resources/Bots/ClipAvatars.meta`
18. `Assets/Resources/Bots/ClipAvatars/PlayerControllersCyborgAvatar_ClipAvatar.asset`
19. `Assets/Resources/Bots/ClipAvatars/PlayerControllersCyborgAvatar_ClipAvatar.asset.meta`

Не изменять исходные человеческие prefabs/Avatar и существующие Rifle/Pistol controllers.
`Unarmed_Idle_Static.anim` уже существует в origin/dev и не изменён: повторное добавление
этого принятого клипа само по себе не даёт нового исправления.

## Отделение только в предлагаемом пакете, WIP сохранить

- `BotClipBody.cs`: оставить Bind/SetPose, Unarmed default, visible wrist/fingers validation,
  прекращение SDK writers и жизненный цикл Animator. Исключить целиком `TrySetMotionSpeed`
  (текущие строки 71–89): locomotion tempo ещё не принят.
- `BotCombatAssets.cs`: оставить ClipAvatarBinding/ClipBodyBinding, pistol/rifle/unarmed,
  clipAvatars/clipBodies, GetClipBody/GetClipAvatar. Исключить ClipSocketBinding,
  ClipMotionBinding, MotionRateParameter, sockets/motions, TryGetSocket/GetMotionSpeed
  и ставший ненужным ResolveSource. Исключить ненужный импорт Arsenal.
- `BotClipComposition.cs`: оставить Build и helpers composition/rest/reference-pose/
  persisted validation. Исключить BuildCalibration и его AnatomicalHand/CheckedScale
  helpers (текущие строки 225–340), импорты Arsenal/Weapons. После этого отсутствует
  зависимость от нового WeaponGripBinding и отвергнутого candidate grip fit.
- `BotCombatSetup.cs`: взять прежний Controller helper из origin/dev; добавить только
  создание/сохранение Unarmed controller и ссылку assets.unarmed. Исключить все
  BotMotionRate hunks. Rifle/Pistol `.controller` сохранить в версии origin/dev.
- `BotUnarmed.controller`: Idle на существующий клип, IK выключен; в предлагаемом
  варианте убрать BotMotionRate parameter и соответствующее имя state speedParameter.
- `BotCombatAssets.asset`: включить только unarmed/clipAvatars/clipBodies.
  Текущие sockets и motions не включать: это кандидатные данные 345/347.

Это перечень для подготовки предложения, а не разрешение редактировать/откатывать WIP.
Без такого отделения первый пакет захватывает незавершённые grip/motion алгоритмы.

## Почему нужны эти зависимости

Оба generated NPC variants добавляют BotClipBody с GUID
`04fd4c4360bb8a84e90699fa02bcb86b`; нельзя переносить prefabs без `.cs` и `.meta`.
Каталог содержит ссылки на оба variants, Cyborg Avatar и Unarmed controller — все ссылки
замыкаются перечисленными ассетами и уже существующими source assets из origin/dev.

BotClipBody требует нового `PlayerSession.IsBotControlled`; marker setter фиксирует его
до Spawn. `PlayersManager.CreateBotSession(..., bool botControlled = false)` нужен
для воспроизводимости fixture 303, который передаёт true NPC и false отдельному VR телу.
Двухаргументные старые callers сохраняют false; обычная bot factory этим пакетом не
переключается. Marker меняет Mirror serialization сессии, поэтому это совместная
source dependency, а не отдельный косметический файл.

`PlayerController.Session`, `SessionNetId`, `SourcePrefab`, связь ActiveAvatarNetId и
AvatarManager body creation уже есть в origin/dev; изменений этих классов не требуется.
AnimatedLegs/Destroy также уже есть в SDK origin/dev. Название ActorBindSession как
отдельный новый класс/API в scoped sources не найдено; fixture использует существующие
AvatarManager/PlayerSession связи. Blaze BindAnimatorSource, SDK availability patch и
WeaponEquipmentBinding для пакета A не нужны.

Наблюдаемый результат A: вручную/fixture созданный NPC variant сразу показывает Unarmed
Idle с опущенными руками; после явного Bind модель имеет один visible Humanoid Animator;
Cyborg persisted geometry/rest сохраняется. Обычные spawn, стрелковая механика и
навигация сохраняют прежний путь. Это подготовленная NPC foundation, не исправление
всех обычных ботов в игре.

## Пакет B отдельно после A: rigid equipment foundation

Минимальные 7 product paths:

- `Assets/Scripts/Weapons/Equipment.meta`
- `Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs`
- `Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs.meta`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/IUxrGrabAvailability.cs`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/IUxrGrabAvailability.cs.meta`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.cs`
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Manipulation.cs`

В UxrGrabbableObject включить только patch61 availability hunks; patch56
CanBeGrabbedByGrabberWithoutProximity относится к прежнему hybrid и остаётся WIP.
IUxrGrabAvailability/getter/low-level gate/binding переносить вместе. Binding зависит
от marker и BotClipBody пакета A, existing MapRunAdmission/StateEventAuthority/Mirror
ResetState. WeaponAutomationCapability, Gunner, equipment use-context, оружейные prefab
изменения и production cutover не требуются для этого dormant foundation.
Нужна собственная запись patch61 в task changelog; не переносить целиком смешанный
historical migration journal как доказательство принятия.

## Фактическая проверка и граница принятия

- Сохранённый 303: **64/64 checks PASS**, включая обе Unarmed poses и equipment fixture.
  Источник: reports/equipment-303/binding.json. Это исторический fixture всего WIP,
  не isolated compile предлагаемого пакета A и не обычная игровая factory.
- Сохранённый 345: composition **2/2 PASS**; fresh saved mesh delta MEF
  **0.000584004 мм**, Cyborg **0.000512739 мм** (reports/calibration-rebuild/composition.json).
  Создание четырёх sockets/16 motions не доказывает принятие grip/tempo.
- 347: восемь preview PNG, хват/наклон rifle отвергнуты; эти sockets исключены из A.
  Обычный spawn остаётся hybrid IK; clip runtime firing, remote и Quest не подтверждены.
- Полный WIP компилировался ранее; **компиляция изолированного A против origin/dev здесь
  не запускалась**. После подготовки root обязан проверить именно этот source/assets
  snapshot, GUID closure и applicable existing checks. Не писать новые gameplay NUnit
  до человеческого принятия. Не считать отсутствие новых compile ошибок доказанным
  только по компиляции полного WIP.

Пользователь принял Unarmed pose; code/assets foundation целиком не принята.
Технический checkpoint и предложенный review patch допустимы отдельно от accepted commit.
Accepted commit требует проверки пользователем конкретного предлагаемого результата.
Не включать foreign Docs, stand/debug harness, BotGunner/Driver/RuntimeController/
ControlContracts/WeaponPoseSolver, navigation/director/shopper, Blaze patch, arm IK patch,
candidate grip/motion reports или task-wide `**`.
