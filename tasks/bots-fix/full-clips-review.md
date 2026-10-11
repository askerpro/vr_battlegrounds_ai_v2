# Независимое ревью плана полной клиповой анимации ботов

История независимого ревью. Текущий статус: [Readme.md](Readme.md).

Дата: 2026-10-07. Объект: `Docs/plans/2026-10-07-bots-full-clips.md`.
Режим: чтение исходников; Unity, исходники, ассеты и Git не менялись. Heavy не проверялся.

## Актуальные вердикты по срезам

### Исправленный equipment stage 1 — SOURCE APPROVED, runtime pending

Дата: 2026-10-08. Финально перечитаны исправленные source и fixture; B4/I2/I3 ниже закрыты.
Остающихся BLOCKER/IMPORTANT source findings в пределах непотребляемого stage 1 не обнаружено.

- **B4 закрыт:** `WeaponEquipmentBinding.cs:171` и `:188` используют ResetState(),
  не Reset(); очистка interpolation не меняет authored syncDirection.
- **I2 закрыт:** `UxrGrabbableObject.cs:907–916` заполняет reusable List текущими ancestor
  providers и комбинирует все запреты AND. Stale ancestry/provider bool не кешируется,
  query array не создаётся; direct/replayed admission остаётся до BeginSync.
- **I3 закрыт на уровне fixture:** `tmp/bots-equipment-probe.cs:10–18` создаёт отдельную
  marker=false сессию с обычным source MEF и требует active/enabled hand. NPC grabbers
  не включаются. Реальная регистрация hand и rollback outcome подтверждаются будущим probe.
- **Admission уточнение закрыто:** `WeaponEquipmentBinding.cs:91–92` запрещает attach/
  transfer при !MapRunAdmission.CanActivateActiveMap; drop остаётся доступным для cleanup.
- **Per-frame array/LINQ путь снят:** `WeaponEquipmentBinding.cs:134–140` использует
  GetComponentsInChildren(List) и простой read-only обход актуальных grips. Это source
  устранение названных allocations, не профилирование/Quest acceptance.

Read-only API IsActive/Owner/OwnerAlive/Epoch, server-only SyncVar writer, unresolved claim,
rollback через receive ReleaseObject и socket только после успешной projection сохраняют
одобренный контракт. Production factory/Gunner/weapon-owner consumers этим этапом не включены.

Исполнитель сообщает свежую компиляцию 7/7 PASS после последних исправлений; reviewer её
не запускал. Runtime RED→GREEN остаётся blocked external maintenance. До аренды дополнить
probe nested receive, transfer и owner destruction/canonical body replacement; дополнительно
проверить NT syncDirection после attach/drop и nested permissive provider под outer deny.
Probe compilation не подтверждает runtime результат и не снимает broker maintenance pause.

Проверка только чтением source; broker/worker, Unity, исходники и Git не изменялись.
Записан только этот review addendum. Source approval не является runtime GREEN или принятием
игровой механики/remote/Quest и не заменяет внешнюю интеграцию UseContext/WRC.

### Source review equipment stage 1, 2026-10-08 — NEEDS_CHANGES

Проверены актуальные `Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs`,
SDK `IUxrGrabAvailability`, effective getter и private GrabObject guard, а также
`tmp/bots-equipment-build-probe.cs` / `tmp/bots-equipment-probe.cs`.
Runtime probes не запускались: исполнитель сообщил external maintenance и ожидание GREEN.
Компиляция 7/7 PASS — сообщение исполнителя, не отдельный запуск reviewer. Broker/worker,
Unity, исходники и Git не изменялись; записан только этот отчёт.

**BLOCKER B4 — Reset() меняет автора оружейного транспорта.**

`WeaponEquipmentBinding.cs:160` и `:177` вызывают `NetworkTransformBase.Reset()`.
`Assets/ThirdParty/Mirror/Components/NetworkTransform/NetworkTransformBase.cs:436–440`
не только очищает snapshots, но также присваивает `syncDirection=ClientToServer`.
Это нарушает authored transport authority при attach/drop и может остановить рассылку
server-owned dropped firearm. Для очистки буферов использовать `ResetState()`
(`:424–433`), сохраняя выбранный transport syncDirection.

