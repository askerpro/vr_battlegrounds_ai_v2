# Cyborg wrist rest: диагностика 2026-10-09

Статус: read-only исследование; Unity не запускалась. Calibration339 завершилась исключением `Изменён NPC wrist rest: PlayerControllersCyborgAvatar`, `BotClipComposition.cs:271`. Числа `restMm/restAngle` перед исключением не сохранены. Точная причина различия live transforms пока не доказана.

## Что доказано исходниками

- `Build():62` снимает исходную render rest. `:71,:85` нормализуют невзвешенные Animator/Hips pivots; `SetPivotPose():455-461` сохраняет мировые позиции и rotations непосредственных детей.
- `:87-88` сохраняет local transforms после нормализации. `:90` применяет исходный Avatar и unarmed; `:125-136` создаёт исправленную T-pose/generated Avatar и вызывает Rebind/Update. `:152` восстанавливает сохранённые transforms, затем `ValidateRenderRest():425-430` проверяет все weighted world matrices, `:435-451` проверяет mesh vertices. Только затем сохраняется prefab (`:169`).
- Отчёт `tasks/bots-fix/reports/equipment-303/composition.json` подтвердил Cyborg: normalizedFrame/normalizedHips=true, originHeight=1.54904234, T-pose error456.886078→0, maxRestVertexDeltaMm=0.000644722837. Это доказательство сохранности transient render rest того запуска, не доказательство fresh-instantiation/rebind rest.
- `BuildCalibration():262-271` сравнивает source и NPC, заново созданные отдельно, через `UxrAvatar.GetHandBone()`. Он не выводит paths, identity/матрицы и не разделяет serialized prefab rest от результата Animator initialization.
- `:273-274` строит socket через исходный anatomical frame. Равенство root-relative world wrist poses является дополнительным предположением. Постоянный wrist-local socket зависит от wrist-local anatomy axes и scale; оно не требует равенства поз кисти в двух разных animation/reference states.
- `AnatomicalHand():318` читает сериализованный SDK `HandUniversalLocalAxes`. SDK `UxrAvatar.OnValidate():1448-1452` заново вычисляет rig info, а `UxrAvatarArmInfo.SolveHandAndFingerAxes():220-235` выводит axes из текущих wrist/finger transforms. Поэтому качество/stability SDK axes после generated T-pose и RestoreRest надо проверить отдельно; SDK serialized version не доказывает neutral-rest calibration.

## Проверяемые гипотезы

Основная: generated NPC при свежем InstantiatePrefab/Animator initialization имеет другую live reference/clip pose, хотя сохранённый weighted rest был восстановлен. Источник и NPC используют разные Avatar/controller configuration. Rebind/Update должны быть отдельно измерены, а не заранее назначены причиной.

Альтернативы: source SDK wrist и NPC SDK wrist ссылаются на разные render bones; сохранённый variant содержит transform override, отсутствовавший в transient проверке; RigInfo anatomy axes вычислены на другой позе. Один текущий status.json эти варианты не различает.

## Минимальный следующий Unity probe

Условия: отдельная broker lease, outside Play, один NewPreviewScene; никаких SaveAsset/SavePrefab/изменений importer. Только MEF и Cyborg. Исходный source, generated NPC и библиотечные assets читаются через AssetDatabase. Все transient объекты и PreviewScene уничтожить/закрыть в finally. Отчёт записать в task reports; исключение тоже включить в durable report.

1. До Instantiate снять с source/NPC prefab asset: Avatar/controller GUID, enabled Animator, serialized root/wrist local TRS, SDK wrist reference и path, serialized weighted ancestor local TRS. Снять SDK local anatomy axes quaternion и ссылку его `_transform`. Это сериализованные значения; не именовать их live rest.
2. Создать source/NPC в PreviewScene. Немедленно снять stage `instantiated`, затем выключить все transient pose writers и Animator. Снять `disabled` без Rebind/Update. Для каждого stage: root/wrist local/world matrices, path SDK wrist, path Animator.RightHand, reference equality SDK/Animator wrist, weighted membership, scale, SDK anatomical quaternion world и wrist-local.
3. Восстановить сохранённые prefab local TRS только transient weighted skeleton и предков, сохранив собственную normalized NPC hierarchy. Снять `serialized-rest-restored`. Измерить max root-relative weighted-matrix delta и mesh-vertex delta source/NPC; wrist pos/angle delta; одинаковость anatomy bone names/paths. Нормализация невзвешенного pivot сама по себе допустима, поэтому не сравнивать literal parent local TRS вместо render matrices.
4. Для каждого экземпляра отдельно выполнить Rebind без Update, снять `rebound`; затем Update(0), снять `updated-zero`; затем explicit Idle/Update(0), снять `idle-zero`. Не переносить source Avatar на NPC и не менять shared assets. В каждый stage включить Avatar/controller IDs, Animator state/clip/normalizedTime. Это локализует момент первого изменения.
5. Для wrist каждой модели вывести все SkinnedMeshRenderer, где wrist входит в bones: renderer path, sharedMesh GUID/localID, bone index, bindpose matrix по этому индексу, renderer/rootBone path. Сравнить source/NPC mesh/bindpose identity. Само одинаковое имя кисти недостаточно.
6. Вывести metrics для каждого stage, даже если старый guard нарушен: `restMm`, `restAngle`, matrix max delta, weighted/render mismatch, anatomy-local quaternion angle. Никакой calibration asset не сохранять.

