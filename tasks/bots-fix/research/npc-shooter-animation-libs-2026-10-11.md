# Готовые решения анимации вооружённых NPC в Unity: обзор для ботов

Дата: 2026-10-11. Задача: bots-fix. Тип: исследование. Код не менялся, Unity не запускалась.
Проверено: исходники `Assets/ThirdParty/{RootMotion,KINEMATION,Blaze AI}`, бот-код `Assets/Scripts/Bots`, веб-источники (ссылки в каждом разделе и в конце).
Обозначения: **[факт]** подтверждено файлом проекта или страницей поставщика; **[неуверенно]** вывод по косвенным данным
или по устаревшей/кэшированной странице; **[оценка]** моя инженерная оценка, её нужно проверить замером.

## 1. Вывод

1. Наши проблемы (опорная рука не на цевье, крен корпуса из клипов, завал оружия в движении, отдача Shoot-клипа сбивает прицел,
   много калибровки) решает слой процедурной постобработки поверх клипа: **поза прицеливания → aim-решатель на позвоночнике →
   IK опорной руки к точке хвата на оружии → процедурная отдача**. Для этого не нужно менять ИИ и сетевую модель.
   Готовые комплексные контроллеры (Opsive, Invector, Emerald) заменяют вместе с этим слоем ИИ, перемещение и оружие,
   поэтому для нас они не подходят.
2. **Первая рекомендация — Final IK 2.5. Он уже лежит в проекте, но игровой код его не использует.**
   Связка `AimIK` (позвоночник, ось = ствол, pole против крена) + `LimbIK` (левая рука к точке хвата) повторяет
   официальный демо-рецепт «Second Hand On Gun» и даёт ручной порядок решателей. Это совпадает с нашим конвейером:
   ручной `Animator.Update(0)` в `LateUpdate`, после него следование оружия за сокетом на order 10000.
   Отдачу лучше брать из уже имеющегося KINEMATION PRAS `RecoilAnimation`: это отдельный MonoBehaviour, на выходе
   у него `OutLoc/OutRot`. Shoot-клип с отдачей тогда не нужен.
3. Запасной вариант — **Unity Animation Rigging 1.4.1** (бесплатный, Burst/Animation Jobs). Его стоит брать, если замер
   на Quest покажет, что Final IK на нужном числе ботов слишком дорог. Есть риск совместимости с нашим ручным `Animator.Update(0)`.
4. Минимальный вариант — **встроенный humanoid IK Unity** (`OnAnimatorIK`) + клипы позы прицеливания + PRAS.
   Внешних зависимостей нет, но точность наведения ниже. Опорная рука может отставать на кадр, потому что оружие
   следует за кистью уже после IK-pass.
5. Сеть во всех трёх вариантах одинаковая: реплицируются **намерение и цель**, а не поза. Сервер передаёт точку
   или netId цели, вес прицеливания и события выстрела. Каждый клиент решает IK у себя. Отдельно для главного агента:
   ворота выстрела `BotGunner.Sees` берут **реальный луч дула** на сервере. Значит, IK должен решаться и на сервере
   (headless тоже), иначе нужно развязать баллистику и визуал (раздел 5).

## 2. Факты о текущей архитектуре, важные для выбора

- `BotClipBody` (order 9000) держит единственный humanoid `Animator`. На сервере он публикует `AnimationSnapshot`
  (состояние/время/переход) и `AimPitch` (SyncVar, квант 0.25°). Клиент в `LateUpdate` делает `Animator.Play/CrossFade`
  и **`Animator.Update(0f)` вручную**, `cullingMode = AlwaysAnimate`. Оружие следует за сокетом правой кисти на order 10000.
  Источник: `Assets/Scripts/Bots/BotClipBody.cs` (стр. ~205–317). **[факт]**
- `BotAim` (339 строк) + `BotAimMath` дают поправку курса δ (±15°, интеграл ±3°) и pitch-контур через аддитивный aim-слой.
  Калибровка лежит в `BotCombatAssets.aims` (фазовые таблицы, basePitch, gain). **[факт]**
- `BotGunner.TryGetMuzzleForwardHit` — единственный источник истины для допуска выстрела: Raycast из `ShotSource.forward`. **[факт]**
- В проекте есть Mixamo-клипы с винтовкой: 8 направлений ходьбы, бег, спринт, присед, `Rifle_IdleAiming`,
  `Rifle_IdleCrouchingAiming` (`Assets/Art/Animations/Locomotion/MixamoRifle`), а также MixamoPistol. **[факт]**
  Значит, позу прицеливания для верхнего слоя по маске взять есть откуда.