**IMPORTANT I2 — nearest provider не гарантирует conjunctive grab denial.**

`UxrGrabbableObject.cs:907` возвращает только первый ancestor provider через
GetComponentInParent. Вложенный provider с true скрывает внешний active equipment provider
с false. Требование generic providers AND из утверждённого контракта этим не выполнено.
Комбинировать все applicable ancestor providers конъюнкцией либо отдельно enforce/validate
единственный provider по всей hierarchy; одного DisallowMultipleComponent на binding root
недостаточно. Direct/private admission guard `UxrGrabManager.Manipulation.cs:779–781`
расположен корректно до BeginSync, но наследует тот же nearest-provider bypass.

**IMPORTANT I3 — disabled NPC hand не является VR prediction fixture.**

`tmp/bots-equipment-probe.cs:30` выбирает grabber клипового NPC variant;
BotClipComposition отключает все такие grabbers. Private GrabObject лишь предупреждает
о disabled grabber и может создать manipulation, однако штатный ReleaseObject
`UxrGrabManager.Manipulation.cs:1047–1049` не отпускает руку вне UxrGrabber.EnabledComponents.
Поэтому prediction rollback в probe `:59–64` рискует получить harness failure.
Использовать действительно active/enabled human hand fixture и проверить этот precondition;
не включать VR hands внутри NPC variant ради стенда. Destroyed/disabled stale hand проверять
отдельно как lifecycle/error fail-closed, не смешивать с обычной predicted VR race.

**Проверенные сильные стороны source, без заявления runtime GREEN:**

- Один SyncVar struct, server-only mutations, epoch overflow/invalid authored offsets
  отказывают; inactive epoch=0 не перемещает обычное оружие в origin.
- IsActive основан на ownerNetId claim, OwnerAlive требует resolved canonical bot session,
  current ActiveAvatarNetId и жизнь. Unresolved owner не возвращает grab availability.
- Rollback перебирает descendant grips и hands; точный 7-argument receive ReleaseObject,
  world pose, zero velocities и propagateEvents=false. Result.IsError и HasSdkGrabs
  проверяются; nested IsInsideStateSync откладывает projection, socket до успеха не запускается.
- Root grip отключается после release; direct/replayed SDK admission проверяет provider
  до mutation. `_isGrabbable` setter/StateSave не переписываются. Snapshot projection state
  (`_projectedEpoch`, `_projectedActive`) не используется как второй domain holder.
- Реальные prefab consumers пока не включены; probe создаёт binding в derived test prefab
  до Spawn. Same prefab/NetworkBehaviour ordering обеих сторон надо проверить реальным transport
  после окончания обслуживания, не выводить из local OnSerialize/OnDeserialize.

**Дополнительные условия:** SetOwner сейчас не проверяет map admission/loading
(`WeaponEquipmentBinding.cs:92–99`). Опубликованный план предусматривает alive/admission.
Уточнить единственную границу этого допуска до production cutover: reject attach во Closing/
loading внутри binding либо явная dependency на existing runtime gate; автономное использование
API без допуска не разрешать. StateEventAuthority/UseContext/WRC integration пока следующие
шаги внешнего owner, их отсутствие не объявляется дефектом текущего непотребляемого stage 1.

**Perf, без Quest acceptance:** HasValidAttachment/ApplySocketPose вызывает HasSdkGrabs,
который создаёт GetComponentsInChildren array и LINQ (`WeaponEquipmentBinding.cs:56`,
`:129–130`, `:195–199`) каждый кадр. Это реальная allocation path; после функционального
GREEN нужен measurement/неаллоцирующий обход актуальных grips, не ownership cache.

После исправления source findings требуются временные runtime RED→GREEN probes по утверждённому
контракту и compile/existing проверки. Предыдущие APPROVED для authoring и архитектурного плана
сохраняются; этот NEEDS_CHANGES относится к текущей реализации equipment stage 1.

### Уточнённый rollback equipment snapshot через SDK receive — APPROVED

