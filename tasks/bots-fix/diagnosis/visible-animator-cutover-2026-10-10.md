# Bounded visible Animator cutover

2026-10-10. Делегированная реализация; записи только BotBody.cs, BotCombatDriver.cs,
BotNavigator.cs. Допуск armed-development обновлён principal до epoch3/revision14.

- BotBody перестал подписываться на SDK stages и писать head/wrist transforms. Feet/Yaw —
  реальные pose корня. Place/WalkTo/PrepareFrame — единая authority-guarded граница root.
  Blaze navigation source задаёт root position/rotation; явная цель LookAt меняет только
  yaw того же корня. PlanarVelocity снимается с фактического root displacement.
- Legacy SetWeaponHands/SetRightHandPose/SetCombatPoseSource/ClearRightHandPose сохранены
  как совместимые no-op; не являются допустимым способом authoring grip correction.
  TryGetWeaponPose читает настоящие видимые кисти из BotClipBody.Animator.
- Driver.EnsureClipBinding() — единственный idempotent binder перед equipment. Он требует
  живую canonical bot session и уже созданный NPC variant с BotClipBody.
- Driver создаёт только лёгкий GameObject navigation proxy. Ни cloned skeleton, ни proxy
  Animator/Avatar/renderer нет. Реальный SDK API — BlazeAI.BindAnimatorSource(Animator);
  SetExternalAnimation в используемом SDK отсутствует. Поведение/target/cover/shootEvent
  сохраняются, события стрельбы по-прежнему принимает runtime/Gunner.
- Driver.SyncVisualPose вызывается в Update: state выбирает Blaze либо скорость/направление
  домашнего движения, rate задаёт BotClipBody.TrySetMotionSpeed(actual speed/native speed).
  Missing motion failclosed останавливает движение. Unarmed Move поддержан forward-only;
  до появления сгенерированной unarmed native-speed записи Navigator неподвижен.
- Navigator не планирует/не шагает одновременно с активным Blaze/navigation source;
  stale домашний маршрут очищается при передаче движения Blaze. Прежние публичные APIs
  сохраняются. Добавлены Driver.CanMoveWithCurrentPose и Body.Animator/PlanarVelocity/
  HasNavigationSource/SetNavigationSource.

## Контракт интеграции principal

Update: EnsureClipBinding → equipment/category → Driver.Tick → Navigator.Tick только
если !CombatActive → Body.PrepareFrame → Driver.SyncVisualPose → обычная оценка Animator.
Body.PrepareFrame/скорость нужно снимать после последнего root step этого Update, до
Animator evaluation; повторное измерение в одном frame не добавляет второй turn/sample.
Attachment и свежий shot остаются в существующем binding/runtime LateUpdate после Animator.
RootGunner/Runtime/factory/SDK/weapon здесь не изменялись. Клиентский pose snapshot,
variant registration и root transport — последующий integration audit principal.

## Проверка

git diff --check назначенных трёх файлов PASS; выдан только существующий EOL warning
для BotBody LF→CRLF при следующей записи Git. Source scan подтвердил отсутствие
Instantiate(rig), AddComponent<Animator>, SDK stage callback и активных hand/head writes.
Локальную совместную компиляцию выполняет principal после параллельных изменений;
здесь csc/Unity/runtime не запускались, no-slide/стрельба/remote не заявлены GREEN.
Нет новых gameplay NUnit и коммитов.

Нужно измерить фазу/темп движений и crossfade при фактическом nav acceleration/turn,
проверить тело/camera/hitboxes и единственный Animator на обеих моделях. Предыдущие
данные authoring/fixture не доказывают новый atomic runtime cutover.