- UltimateXR содержит свои решатели (`UxrArmIKSolver`, `UxrCcdIKSolver`, `UxrBodyIK`), но они рассчитаны на VR-тело игрока
  с трекингом. Для NPC под Animator готового aim-решателя в SDK нет. **[факт по составу файлов; применимость — неуверенно]**
- Пакет `com.unity.animation.rigging` **не установлен** (`Packages/manifest.json`). Unity 6000.4.1f1, URP 17.4.0. **[факт]**

### Версии уже имеющихся SDK

| SDK | Версия в проекте | Где видно |
|---|---|---|
| Final IK (RootMotion) | **2.5** | `RootMotion/FinalIK/FinalIK ReadMe.rtf` («Welcome to Final IK version 2.5»), `FinalIK Change Log.pdf` (верхняя запись 2.5) |
| KINEMATION Core (KAnimationCore) | **1.2.1** | `KINEMATION/Shared/KAnimationCore/kinemation-package.json` |
| KINEMATION FBX Exporter | 1.2.0 | `Shared/FbxExporter/kinemation-package.json` |
| Tactical Shooter Pack / KShooterCore / PRAS | номер версии в пакете не указан; копирайт «2026 KINEMATION» | `TacticalShooterPack/Scripts/Animation/TacticalShooterPlayerJob.cs` |
| Blaze AI | номер версии в пакете не найден **[неуверенно]**; актуальная в Asset Store — 3.4.22 | `Blaze AI/Readme.md`, `Docs.pdf` |

В составе Final IK есть всё нужное: `IK Components/{AimIK,LimbIK,FullBodyBipedIK,ArmIK,LookAtIK}`, `Grounder/*`,
`Tools/{AimController,AimPoser,Recoil,HitReaction,BodyTilt}`, демо `_DEMOS/Aim IK/Second Hand On Gun.unity`,
`_DEMOS/FBBIK/Third Person Shooter (Recoil).unity`. Код решателей обычный C# на главном потоке, без Burst и Jobs
(grep по `Unity.Jobs|IAnimationJob|Burst` в FinalIK пуст). **[факт]**

## 3. Разбор решений

Обозначения проблем: **P1** опорная рука, **P2** крен корпуса, **P3** завал оружия в движении, **P4** отдача сбивает прицел,
**P5** объём самописной калибровки.

### 3.1 Final IK (RootMotion) — AimIK, LimbIK, FullBodyBipedIK, GrounderIK, Recoil