Дата: 2026-10-08. B3 предыдущего архитектурного ревью закрывается предложенной единой
provider-only admission проверкой private GrabObject до BeginSync и синхронной rollback
projection active snapshot через штатный `UxrManager.ExecuteStateSyncEvent`.
Одобрение относится к контракту для реализации; source/runtime проверка самого binding
ещё впереди. Другого владельца holder, нового ReleaseObject internals или automation lifecycle
этот подход не требует.

Основание: `UxrManager.cs:631–645` выполняет SyncState внутри IsInsideStateSync,
а `:1695–1699` подавляет исходящий ComponentStateChanged. Сам ReleaseObject остаётся
`UxrGrabManager.Manipulation.cs:1043–1253`, включая снятие manipulation/hand links.
Snapshot claim становится canonical, predicted VR state откатывается как receive projection;
low-level provider guard затем запрещает прямой и реплицированный Grab на active claim.

Обязательные условия исполнения этого APPROVED:

- Точный synced ReleaseObject payload из `:1253`: конкретная hand, её действительный
  held grabbable, explicit current world pose, zero linear/angular velocity,
  propagateEvents=false. Перечислять снимок всех held root/descendant grips, а не только
  primary firearm root. Это отмена prediction, не новый gameplay throw/drop.
- Проверить UxrStateSyncResult.IsError и отсутствие SDK holder/current manipulation
  после rollback. До успешного завершения не запускать socket follow; claim остаётся
  deny-grab, unresolved Owner даёт OwnerAlive=false. Root grabbable muting делается после
  штатного release, без setter/cache/restore `_isGrabbable`.
- Не вызывать ExecuteStateSyncEvent вложенно: SDK `finally` в `UxrManager.cs:644` всегда
  ставит IsInsideStateSync=false, не восстанавливает прежнее состояние. Hook должен
  разрешать receive projection только при !IsInsideStateSync. Если nested case возможен,
  оставить deny/mute без socket writer и завершить reconciliation после outer receive;
  иначе явно проверить/зафиксировать его невозможность. Не полагаться на stack safety SDK.
- При inactive default snapshot с epoch=0 не применять нулевой drop world pose к обычному
  оружию. На действительном drop snapshot публикует final world pose/velocity/epoch,
  projection восстанавливает transport с очищенными interpolation buffers и не допускает
  удерживающего socket writer. Повторное применение той же эпохи идемпотентно.
- Сохраняются ранее перечисленные source/provider ancestry, same-prefab NetworkBehaviour
  ordering и resolved current-session/ActiveAvatar OwnerAlive проверки. Стрельба/WRC
  в шаге 1 не подключаются; внешнее owner-owned UseContext/readiness внедряется отдельно.

Проверки перед заявлением GREEN: direct/replayed blocked grab, root и вложенные grips,
predicted VR → received equipment snapshot с нулём исходящих SDK events, unresolved latejoin,
повтор hook/host, error/nested receive fail-closed, drop world pose и транспорт после epoch.
Новых source/Unity/Git изменений reviewer не выполнял; изменён только этот отчёт.

### Архитектурное ревью equipment binding / grab availability, 2026-10-08 — NEEDS_CHANGES

Это отдельное ревью нового контракта шага 1; одобрение authoring slice ниже сохраняется.
Прочитан согласованный ответ weapon owner
`F:/UnityProjects/Vr_Battlegrounds_ai/.agent-state/coordination/weapon-system-to-bots-full-clips.md`.
Разделение владельцев принято: bots-fix пишет binding, weapon-system выводит UseContext
и адаптирует WRC, factory/Gunner переключаются позже атомарно. Read-only API
IsActive/Owner(UxrAvatar)/OwnerAlive/Epoch соответствует ответу владельца.

**BLOCKER B3: availability только в IsGrabbable getter не обеспечивает взаимное исключение.**

`UxrGrabbableObject.cs:1162` проверяет IsGrabbable в CanBeGrabbedByGrabber, но публичный
`UxrGrabManager.Manipulation.cs:96–114` вызывает private GrabObject, и тот в строках
777–806 проверяет null, лишь предупреждает о disabled компонентах и начинает BeginSync.
Availability там не проверяется. Прямой/реплицированный GrabObject поэтому способен
создать SDK holder при active equipment, несмотря на derived IsGrabbable=false.

