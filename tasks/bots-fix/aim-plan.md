# Наведение ствола NPC: корпус + aim-слой

2026-10-11. Решение пользователя: yaw — корпус (BotBody), pitch — aim-слой видимого Animator.
Основание — замер B2 ([разбор](diagnosis/rifle-occluded-2026-10-11.md)): Idle/Shoot ≈+5° вверх, +2° вправо.
Проект — read-only план субагента, проверен principal по инвариантам; код ещё не писался.

## Сейчас

- `BotBody.PrepareFrame` копирует позу Blaze-proxy; комментарий `BotBody.cs:89-90` запрещает отдельный yaw.
- Blaze наводит корень на `enemy.transform.position` (`CoverShooterBehaviour.cs:852,1496,1854,1861`),
  Slerp в `BlazeAI.RotateTo` (`BlazeAI.cs:1198-1206`).
- Порядок сервера: `BotRuntimeController.Update(11000)` → Animator → `WeaponEquipmentBinding.LateUpdate(10000)`
  → gate `Sees` в `BotRuntimeController.LateUpdate(11000)`.
- Анимация реплицируется `[SyncVar] AnimationSnapshot` (`BotClipBody.cs:31,152-194`); параметры Animator пишет
  только `BotClipBody`. Контроллеры — один Base Layer без масок; IK pass запрещён валидатором.

## Дизайн

- **Yaw (BotBody, единственный автор корня).** `yaw = proxyYaw + δ`, proxy и Blaze не трогаются.
  `ψ = β − φ − asin(clamp(c/d))`: β — азимут на `AimPoint` цели, c/φ — боковое плечо и курс дула в системе
  корня из калибровки (среднее по клипу для модель×оружие×состояние). Медленная подстройка по ошибке
  реального луча τ≈0.5 с, ±3°. |δ| ≤ 15°, ≤ 90°/с; δ только когда Blaze уже смотрит на цель (<35°).
- **Pitch (aim-слой Animator, без IK).** Additive-слой, маска spine/chest, 1D BlendTree по `AimPitch`
  (−30/0/+30) из сгенерированных humanoid-клипов `BotAim_Ref/Up/Down` (обязательна additive reference pose).
  Руки — дети chest: хват из клипа сохраняется, оружие следует через binding.
- **Калибровка.** `BuildCalibration` добавляет `aims[]` в `BotCombatAssets`: basePitch/baseYaw/c/φ/усиление g
  по модель×оружие×состояние. Idle-калибровка socket остаётся нейтральной базой.
- **Владелец и сеть.** Чистый `BotAim` (сервер) в `BotRuntimeController`; δ → `BotBody`, pitch →
  `BotClipBody.SetAimPitch`. `Animator.SetFloat(AimPitch)` пишет только `BotClipBody`; клиенты — отдельный
  `[SyncVar] short` (квант 0.25°) со сглаживанием. Yaw — штатно через root NetworkTransform.
- **Контур.** Прямая связь по геометрии + медленный интеграл (±5°, τ≈0.5 с): не гоняется за отдачей Shoot.
  Pitch ±25°, ≤120°/с; без цели — к 0. Gate `Sees` не меняется.
- Отвергнуто: `SetBoneLocalRotation`/`OnAnimatorIK`/UxrIK (IK pass), запись spine/кисти в LateUpdate
  (второй писатель), Animation Rigging (отдельный путь графа — только по решению пользователя).

## Этапы и приёмка на стенде

0. `BotAimMath` — общая метрика ошибки дула для стенда и gate; совпадение с B2.
1. Калибровка `aims[]`: два прогона побайтно равны; g линейно (<10%); кости/socket не меняются.
2. Yaw: медиана |yaw| в Idle/Shoot ≤1.5°; скольжение не хуже B.
3. Aim-слой: 0 IK pass; повторная сборка стабильна; при AimPitch=0 дельта <0.01°.
4. Pitch: медиана |pitch|,|yaw| в Shoot ≤1.5°; isTargetShare в Shoot ≥0.8; AK105 стреляет у MEF и Cyborg.
5. Сеть: стенд ServerOnly + 2 клиента; ревью network-expert (новый SyncVar, late join, хост).
6. Визуальное ревью avatar-grip, затем приёмка пользователем; тесты только после неё.