## Socket contract после доказательства

Использовать explicit MEF weapon grip entry как reference geometry; Cyborg SDK weapon entry/fallback отсутствует и не нужен. Reference weapon pose переводится в доказанный bone-local anatomical frame generated NPC. Frame получают на единой neutral serialized/render rest, из видимых wrist/finger bones либо проверенных локальных anatomy axes, и фиксируют при authoring. Generated Avatar reference T-pose не должна подменять anatomical neutral measurement. Постоянный socket сохраняется в координатах именно Animator visible wrist; runtime единственным писателем костей остаётся Animator, equipment только следует socket.

Guard271 не удалять вслепую. Сначала выяснить, отличается ли serialized/render rest или лишь animation initialization. В первом случае исправлять authoring/serialization; во втором проверять правильный invariant: identity видимой кисти, bindpose/render сохранность и стабильная bone-local anatomy calibration, а не равенство двух world reference poses. Качество поддержки rifle и пальцев требует отдельных baked clips/приёмки; primary socket не доказывает поддержку.

## Фактический probe344: первое расхождение доказано

`tasks/bots-fix/reports/wrist-probe/result.json`, failures=[]; результаты анализа 2026-10-09:

- Cyborg root GameObject position source/NPC=(0,0,0) на всех стадиях. Serialized source wristRootY=1.161829, NPC=-0.3872133, разница=-1.5490423м уже в `asset-before-instantiation`; она сохраняется на `instantiated`, `disabled`, `serialized-rest-restored`, `rebound`. Следовательно, это сохранённый дефект variant, не fresh Animator Update0.
- Первое различие ancestor local TRS — path `Cyborg` (Animator frame): source localY=1.54904234, NPC=0. Все следующие предки wrist — `CyborgRig/Spine01/Spine02/Clavicle_Right/Arm_Right/Forearm_Right/Hand_Right` — имеют одинаковые local position/rotation/scale, кроме quaternion float noise кисти. В частности `CyborgRig.localY=-0.3635205` у обоих. Нормализация Animator pivot сохранилась, а компенсирующий сдвиг дочернего skeleton в serialized variant отсутствует. Это полностью объясняет мировой сдвиг wrist.
- Wrist paths, render membership/четыре mesh-bindpose записи и wrist-local anatomy axes source/NPC совпадают. Cyborg weapon SDK fallback к этому отказу guard не относится.
- MEF asset/instantiated/disabled/restored/rebound совпадают; NPC wrist меняется только на `updated-zero`, что ожидаемо при проигрывании NPC unarmed controller и является отдельным явлением.

Причина потери child compensation между проверкой transient rest и сохранённым prefab ещё требует локализации. В `Build()` ValidateRenderRest выполняется до окончательного `animator.avatar=asset` и SaveAsPrefabAsset. Гипотеза: окончательное назначение generated Avatar либо prefab serialization переинициализирует local skeleton transforms из reference pose; probe344 не различает эти два момента. Нельзя заявлять конкретный setter доказанной причиной.

Минимальное исправление-кандидат: завершить создание/CopySerialized/назначение окончательного Avatar до последнего RestoreRest; после всех операций, способных переинициализировать skeleton, восстановить сохранённый compensated rest, выключить evaluation перед serialization и снова ValidateRenderRest. Сохранять prefab только после этого; затем fresh-load/instantiate variant без Update и повторить проверку weighted root-relative matrices/mesh geometry против source. Валидация должна включать root TRS и root-relative render bones, а не только transient world matrices; отдельно проверять компенсацию normalized pivot. Если расхождение впервые появляется в Save, нужен fix prefab persistence, а не удаление guard.

Socket для generated NPC вычислять после доказательства правильного persisted neutral render rest. Сохранить постоянный local frame его visible wrist; подтверждённо одинаковые source/NPC local anatomy axes позволяют перенос explicit MEF reference weapon geometry без Cyborg grip fallback и без pose writer. Guard271 пока сохранять: теперь он выявил реальный serialized shift.