- **Что решает.** P1: `LimbIK` на левую руку к трансформу хвата на оружии. Официальный рецепт: «When aiming 2-handed weapons,
  we can use only the spine bones… If we used the arm bones, the other hand would lose contact», затем LimbIK/FBBIK возвращают
  вторую руку ([AimIK docs](http://www.root-motion.com/finalikdox/html/page1.html)). Демо-скрипт
  `_DEMOS/Aim IK/Scripts/SecondHandOnGun.cs` выключает компоненты и вызывает `aim.solver.Update()` → `leftArmIK.solver.Update()`
  вручную в `LateUpdate`. P2/P3: AimIK с `Aim Transform` = дуло и осью = forward ствола доворачивает цепочку позвоночника так,
  что ствол смотрит в цель независимо от фазы клипа. `poleTarget` держит корпус вертикально, крен клипа подавляется.
  P4: документация советует на время отдачи/перезарядки менять `solver.axis` или вес. Отдачу при этом лучше прикладывать
  поверх уже решённого прицела (см. 3.2 про PRAS). Компонент `Tools/Recoil.cs` в Final IK работает через FBBIK
  («Procedural recoil using FBBIK»), а FBBIK для ботов на Quest заметно дороже. **[факт по коду; цена — оценка]**
  P5: уходят фазовые таблицы, basePitch/gain, δ-интеграторы. Остаётся только локальная поза хвата для каждого класса оружия
  (точка на цевье) и ось ствола.
- **Сеть.** Решатель работает на трансформах после Animator, детерминирован при одинаковых входах. Нужно реплицировать
  цель (netId + смещение или точку), вес прицеливания (0/1 с плавным переходом) и события выстрела, которые уже есть.
  Клиенты решают локально. SyncVar `AimPitch` и δ становятся не нужны. **[оценка]**
- **Quest.** Главный поток, без Burst. Автор рекомендует для экономии `renderer.isVisible` → вес 0 и iterations = 0 на дистанции
  ([форум Final IK](https://discussions.unity.com/t/final-ik-full-body-ik-aim-look-at-fabrik-ccd-ik-1-0-released/525769?page=88)).
  Есть жалобы на CPU при многих персонажах с FBBIK+PuppetMaster (там же). AimIK (3–4 кости, 1–2 итерации) + LimbIK
  (аналитическое решение) — **порядка десятков мкс на бота на ПК, на Quest в 3–5 раз больше [оценка, замерить]**.
  При 8–16 ботах это ниже 1 мс/кадр, если не включать FBBIK/Grounder.
- **Совместимость.** Работает с любым humanoid (BipedReferences берутся из Animator), ретаргет не мешает: решатель видит итоговые
  трансформы. С Blaze не пересекается: Blaze двигает корень/proxy, IK — кости. `CoverShooterBehaviour.hitPoint/shootingAt`
  (Blaze API, `Docs.pdf` стр. 47+) можно сразу использовать как цель.
- **Лицензия/цена.** Asset Store, $90 за место, **уже куплен** (в проекте). **[факт]**
- **Актуальность.** 2.5, релиз 10.04.2026, совместим с 6000.4.2f1 / 6000.0.72f1 / 2022.3 / 2021.3, Built-in/URP/HDRP
  ([Asset Store](https://assetstore.unity.com/packages/tools/animation/final-ik-14290)). **[факт]**
- **Миграция.** Выбрасываем: δ/pitch-контуры `BotAim`, аддитивный aim-слой и его BlendTree, фазовые таблицы `BotCombatAssets.aims`,
  SyncVar `_aimPitchCenti`, отдачу внутри Shoot-клипа. Остаётся: Blaze, `BotClipBody` snapshot (состояние/время),
  сокет правой кисти, `BotGunner`, сетевые события выстрела. Добавляется: верхняя поза прицеливания (маска рук и корпуса
  с `Rifle_IdleAiming`/пистолетной позой), компонент-«конвейер IK» после `Animator.Update(0)`, точка хвата на оружии.

### 3.2 KINEMATION — Tactical Shooter Pack, KShooterCore, PRAS, FPS Animation Framework, CAS

- **Tactical Shooter Pack (в проекте).** Это FPS-игрок: `TacticalShooterPlayer : KShooterCharacter`, жёстко связан с
  `FPSCameraAnimator` (yaw/pitch из камеры), процедурная часть идёт в `TacticalShooterPlayerJob : IAnimationJob`
  со своим `TwoBoneIkChain` для рук. **Поддержки NPC и третьего лица в документации нет**
  ([TSP docs](https://kinemation.gitbook.io/tactical-shooter-pack-unity/)). Адаптировать к ботам — значит переписать
  ввод камеры на цель ИИ. **[факт по коду; адаптация — оценка]**
- **PRAS `RecoilAnimation` (в проекте).** Отдельный MonoBehaviour: `RecoilAnimData`, `FireMode`, свойство
  `RecoilTransform => new KTransform(OutLoc, OutRot)`. Это генератор кривых отдачи, к FPS-камере он не привязан. **[факт]**
  **Это готовое решение P4 для любого из вариантов:** смещение прикладывается к кисти/оружию после aim-решателя,
  вес прицела не трогается. Решение локальное на каждом клиенте по событию выстрела, сеть не нагружает.
- **FPS Animation Framework.** 4.9.3 (26.01.2026), $29.99, тестирован на 2022.3
  ([Asset Store](https://assetstore.unity.com/packages/tools/animation/fps-animation-framework-238641)).
  Документация помечена как **«Legacy»** ([llms.txt](https://kinemation.gitbook.io/fps-animation-framework/llms.txt)).
  Слои: Left/Right Hand IK, Look, Recoil, Sway, Leg IK. Продавец пишет об «одном наборе анимаций для first и third person»
  ([itch.io](https://kinemation.itch.io/fps-animation-framework)), об NPC/AI и сети — ничего. **[неуверенно]**
- **Character Animation System (CAS) + FPS Addon** — преемник FPSAF. CAS 2.4.2 (17.09.2026), $79.99, Unity 6000.0.78
  ([Asset Store](https://assetstore.unity.com/packages/tools/animation/character-animation-system-320309)).
  FPS Addon 1.4.1 (16.08.2026), $29.99 ([Asset Store](https://marketplace.unity.com/packages/tools/animation/fps-addon-for-cas-321490)).
  Построен на Playables и работает рядом с Animator ([Architecture](https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/architecture.md)).
  Модификаторы: Aim At (кости позвоночника + `aimAtPosition`, пределы yaw/pitch), Two Bone IK, Full-body IK, Foot IK, Look,
  Stabilize Bone ([список](https://kinemation.gitbook.io/character-animation-system-docs/llms.txt),
  [Aim At](https://kinemation.gitbook.io/character-animation-system-docs/character-animation-system/animation-modifiers/aim-at.md)).
  Attach Hand (FPS Addon) ставит левую руку к хвату с позой пальцев
  ([Attach Hand](https://kinemation.gitbook.io/character-animation-system-docs/fps-addon/modifiers/attach-hand.md)).
  Против CAS: FPS-скелет добавляет IK-объекты, оружие и кисти привязаны к голове
  ([Skeleton & IK](https://kinemation.gitbook.io/character-animation-system-docs/fps-addon/quickstart/skeleton-and-ik.md)).
  Перед импортом требуется удалить папки `KAnimationCore`, `ScriptableWidget`, `MotionWarping`
  ([Introduction](https://kinemation.gitbook.io/character-animation-system-docs/quickstart/introduction-to-cas.md)).
  **Это прямой конфликт с нашим KAnimationCore 1.2.1 под TSP и оружейными инструментами Editor.** Про NPC, сеть,
  производительность на многих персонажах и Quest в документации ничего нет. **[неуверенно]**
  Для ботов CAS сейчас выглядит рискованным, хотя по качеству позы он, вероятно, лучший.

### 3.3 Unity Animation Rigging (Multi-Aim, Two Bone IK, Multi-Parent, Rig Builder)

- **Что решает.** P1 (`TwoBoneIKConstraint` на левую руку к хвату), P2/P3 (`MultiAimConstraint` на 2–3 кости позвоночника
  с осью ствола, ограничения), привязка оружия (`MultiParentConstraint`). P4 — через Override Transform или PRAS-смещение.
  P5 — как у Final IK.
- **Сеть.** Как у Final IK: веса и цель приходят из сети, решение локальное.
- **Quest.** Constraint-ы построены на C# Animation Jobs, есть Burst ([CHANGELOG](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/changelog/CHANGELOG.html)).
  Масштабируется по потокам лучше Final IK. Цена — дополнительный PlayableGraph и синхронизация трансформов на каждого персонажа.
  Публичных замеров для Quest не нашёл. **[оценка: сопоставимо или лучше Final IK при 10+ ботах, замерить]**
- **Совместимость.** Humanoid и generic. **Риск:** RigBuilder встраивается в граф Animator, а мы сами вызываем
  `Animator.Update(0)` в `LateUpdate`. Нужно проверить, что constraint-ы выполняются в этом вызове, и как задаются цели,
  которые зависят от позы оружия текущего кадра (оружие следует за кистью на order 10000, уже после графа). **[неуверенно]**
- **Лицензия/цена.** Бесплатный пакет Unity. Для 6000.4 — версия **1.4.1, Released**
  ([Unity Manual](https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.animation.rigging.html)).
- **Актуальность.** Пакет живой, но последняя содержательная запись changelog — 1.3.0 (2023). Похоже на режим поддержки. **[неуверенно]**
- **Миграция.** Та же, что у Final IK, плюс пакет в manifest и rig-иерархия в префабах клиповых тел.

### 3.4 Встроенный humanoid IK Unity (`OnAnimatorIK`) + aim-offset клипы (подход Mixamo)

- **Что решает.** `SetIKPosition/Rotation(AvatarIKGoal.LeftHand)` к хвату даёт P1, `SetLookAtPosition` с `bodyWeight`
  частично решает P2/P3 ([Unity Manual: IK](https://docs.unity3d.com/6000.4/Documentation/Manual/InverseKinematics.html)).
  Aim-offset — 2D BlendTree поз прицеливания (вверх/вниз/влево/вправо), так делают с Mixamo-наборами. Наш текущий
  pitch-слой и есть такой подход, и калибровка у него остаётся.
- **Ограничения.** LookAt наводит голову и корпус, а не ствол: без доводки ствол промахивается на величину плеча.
  Цель левой руки берётся из позы оружия **прошлого кадра**: IK-pass идёт внутри `Animator.Update`, а оружие следует
  за кистью позже, на order 10000. В движении возможно дрожание. **[оценка]**
- **Цена/Quest.** Бесплатно, считается внутри Animator, самый дешёвый вариант. **[оценка]**
- **Миграция.** Минимальная, но P5 решается лишь частично.

### 3.5 Animancer Pro

8.4.0 (21.06.2026), Unity 2022.3+/6, ~$90–99 ([Asset Store](https://assetstore.unity.com/packages/tools/animation/animancer-pro-293522),
[changes](https://kybernetik.com.au/animancer/docs/changes)). Это замена Animator Controller на проигрывание клипов из кода.
Humanoid IK включается флагом `ApplyAnimatorIK` ([IK docs](https://kybernetik.com.au/animancer/docs/manual/ik)).
Aim и опорную руку **не решает**. Мог бы упростить наш snapshot «состояние+время», но эта часть уже работает.
**Для текущих проблем не нужен.**

### 3.6 Opsive Ultimate Character Controller (+ AI, + сеть)

- UCC 3.3.x, поддержка Unity 6.5 в 3.3.6 ([release notes](https://opsive.com/news/ultimate-character-controller-3-3-6-released/)).
  AI-агент: `Local Look Source` + `NavMeshAgent Movement`, Behavior Designer
  ([AI docs](https://opsive.com/support/documentation/ultimate-character-controller/character/artificial-intelligence/)).
  `Character IK` — humanoid `OnAnimatorIK`, верхняя рука по вертикальному прицелу, `Non Dominant Hand IK Target` на предмете
  ([IK docs](https://opsive.com/support/documentation/ultimate-character-controller/character/inverse-kinematics/)).
  То есть P1–P3 решаются, если весь персонаж живёт на UCC.
- **Сеть.** Mirror-адаптер «Ultimate Mirror (Networking) Controller» на странице Asset Store помечен **deprecated**
  ([Asset Store](https://assetstore.unity.com/packages/add-ons/ultimate-mirror-networking-controller-250532)). Был в pre-release 0.8–0.9.
  Официальный сетевой add-on — PUN. **[факт по странице; продажи через opsive.com — неуверенно]**
- **Миграция.** Заменяет Blaze (или требует связки), перемещение, оружие UltimateXR и наше тело бота. **Не подходит.**

### 3.7 Invector Third Person Controller — Shooter Template (AI Shooter)

3.0.0a (09.09.2026), $89.99, Unity 6000.0 URP ([Asset Store](https://assetstore.unity.com/packages/tools/game-toolkits/invector-third-person-controller-shooter-template-84583)).
В шаблон входят AI-стрелки и IK-подстройка рук под оружие. **[неуверенно: подробности из документации не получены]**
Mirror — только сторонний «Easy Multiplayer – Invector» для шаблонов 2.6.1b–2.6.2c ([gumroad](https://cyberbulletgames.gumroad.com/l/ICNXT)),
совместимость с 3.0 не подтверждена. Это полный контроллер со своим ИИ и оружием. **Не подходит** по той же причине, что Opsive.

### 3.8 Emerald AI (2024 → 2026)

Emerald AI 2026 1.3.4 (14.09.2026), $60, Unity 6000.0 ([Asset Store](https://marketplace.unity.com/packages/tools/behavior-ai/emerald-ai-2024-268519)).
Есть ranged-оружие с hand IK: профиль `EmeraldAIHandIK`, точка левой руки настраивается вручную для каждой пары ИИ/оружие
([wiki](https://github.com/Black-Horizon-Studios/Emerald-AI/wiki/Setting-up-Ranged-Weapon-Hand-IK)).
Есть наведение 1–3 костями позвоночника на `Ranged Attack Transform`
([wiki](https://github.com/Black-Horizon-Studios/Emerald-AI/wiki/Using-the-Built-in-IK-System)). Wiki описывает 3.0;
что именно изменилось в 2024/2026 и опирается ли новая версия на Animation Rigging — **неуверенно**. Сети нет.
Это замена Blaze. **Не подходит.** Полезна как подтверждение схемы «aim позвоночником + IK левой руки».

### 3.9 Blaze AI (в проекте)

3.4.22 (22.06.2026), $40 ([Asset Store](https://assetstore.unity.com/packages/tools/behavior-ai/blaze-ai-engine-194525)).
Стрельба Blaze — это только тайминги (Shoot Every, Single Shot Duration, Total Shoot Time) и имена анимаций
Idle/Move/Shooting Anim (`Blaze AI/Docs.pdf`, «Setting Up Cover Shooter AI»). **Собственного aim/IK нет.** В отзыве
на Asset Store отмечено отсутствие интеграции с Final IK ([reviews](https://assetstore.unity.com/packages/tools/behavior-ai/blaze-ai-engine-194525/reviews)).
Полезное API: `CoverShooterBehaviour.hitPoint` и `shootingAt` как источник цели для IK. Blaze остаётся владельцем решений ИИ.

### 3.10 Прочее

- **Game Creator 2 Shooter** — свой стек (Character, Perception, Behavior), сеть сторонняя
  ([GC2](https://docs.gamecreator.io/gamecreator/), [network layer](https://arawn-software-publishing.gitbook.io/networking-layer-for-gc2/network-modules/shooter)).
  Это замена всего, не подходит.
- **Malbers Animal Controller** 1.4.8–1.4.9, Unity 6 ([Fab](https://fab.com/listings/b9d614cd-1918-45b7-a194-7bd643ad0937)).
  Подтверждённого стрелкового aim/IK для NPC в найденных материалах нет. **[неуверенно]**
- **Tactical (Shooter) AI** — снят с продажи ([publisher](https://assetstore.unity.com/publishers/4375)). **[неуверенно, что это тот же продукт]**
- **Alek AI IK** — aim IK только для фреймворка Alek AI
  ([Asset Store](https://assetstore-fallback.unity.com/packages/tools/behavior-ai/alek-ai-ik-265517)), не подходит.

## 4. Таблица сравнения

| Решение | P1 рука | P2 крен | P3 завал в движении | P4 отдача | P5 калибровка | Сеть (что реплицировать) | Quest CPU [оценка] | UXR humanoid / Blaze | Цена | Актуальность | Миграция |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **Final IK** AimIK+LimbIK (+PRAS) | да | да (pole) | да | да, с PRAS | сильно меньше | цель+вес, решение локальное | низкая–средняя, главный поток | да / да | в проекте | 2.5, апр 2026, Unity 6.4 | средняя |
| **Animation Rigging** Multi-Aim+TwoBoneIK (+PRAS) | да | да | да | да, с PRAS | сильно меньше | то же | низкая, Burst-jobs | да / да | бесплатно | 1.4.1 для 6.4, развитие вялое | средняя+, риск с ручным Update |
| **Humanoid IK** + aim-offset (+PRAS) | да, с лагом в кадр | частично | частично | да, с PRAS | немного меньше | то же | минимальная | да / да | бесплатно | встроен | малая |
| KINEMATION CAS + FPS Addon | да | да | да | да | меньше | не описано | неизвестно | неясно для NPC / да | $110 | 2.4.2, сен 2026 | большая, конфликт KAnimationCore |
| KINEMATION TSP (в проекте) | да (только FPS) | — | — | да | — | не описано | — | FPS-камера / нет | в проекте | 2026 | не для NPC |
| Animancer Pro | нет | нет | нет | нет | нет | — | — | да / да | ~$90–99 | 8.4.0, июн 2026 | не по задаче |
| Opsive UCC | да | да | да | частично | да | Mirror-адаптер deprecated | средняя | свой контроллер / замена | платно | 3.3.x | очень большая |
| Invector Shooter | да [неуверенно] | да | да | да | да | сторонний Mirror для 2.6.x | средняя | свой контроллер / замена | $89.99 | 3.0.0a, сен 2026 | очень большая |
| Emerald AI 2026 | да (ручная точка) | частично | частично | нет данных | средне | нет | средняя | да / замена | $60 | 1.3.4, сен 2026 | очень большая |
| Blaze AI (в проекте) | нет | нет | нет | нет | — | — | — | — | в проекте | 3.4.22, июн 2026 | остаётся |

## 5. Сквозной вопрос для главного агента: баллистика и визуал

Сейчас допуск и направление выстрела берутся из **реального луча дула** на сервере (`BotGunner.Sees`,
`TryGetMuzzleForwardHit`). Возможны две модели:

- **A. Поза — истина и на сервере.** IK решается и на сервере (Animator `AlwaysAnimate` уже стоит), луч остаётся из дула.
  Плюс: что видно, то и летит. Минус: процессорное время IK на сервере, результат зависит от одинаковости решения
  на клиентах (расхождение в пределах долей градуса).
- **B. Баллистика от намерения.** Сервер стреляет по решению прицела (дуло + направление на цель + разброс), а поза
  только косметика на клиентах. Плюс: калибровка исчезает почти полностью, на headless-сервере IK не нужен.
  Минус: ствол визуально может на долю градуса не совпадать с трассой. Это меняет владельца истины луча — решать
  с weapon-system и network.

Обе модели работают с любым из рекомендуемых вариантов. Выбор архитектурный, остаётся за главным агентом.

## 6. Рекомендации

### Вариант 1 (основной): Final IK + поза прицеливания + PRAS

Конвейер одного компонента-владельца позы бота, на клиенте и (в модели A) на сервере, в `LateUpdate` после `Animator.Update(0)`:
1. Базовый слой — локомоция из Mixamo Rifle/Pistol. Верхний слой по маске (руки + позвоночник) — **статичная поза прицеливания**
   (`Rifle_IdleAiming` / пистолетная). Покачивание оружия из клипов ходьбы уходит, P3 решается.
2. `AimIK`: кости Spine/Chest/UpperChest (+ при необходимости плечо правой руки с малым весом), `Aim Transform` = дуло,
   `axis` = forward ствола, `poleTarget` = вверх корня (P2). Цель — из сети/Blaze.
3. Позиция оружия на сокете правой кисти — сразу после AimIK, явным вызовом: порядок нужно перестроить, сейчас это отдельный
   order 10000.
4. `LimbIK` левой руки → трансформ хвата на цевье (одна точка на класс оружия; можно взять из существующей второй точки
   хвата UltimateXR — **гипотеза, проверить**) (P1).
5. PRAS `RecoilAnimation.RecoilTransform` → смещение правой кисти/оружия, затем повторный LimbIK, чтобы левая рука ушла вместе
   с оружием (P4).
6. LOD: вне видимости или дальше N м — вес 0 или пропуск LimbIK.

Убирает: `BotAim` (δ и pitch-интеграторы), аддитивный aim-слой, фазовые таблицы и `aims` в `BotCombatAssets`, SyncVar AimPitch.
Оценка работ **[грубо]**: прототип на BotCombatStand 2–3 дня; интеграция и перестройка порядка сокета 3–4 дня;
сетевые поля цели и веса 1 день; тесты после приёмки пользователем 1–2 дня. **Итого 7–10 дней.**

### Вариант 2 (если Final IK дорог на Quest): Unity Animation Rigging + та же схема + PRAS

Multi-Aim (позвоночник) + Two Bone IK (левая рука) + Multi-Parent (оружие). Сначала прототипом проверить совместимость
с ручным `Animator.Update(0)` и цели, которые зависят от позы оружия текущего кадра. **Итого 8–12 дней**, из них 1–2 дня риска на порядок кадра.

### Вариант 3 (минимальный): humanoid IK + поза прицеливания + PRAS

`OnAnimatorIK`: LeftHand → хват, `SetLookAtWeight(1, bodyWeight≈0.3–0.5)` → цель. Небольшой остаточный доворот корня (оставить
упрощённый δ без фазовых таблиц). Отдача через PRAS. **Итого 3–5 дней**, но P2/P5 решаются частично, а у левой руки возможен лаг в кадр.

Не рекомендую сейчас: Opsive, Invector, Emerald, Game Creator — замена ИИ, оружия и сети. KINEMATION CAS — конфликт KAnimationCore,
FPS-ориентированный скелет, нет данных про NPC и Quest. К CAS можно вернуться отдельным исследованием, если понадобится
качество позы уровня FPS-рук. Animancer — не по задаче.

## 7. Риски

- **Порядок кадра.** Animator → aim → сокет оружия → левая рука → отдача: сейчас сокет на отдельном order 10000.
  Его нужно сделать шагом одного конвейера, иначе будет лаг или дрожание кадра. Один владелец позы — по правилам проекта.
- **Квалификация осей.** Ось ствола и up оружия разные у разных моделей (Hands Weapons Pack, KINEMATION).
  Ошибка оси даёт ровный промах, который трудно отличить от ошибки IK.
- **Пределы AimIK.** Цель ближе, чем расстояние от цепочки до дула, даёт неустойчивость (документация Final IK).
  Нужен минимум дистанции и clamp, по аналогии с нынешним `MinDistance` 0.5 м.
- **Сервер.** В модели A решатель обязан работать и на headless-сервере. Там Animator включён и обновляется штатно
  (`AlwaysAnimate`, без ручного `Update(0)`; `BotClipBody.cs` стр. ~111, 207). Значит, IK-конвейер на сервере должен
  выполниться в `LateUpdate` **до** проверки `Sees`/выстрела этого кадра. Порядок нужно проверить явно.
- **Quest CPU.** Все цифры — оценки. При 16+ ботах нужен LOD по видимости и дистанции.
- **Конфликт KINEMATION.** CAS/FPSAF требуют своей версии KAnimationCore. Это касается и оружейных Editor-инструментов
  (`Assets/Editor/VR_Battlegrounds/Gameplay/Kinemation*`). Владелец — weapon-system.
- **Позы пальцев левой руки.** IK ставит кисть, но не пальцы. Нужна поза хвата: из клипа, из `UxrHandPoseAsset` или из
  позы KINEMATION. Зона avatar-grip.

## 8. Что проверить прототипом (BotCombatStand, одно тело, винтовка и пистолет)

1. Final IK: `AimIK` (3 кости, pole) + `LimbIK` в ручном порядке после `Animator.Update(0)`. Метрики: угол луча дула к цели
   (< 1° стоя, < 2° в ходьбе/стрейфе), расстояние левой кисти до точки хвата (< 1 см), крен оружия (< 3°), без дрожания кадра.
2. Верхняя поза `Rifle_IdleAiming` по маске поверх 8-направленной ходьбы: исчез ли завал оружия, нет ли поломок плеча
   на стрейфе и при ходьбе назад.
3. PRAS: короткая очередь — возвращается ли ствол на цель за ≤ 150 мс, не теряет ли левая рука цевьё.
4. Сеть: на клиенте реплицируются только цель и вес. Сравнить угол дула сервер/клиент (модель A) и визуальный разрыв
   ствол/трасса (модель B).
5. Quest 2: профилировать 1/8/16 ботов, время `LateUpdate` конвейера, сравнить Final IK и Animation Rigging на одной сцене.
6. Порядок: сокет оружия как шаг конвейера и отсутствие двойной записи кисти разными компонентами.

## 9. Источники

Локальные (этот checkout):
- `Assets/ThirdParty/RootMotion/FinalIK/FinalIK ReadMe.rtf`, `FinalIK Change Log.pdf`, `_DEMOS/Aim IK/Scripts/SecondHandOnGun.cs`, `Tools/Recoil.cs`
- `Assets/ThirdParty/KINEMATION/Shared/KAnimationCore/kinemation-package.json`, `ProceduralRecoilAnimationSystem/Runtime/RecoilAnimation.cs`,
  `TacticalShooterPack/Scripts/{Player/TacticalShooterPlayer.cs,Animation/TacticalShooterPlayerJob.cs}`
- `Assets/ThirdParty/Blaze AI/Docs.pdf`, `Readme.md`
- `Assets/Scripts/Bots/{BotClipBody.cs,BotAim.cs,BotGunner.cs,BotCombatAssets.cs}`, `Packages/manifest.json`

Веб:
- Final IK: [Asset Store](https://assetstore.unity.com/packages/tools/animation/final-ik-14290), [AimIK docs](http://www.root-motion.com/finalikdox/html/page1.html), [форум](https://discussions.unity.com/t/final-ik-full-body-ik-aim-look-at-fabrik-ccd-ik-1-0-released/525769?page=88)
- KINEMATION: [TSP docs](https://kinemation.gitbook.io/tactical-shooter-pack-unity/), [FPSAF Asset Store](https://assetstore.unity.com/packages/tools/animation/fps-animation-framework-238641), [FPSAF docs (Legacy)](https://kinemation.gitbook.io/fps-animation-framework/llms.txt), [FPSAF itch.io](https://kinemation.itch.io/fps-animation-framework), [CAS Asset Store](https://assetstore.unity.com/packages/tools/animation/character-animation-system-320309), [CAS docs](https://kinemation.gitbook.io/character-animation-system-docs/llms.txt), [FPS Addon](https://marketplace.unity.com/packages/tools/animation/fps-addon-for-cas-321490)
- Animation Rigging: [Unity 6.4 Manual](https://docs.unity3d.com/6000.4/Documentation/Manual/com.unity.animation.rigging.html), [CHANGELOG](https://docs.unity3d.com/Packages/com.unity.animation.rigging@1.3/changelog/CHANGELOG.html)
- Unity humanoid IK: [Manual](https://docs.unity3d.com/6000.4/Documentation/Manual/InverseKinematics.html)
- Animancer: [Asset Store](https://assetstore.unity.com/packages/tools/animation/animancer-pro-293522), [IK](https://kybernetik.com.au/animancer/docs/manual/ik), [changes](https://kybernetik.com.au/animancer/docs/changes)
- Opsive: [3.3.6](https://opsive.com/news/ultimate-character-controller-3-3-6-released/), [AI](https://opsive.com/support/documentation/ultimate-character-controller/character/artificial-intelligence/), [IK](https://opsive.com/support/documentation/ultimate-character-controller/character/inverse-kinematics/), [Mirror add-on (deprecated)](https://assetstore.unity.com/packages/add-ons/ultimate-mirror-networking-controller-250532)
- Invector: [Asset Store](https://assetstore.unity.com/packages/tools/game-toolkits/invector-third-person-controller-shooter-template-84583), [Easy Multiplayer – Invector](https://cyberbulletgames.gumroad.com/l/ICNXT)
- Emerald AI: [Asset Store](https://marketplace.unity.com/packages/tools/behavior-ai/emerald-ai-2024-268519), [Hand IK](https://github.com/Black-Horizon-Studios/Emerald-AI/wiki/Setting-up-Ranged-Weapon-Hand-IK), [Built-in IK](https://github.com/Black-Horizon-Studios/Emerald-AI/wiki/Using-the-Built-in-IK-System)
- Blaze AI: [Asset Store](https://assetstore.unity.com/packages/tools/behavior-ai/blaze-ai-engine-194525), [reviews](https://assetstore.unity.com/packages/tools/behavior-ai/blaze-ai-engine-194525/reviews)
- Прочее: [Game Creator](https://docs.gamecreator.io/gamecreator/), [GC2 network shooter](https://arawn-software-publishing.gitbook.io/networking-layer-for-gc2/network-modules/shooter), [Malbers AC](https://fab.com/listings/b9d614cd-1918-45b7-a194-7bd643ad0937), [Alek AI IK](https://assetstore-fallback.unity.com/packages/tools/behavior-ai/alek-ai-ik-265517), [Tactical AI notice](https://assetstore.unity.com/publishers/4375)