Рекомендованный узкий SDK API: `IUxrGrabAvailability { bool IsGrabAvailable { get; } }`,
и внутренняя read-only оценка providers у UxrGrabbableObject. Effective IsGrabbable остаётся
`_isGrabbable && providersAvailable`; synced setter и StateSave поля не меняются
(`UxrGrabbableObject.cs:886–899`, `UxrGrabbableObject.StateSave.cs:64`). Та же provider-only
оценка должна запрещать low-level GrabObject **до BeginSync/anchor/holder mutation**,
включая replicated/replay путь. Не превращать эту проверку в повтор proximity/обычных
grab rules: прежний explicit SDK force-grab без providers остаётся совместимым.

Дополнить контракт гонкой: pending VR Grab до Attach и позднее применение active snapshot
после локального VR grab. На сервере и копиях не должно оставаться SDK currentManipulation
параллельно binding socket writer. Нужна явная единственная граница arbitration/reconciliation
для этой гонки и read-only inactive/unresolved claim. Возврат UseContext.None при конфликте
правильно запрещает fire, но сам по себе не прекращает конкурирующий transform writer.
Не вводить client-written ownership или cached holder/availability restore для исправления.

**IMPORTANT / точные условия исполнения:**

- IsActive определяется сохранённым ownerNetId claim, а не успешностью Owner lookup.
  Unresolved/dead/stale owner не делает firearm SDK-grabbable; только server drop/transfer
  меняет claim/epoch. OwnerAlive требует разрешённую session, IsBotControlled, current
  ActiveAvatarNetId/body identity, жизнь/допуск. Одного `PlayerController.IsAlive` недостаточно:
  `PlayerController.cs:141` допускает Session==null. Epoch возрастает на каждой принятой
  операции; повторный cleanup должен быть idempotent, отказ attach не расходует эпоху.
- SyncVar struct меняется целиком сервером; no client mutation в hooks/OnDisable/unresolved
  owner. Hooks и OnStartClient применяют projection до первого pose/SDK кадра; разрешение
  late owner повторяется при Spawn/session-ready, без второго holder snapshot.
- Root NetworkBehaviour authoring присутствует в prefab до Spawn на обеих сторонах с тем
  же component order. Не добавлять binding по факту уже spawned firearm и не отключать
  NetworkIdentity/сам binding при блокировании конкурирующего root NetworkTransform.
- Cached provider reference допустим; cached IsGrabAvailable bool — нет. Awake/собственный
  OnTransformParentChanged недостаточны для изменения ancestor у nested child grips.
  Требуются проверка актуального ancestry и refresh/subtree invalidation, включая снятие
  части/магазина из weapon hierarchy; отсутствие/появление provider также должно обновляться.
  Generic providers комбинируются конъюнкцией, disabled binding не должен освобождать active claim.
- Socket follow предпочтительно вычисляет world pose от RightHand без помещения root
  NetworkIdentity weapon внутрь avatar hierarchy. На drop re-enable транспорта очищает
  interpolation buffers (`NetworkTransformBase.cs:424–453`), публикует действительный world
  pose и не допускает поздних held transforms поверх освобождённого предмета. Physics policy
  берётся из authored baseline, не из mutable копии holder/ammo; после release обычная физика.
- Equipment resolver должен войти в существующие IsAuthorOfItem/ShouldSend по утверждённому
  плану. `StateEventAuthority.cs:128` сейчас считает отсутствие SDK holders разрешением
  ShouldSend; наличие equipment claim должно иметь server author, включая unresolved owner.
  Resolver нельзя вызывать рекурсивно из вычисления собственного item authority.

**Не являются blockers:** один SyncVar snapshot ownerNetId/epoch/socket pose, immutable
read-only owner API и отсутствие M/C копий; derived availability вместо setter/cache/restore;
server-only attach/drop/transfer и fail-closed при missing owner/socket. SDK номер 61 согласовать
с pipeline до реализации. Новый gameplay NUnit до человеческой приёмки не требуется;
нужны временные probes direct/replayed grab bypass, pending-grab race, nested provider
reparent, unresolved late join, death/drop/transfer и compile/existing suites.