Риски: humanoid additive без reference pose даёт ноль; у Cyborg может не быть UpperChest; разное усиление
мышц (калибровка g); наклон spine сдвигает хитбоксы бота; отставание Slerp Blaze; большой δ при strafe.

## Решения пользователя (2026-10-11)

- δ разрешён: снимается запрет `BotBody.cs:89-90`; δ ≤ 15° и только когда Blaze уже смотрит на цель
  (Idle/Shoot/strafe), иначе плавно в 0.
- Aim-слой — для винтовки и пистолета.
- Этапы 0–4 подряд с проверкой на стенде; при PASS продолжать, при FAIL или новой развилке — к пользователю.
  Unity-раунды: (I) этапы 0–2 — authoring калибровки + замер yaw; (II) этапы 3–4 — слой + pitch.

## Решение пользователя 2026-10-11 (вечер): опорная рука — один IK-проход

Узкое изменение инварианта: «кости пишет только видимый Animator» → «Animator, затем один post-pass опорной руки».
Готовый LimbIK (Final IK, без правок пакета) на левую руку бота (плечо–локоть–кисть), цель — точка support-хвата
оружия (WeaponGripBinding / GripAlignTransformHandLeft). Владелец — один компонент бота, ручной Update (авто-обновление
Final IK выключено) после проекции оружия binding (LateUpdate 10000) и до gate (11000); считается локально на каждом
процессе из синхронизированных корня/Animator/оружия, без новых SyncVar. Корень (BotBody), оружие (binding), gate,
Blaze и SDK не меняются. Требуется согласование порядка писателей кисти с avatar-ik/avatar-grip и замер на Quest.
Крен корпуса и завал оружия — пока как есть (исследование `tasks/bots-fix/research/`).
ACK владельцев: avatar-grip 4046 — пальцы только Animator; цель — UxrGrabbableObject grab point 1,
`GetGripPoseInfo(avatar).GripAlignTransformHandLeft` (проверить реальную запись, Issue 26), читать transform, не копию.
avatar-ik 4051 — LimbIK только на теле бота (рантайм-Bind или бот-only ClipBody), не в общих префабах игрока;
только плечо–локоть–кисть; при включённом UXR-писателе на теле бота порядок согласовать заново; проход будет вписан
в контракт hand-writer как «вне UxrManager: BotClipBody LimbIK опорной руки (только NPC)».
avatar-grip 4091: Cyborg — IK опорной руки пропускается («без цели») до записи хвата на базе PlayerBase_SdkHands
(их grip-pose-coverage, решение пользователя); align берётся как есть (mef-correction исправит автоматически).
Socket правой руки по `GetGripPoseInfo("b6fe…")` (MEF_Base_Avatar) — известная копия; перевод на цепочку аватара
вместе с записью Cyborg, иначе Cyborg теряет оружие.
Final IK в сборке ботов: RootMotion.asmdef + Editor/RootMotion.Editor.asmdef (только файлы сборок), ссылка из
VrBattlegrounds.asmdef; avatar-ik zone-ack 4117 при условиях 4098 (без правок кода пакета, демо не трогать, Editor-only
сборка, проверки: полная компиляция, Android, нет Missing Script в Optimized_MEF_Player_VRIK.prefab, дифф до merge).
Патч вендора FinalIK #1 (key finalik-asmdef-support-hand). Решение пользователя: Cyborg убран из калибровки и ботов.
Исследование готовых решений — `research/npc-shooter-animation-libs-2026-10-11.md` (рекомендация: Final IK AimIK+LimbIK+PRAS).

## 2026-10-12: смена архитектуры (решение пользователя)

Боты — штатный поток обычного игрока + адаптер намерений ИИ (позы головы/контроллеров, ввод). Модель наведения
этого документа и этап pose-fit не развиваются; клиповый путь (armed-development, принят) — запасной до приёмки
нового. Проект перехода — в работе.
