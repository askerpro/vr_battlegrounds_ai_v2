# Clip grip: диагностика 2026-10-09

Граница: исходники/assets прочитаны без Unity и изменений продукта. Root просмотрел actual preview347: support обеих rifle открыт ниже цевья, muzzle вверх; pistol пальцы охватывают receiver неправильно. Socket345/rest correction не являются принятым grip.

## Факты

- `Assets/Editor/VR_Battlegrounds/Bots/BotCombatSetup.cs:28-44`: pistol Idle — Blaze `Gun Aim.fbx`, rifle Idle — `MixamoRifle/Rifle_IdleAiming.fbx`; `:47-48` только проверяет альтернативный `MixamoPistol/Pistol_PistolIdle.fbx`, но не использует его в controller. Классификация humanoid и clip.length в `Clip():79-86` не проверяет качество grip/пальцев.
- Rifle importer `.fbx.meta:55-60`: bodyMask все enabled, transformMask пустой. HumanDescription содержит30mapped finger joints; animationType3/avatarSetup2, copied Avatar source GUID `4e38438e7c8befa4fbe8eb4b374645bc` (`:874-878`). Совпадение reference bone convention с импортируемым FBX надо проверять в Unity; само наличие HumanDescription не доказывает правильных finger muscles.
- Blaze Gun Aim `.meta:746-747`: импортируется `standing_with_aim`, take `Pistol_Idle`. HumanDescription содержит30finger joints, bodyMask все enabled, maskType0, animationType3/avatarSetup1. Это реальный pistol take, а не вывод из имени файла.
- BotPistol/BotRifle controller `:137` — `m_Mask:{fileID:0}`, `:143` — IKPass0. Finger mask exclusion на уровне этих controllers не найдено.
- Scoped binary FBX read прошёл Objects/Connections/AnimationStack→Layer→CurveNode→Model, отдельно точный take. Rifle `mixamo.com`:30mapped non-end finger bones,90 rotation channels,90 constant. Примеры left Index2 local X28.43°, Index3X28.52°. Следовательно raw clip содержит статическую finger pose, не лишён finger channels. Постоянная pose совместима с idle и не является ошибкой сама по себе.
- Blaze `Pistol_Idle`:40finger bones включая end bones,120 rotation channels, все constant в этом take. Пример left Index2Z−45.46°, Index3Z−52.93°. Нельзя путать этот take со всеми остальными animation stacks того23.7MB FBX.
- Альтернативный MixamoPistol `Pistol_PistolIdle.fbx`, take `mixamo.com`:30finger bones/90constant rotation channels; left Index2X61.59°, Index3X23.89°. Это доступный кандидат с иной статической hand pose; его визуальная совместимость с PPK и обеими моделями пока не подтверждена.

## Вывод и неопределённость

Высокая уверенность: дефект нельзя объяснять отсутствием finger data в исходном FBX или явным finger mask этих controllers. Данные есть. Окончательно исключить потерю curves при Unity import/ретаргетинге можно только живым измерением imported clip muscles.

Вероятный architectural gap: clip задаёт generic pistol/rifle body+finger pose, а cached primary socket переносит independent SDK-authored reference weapon frame; никакая проверка не согласует clip palm orientation, finger closure, support geometry и muzzle. Даже полностью корректный Humanoid перенос такой generic pose не гарантирует хват конкретных PPK/AK105. Одинаковое качество ошибки на MEF и Cyborg после исправления rest поддерживает эту гипотезу, но не доказывает её вместо import/retarget defect.

## Минимальный следующий read-only Unity probe

Outside Play, PreviewScene, только source clip model и generated MEF/Cyborg; transient objects и HumanPoseHandler.Dispose в finally; ничего не импортировать/сохранять в Assets.

1. Для exact imported clips GunAim/Pistol_Idle, Rifle_IdleAiming, Pistol_PistolIdle: GUID/localID/name, source Avatar reference, importer HumanDescription mapping, AnimationUtility.GetCurveBindings. Вывести все finger muscle properties/число/keyframes/min/max, включая постоянные ненулевые curves. `hasMotionCurves` и количество изменяющихся ключей не являются доказательством отсутствия статической hand pose.
2. На source model и каждом NPC снять neutral snapshot до sampling. Через HumanPoseHandler получить именованные HumanTrait.MuscleName значения finger stretch/spread; сохранить wrist-local rotations всех30fingerbones, pose references и render-bone identity.
3. Изолированно sample each imported clip на t0,.25,.5,.75 (секунды=normalizedPhase×length), повторно снять muscles и actual finger rotations. Прямой source-model clip pose отличит authored data от ретаргета; imported-muscle curves отличат FBX animation от потери при импорте. Нейтральный ноль muscle не равен нулевому local Euler, поэтому сравнивать обе формы.
4. При cached socket отдельно снять primary palm-frame orientation, muzzle.forward относительно root; clip left/right relative wrist transform; finger tip/segment world positions и дистанции до grip/foreend geometry. SDK grabber/collider-centers не использовать как anatomical target. Показывать source-model и NPC hand images с тем же реальным оружием.
5. Интерпретация: FBX keys есть, imported finger bindings/muscles отсутствуют → importer/copy-avatar проблема. Source-model imported pose правильна, target finger muscles/rotations не соответствуют → target Avatar mapping/limits/reference axes. Source и targets воспроизводят одинаковую generic pose, но weapon/support/muzzle не совпадают → нужен full clip+weapon authoring, не очередной socket offset.

## Доступный путь без runtime IK

Сначала сравнить существующий MixamoPistol PistolIdle с Blaze pistol и Rifle Idle/IdleAiming/IdleCrouchingAiming; названия не считать доказательством закрытого хвата. Если ни один source clip не подходит, authoring должен запечь model+weapon upperbody/fingers в клипы, согласованные с primary, support и muzzle. Один Animator остаётся владельцем всех видимых bones; equipment следует постоянному socket. Можно сохранить выбранные locomotion legs/root, но ни neutral finger repair, ни wrist socket самостоятельно не устраняют rifle support constraint. Финальный вариант требует actual images и пользовательской Unity/шлем-приёмки.