Вердикт относится к достаточности нового equipment/grab контракта. Он не отменяет
APPROVED для ранее проверенного NPC authoring и не требует заранее реализовать весь cutover.

### Ревью authoring slice по probe 200 — APPROVED

Дата: 2026-10-08. Проверены текущий `BotClipComposition.cs`, `BotCombatAssets.cs`,
сохранённые NPC assets, отчёты `tmp/bots-refactor-200/reports/composition.json` и
`visual.json`, кадры Cyborg `images/1/frame-11250-front.png` и `frame-13932-front.png`.
Результат worker, сообщённый исполнителем: `f31d9dff42a1e8470105625937f8a8c24a0b7f7c`.
Reviewer не запускал Unity, Android или Git mutations; изменён только этот отчёт.

**BLOCKER / IMPORTANT: не обнаружены в пределах authoring slice.**

- Reference pose и geometry rest теперь имеют разные явные роли. Generated NPC Avatar branch
  восстанавливает geometry rest, вызывает editor-only `PrepareReferencePose`, захватывает
  подготовленную reference T-pose в HumanDescription.skeleton и создаёт новый Avatar
  (`BotClipComposition.cs:126–135`). До записи persistent Avatar/variant возвращается
  geometry rest и выполняются weighted-matrix/BakeMesh проверки (`:150–164`). Human source
  Avatar не переписывается. MEF остаётся на native Avatar, helper в его branch не вызывается.
- Composition отчёт: 2/2 passed; Cyborg referencePoseError 456.886078 → 0;
  максимальное world-vertex отклонение rest 0.000645 мм у Cyborg и 0.001012 мм у MEF.
  Отдельные Mirror assetId сохранены; persisted bounds проверяются authoring кодом после Save.
- Visual отчёт независимо агрегирован: по 60 samples каждой модели, всего 120;
  все configured=true, SDK controller=false, ikEnabled=0. Диапазон upper-arm длины
  0.000625 мм у MEF и 0.000700 мм у Cyborg. Cyborg footHeight 0.067606–0.169777 м,
  headHeight 1.543771–1.713745 м; прежний перенос тела вверх отсутствует в этом probe.
  Минимумы reported `rightHandForward` положительны: 0.037988 у MEF и 0.066414 у Cyborg.
  Исходный код transient recorder в этом ревью не проверялся; направление руки относительно
  торса подтверждается отдельно просмотренными кадрами, а не предположением об осях wrist bone.
- На обоих просмотренных кадрах Cyborg руки находятся перед торсом. Это подтверждает
  устранение показанного arm-behind-torso эпизода; оружия на этих кадрах нет, поэтому
  контакт конкретного firearm grip и socket-follow этим не принимаются.
- `BotCombatAssets.cs:28–32` возвращает null для неизвестного source prefab; generated
  catalog содержит только MEF/Cyborg и не выбирает skin. `GetClipAvatar` сохраняет source
  Avatar для native пути; это не разрешение использовать неизвестную живую модель.
  Будущая factory обязана проверять `GetClipBody` и fail-closed, как требует утверждённый план.
- Проверка `git diff --name-only` / `git status --short` по человеческим player prefabs
  и `Assets/Art/Avatars` не обнаружила изменений; новые assets расположены в
  `Assets/Resources/Bots/ClipAvatars` и `ClipBodies`. Обычный `AvatarManager` ещё не
  подключён к GetClipBody/AvatarBodyFactory — authoring slice не является gameplay cutover.

**MINOR:** finite checks в PrepareReferencePose и удаление уже перезаписываемого
description.skeleton assignment исполнитель вынес в отдельную небольшую правку.
Reflection остаётся editor-only, ошибка несовместимости API ведёт к failed Build.
После hardening достаточно повторить затронутую authoring/compile проверку по решению
исполнителя; полного equipment/runtime acceptance для этого authoring ревью не требуется.

Android PASS получен из сообщения исполнителя, а не собственного запуска reviewer.
Этот APPROVED относится к authoring/reference-pose slice. Он не принимает обычный
factory/Gunner, владение equipment, сеть remote client или Unity/Quest игровой результат.

### Дополнительное ревью 2026-10-08: pre-Awake NPC variants — APPROVED

Проверен раздел Task 2 «Уточнение по runtime preflight 190: позой нужно владеть до Awake»,
текущие строки 119–163 плана. Одобрено создание производных NPC prefab variants MEF/Cyborg,
pre-Awake authoring и последующее атомарное подключение через AvatarBodyFactory вместе с
equipment/runtime cutover. Источники и Unity в этом ревью не менялись; runtime preflight
189/190 получен от исполнителя, повторный Play-прогон ревьюером не выполнялся.

**BLOCKER: нет оставшихся.**

- Причина структурной мутации подтверждается исходниками:
  `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs:418–437`
  создаёт arm/body/native leg IK в Awake по serialized flags;
  `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Animation/IK/UxrBodyIK.cs:156–160`
  создаёт Dummy Forward и переподчиняет render skeleton. Поэтому позднего Disable недостаточно.
  Editor authoring variant должен записать `_useArmIK`, `_useBodyIK`, `_useLegIK`,
  `_useNativeLegIK` как false до runtime Instantiate; private поля без публичного setter
  доступны через SerializedObject, SDK runtime setter для этого не нужен. Отдельные
  уже authored IK/legs компоненты также должны пройти проверку отсутствия active writers.
- Два фактических Instantiate находятся в `AvatarManager.cs:134` и `:275`;
  присвоение SourcePrefab — `:145` и `:285`. Общая factory применяет source→variant только
  при canonical `PlayerSession.IsBotControlled` (`PlayerSession.cs:39–46`) и сохраняет
  исходный authored source. Это сохраняет сравнение `AvatarManager.cs:236–240`, исключает
  повторный replace из-за сравнения source с variant и не переносит skin selection в каталог ботов.
- Обязательное уточнение ghost пути внесено: `TeamAvatarStrategy.cs:26` выбирает GhostPrefab
  для IsEliminated, и тот же ReplaceBody обслуживает эту замену. План, строки 140–143,
  разрешает только точный canonical ghost passthrough без BotClipBody/Gunner/equipment;
  живые неизвестные модели остаются fail-closed, respawn снова требует валидный variant.
  Во время cutover EnsureBody/директор не должны добавлять bot runtime на этот ghost;
  это следует из указанного запрета компонентов. Cleanup старого binding завершить до замены.

**IMPORTANT: контракт регистрации Mirror исполним; соблюдать проверки.**

- План, строки 147–152, размещает регистрацию generated variants первым шагом
  `GameNetworkManager.OnStartClient`, после base и до NetworkUxrIdentity. Mirror уже выполнил
  штатный RegisterClientMessages (`Assets/ThirdParty/Mirror/Core/NetworkManager.cs:408–412`,
  `:738–754`); callbacks/handlers текущего проекта находятся в
  `GameNetworkManager.cs:86–97`. Это конкретная интеграционная граница, а не неизвестный
  «до StartClient» hook.
- UniqueId avatar остаётся у UxrMirrorAvatar: исключение avatar из дополнительного Align
  подтверждается `NetworkUxrIdentity.cs:377–385`. При authoring проверить у каждого variant
  собственный ненулевой assetId, отличие от base и другого variant, sceneId=0 и ровно одну
  root NetworkIdentity. Mirror назначает assetId из GUID asset path
  (`NetworkIdentity.cs:388–410`, `:542–549`); проверять уже сохранённый/reimported variant,
  не предполагать, что prefab override автоматически корректен.
- Registry регистрирует только производные исполнения, не выбирает внешность, не хранит
  runtime holder и не добавляет NetworkBehaviour после Spawn. Повторный start/stop и late join
  должны проверить registration idempotency и снятие только своих registrations, без удаления
  чужих prefab/handlers при совпадении assetId; коллизия — отказ Validate, не overwrite.

**MINOR / детализация при исполнении:** Build/Validate должен проверять уникальность source
mapping, соответствие variant именно своему base, serialized flags и Animator/render/hitbox
binding после Awake на обеих сторонах. BotCombatAssets — производный execution mapping;
в нём нельзя повторять Team/AvatarIndex fallback или выбирать альтернативную живую модель.
Не менять human/stress (`marker=false`) пути и не исследовать Heavy. Независимый authoring
variant разрешён сейчас; factory cutover обычных ботов зависит от полного equipment/runtime
перехода и согласования существующего weapon owner. Новые gameplay NUnit — после человеческой
приёмки; до неё временные probes, существующие suites и Android, как указано в плане.

Это одобрение уточнённого плана, не подтверждение готовых variants, remote/Quest acceptance
или разрешение обойти внешний equipment/readiness контракт.

Архитектура NPC в целом исполнима: один видимый Humanoid Animator, один socket writer,
существующие владельцы фаз/жизни/боеприпасов, общий equipment/use context и сообщение Mirror
без добавления NetworkBehaviour после Spawn. Task 1 корректно выделяет проверку реального
Humanoid/mesh/hitbox binding и согласование readiness с владельцем weapon-system.
Проводить весь runtime preflight до одобрения плана не требуется. Зависимый оружейный путь
нельзя начинать до ответа настоящего владельца weapon-system.

Повторное ревью исправленного плана выполнено 2026-10-07. BLOCKER B1 и IMPORTANT I1
устранены в контракте плана; остающихся препятствий к началу независимого исполнения нет.
Пользователь уже поручил начать после APPROVED, дополнительное разрешение не требуется.
Согласование readiness/equipment с настоящим владельцем weapon-system остаётся обязательной
зависимостью: это одобрение её не снимает.

Исполнимость подтверждена на уровне плана, не готовой реализации. Task 1 должен проверить
реальные MEF/Cyborg Humanoid/finger/mesh/hitbox bindings; отсутствие валидного binding ведёт
к fail-closed и editor authoring, без runtime IK/pose-copy fallback.

## BLOCKER — устранён в плане

### B1. Proxy Blaze без локального Animator

Исходный план обещал navigation proxy без второго Animator/скелета, но перенаправлял
только animation manager. Текущий SDK требует более полной границы:

- `Assets/ThirdParty/Blaze AI/Scripts/BlazeAI.cs:13`: RequireComponent(Animator).
- `BlazeAI.cs:205`: `anim` имеет private setter.
- `BlazeAI.cs:403–408`: Awake получает локальный Animator и создаёт AnimationManager.
- `BlazeAI.cs:659–668`: OnEnable повторно получает локальный Animator.
- `BlazeAI.cs:1277`: чтение normalizedTime идёт прямо через `anim`, минуя animation manager.
- `Assets/ThirdParty/Blaze AI/Scripts/Classes/AnimationManager.cs:12` и `:59`:
  manager удерживает переданный Animator и вызывает CrossFadeInFixedTime.

**Устранено:** исправленный план, строки 87–93 и 124–128, задаёт явный
`BindAnimatorSource(Animator)`, общий resolver для Awake/OnEnable/refresh/direct reads и
AnimationManager, отсутствие принудительного локального Animator, fail-closed и сохранение
обычного local-Animator пути для прочих Blaze actors. Проверки re-enable/category switch
названы явно; файл SDK-патча и его документ указаны, номера согласуются в Task 1.
Никакая заглушка Animator на proxy не требуется. Реализация этого порта входит в Task 2.

## IMPORTANT — устранён в плане

### I1. Human held-motion policy должна включать root recoil и оба resistance entrypoint

Исходный план задавал только запрет translation/rotation resistance для human primary
firearm grip. Для жёсткого следования существенны также следующие SDK writers:

- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabManager.Manipulation.cs:1662–1665`:
  сопротивление в FinalizeManipulation.
- Тот же файл, `:2164–2167`: отдельный путь SetPositionAndRotationUsingConstraintsInternal.
- `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs:691–716`:
  ConstraintsApplied изменяет position/rotation корня оружия отдачей.

**Устранено:** исправленный план, строки 158–167, охватывает оба resistance entrypoint,
firearm transform recoil, единственного held-pose writer, отсутствие serialized/mutable
копий resistance и human one-hand/two-hand probes после Manipulation/PostProcess,
включая выстрел и смену primary/support. Точная классификация primary firearm grip без
ancestry-эвристики сохранена в строках 85–86. Звук/снаряды/эффекты и учёт остаются у
существующего оружейного владельца. Это достаточный контракт для реализации и проверки.

## MINOR / замечания для исполнения

### M1. Смертельный drop должен сохранить существующий импульс экипировки

**Учтено:** исправленный план, строки 94–95, включает DeathDropEjection в общий equipment
teardown; строки 148–149 и 179–180 требуют проверку death/drop/respawn/старого тела.

`Assets/Scripts/Player/Corpse/DeathDropEjection.cs:52–63` перечисляет только SDK hands и
карманы. При переходе на NPC equipment binding его предмет не попадёт в этот перечень.
Расширить общий enumeration equipment либо подключить owner-managed release к существующему
ejection до снятия binding. Это часть заявленного death/drop lifecycle, отдельный lifecycle
бота создавать не требуется. `PlayerLoadoutManager.cs:148–175` и
`EquipmentStrip.cs:75–101` также имеют SDK-only обходы, уже учтённые планом.

### M2. Сетевой snapshot описать на уровне всех реально используемых слоёв

**Учтено:** исправленный план, строки 57–64, задаёт состояния/фазы/переход/duration/weight
всех слоёв, authored BlendTree/aim параметры и server time, bounded Spawn buffering,
host без повторного применения и единственный root transport. Строки 155–169 покрывают
netId+epoch, late join и stale/dead snapshots. При реализации сохранить оба состояния
перехода и очищать очередь на disconnect; это обычная детализация названного контракта,
новой архитектурной развилки или препятствия для APPROVED здесь нет.

Если Task 1 добавит слой пальцев/BlendTree, снимок обязан включать достаточные state/phase,
layer weights и параметры этого controller, чтобы late join воспроизводил ту же позу.
Один state hash базового слоя недостаточен для такой композиции. Для перехода важны оба
состояния/их фаза и progression перехода; применять Play каждый кадр вместо реконструкции
нельзя. Очередь до Spawn ограничена и очищается на disconnect/despawn; stale equipment epoch
не должен возвращать освобождённый socket.

## Подтверждённые сильные стороны и условия проверки

- Существующий скрытый риг действительно создаётся в `BotCombatDriver.cs:135–152`;
  план заменяет его видимым Animator, не вводит HumanPose-copy.
- Текущие wrist/head writers находятся в `BotBody.cs:210–248`, UXR stages в
  `BotRuntimeController.cs:69–98`, grab/hand-pose путь в `BotGunner.cs:207–250` и `:271–302`;
  их удаление в плане явно задано.
- Отключение avatar controller подавляет SDK hand-pose update:
  `UxrManager.cs:1974–1987`; надо дополнительно отключить отдельные IK/legs writers,
  проверить это на сервере и клиенте до первого клипового кадра.
- `WeaponReadinessController.cs:277–284`, `:765–773`, `:806–816` подтверждают, что нужен
  общий use context и epoch/correlation вместо фиктивного grabber или обхода chamber logic.
- `StateEventAuthority.cs:85–95` и `:137–158` определяют авторство через SDK holders;
  equipment resolver должен подключаться к этой существующей границе, включая ShouldSend,
  а не только давать боту world-author permission.
- `ServerAuthoredAvatar.cs:18–30` требует настроить syncDirection до Spawn. Отключение
  конкурирующих bone NT не должно добавлять/удалять сетевые компоненты после Spawn;
  root NT остаётся единственным транспортом root.
- Новые gameplay NUnit до пользовательской приёмки не нужны и запрещены правилами задачи.
  Требуемые временные RED→GREEN probes, Android и существующие suites в плане есть;
  compile и socketless probes не подтверждают remote/Quest/визуальный хват.

Это ревью плана, не принятие механики и не утверждение, что Unity/remote проверка выполнена.
