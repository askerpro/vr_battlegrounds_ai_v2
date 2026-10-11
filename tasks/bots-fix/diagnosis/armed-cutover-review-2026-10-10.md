# Независимое ревью armed DEVELOPMENT

Дата: 2026-10-10. Этап: armed-development, epoch 5 / revision 15 по заданию principal.
Ревью read-only; изменён только этот документ. Unity, Git mutations, инфраструктура и тесты не запускались.

## Существенные замечания

### P1: разрешающий LOS не соответствует направлению настоящей пули

`Assets/Scripts/Bots/BotGunner.cs:337-340` задаёт корню центр цели; `BotBody.cs:108-114` удаляет вертикальную составляющую и ограничивает yaw 240°/с. Коррекция оружейной/костной позы теперь отсутствует (`BotGunner.cs:416-429`). `ExecuteShot` проверяет видимость лучом из muzzle в центр цели (`BotGunner.cs:372`, `439-452`), но вызывает штатный `TryToShootRound(0)` без поправки направления (`374`). SDK использует именно `ShotSource.rotation` (`Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs:269-274`; `UxrProjectileSource.cs:99-103`).

Следствие: при ещё не завершённом развороте или цели на иной высоте LOS разрешает выстрел, который идёт вдоль текущего клипа в другую точку. `_aimOffset` сохраняется и меняется (`BotGunner.cs:92`, `379`), но никогда не читается: описанный разброс по телу больше не действует. Это не блокер отсутствующего resolver/host и не замечание по отложенной геометрии хвата; это изменение боевого наведения текущего кандидата.

Минимальная рекомендация: согласовать явную политику направления. При сохранении строго clip-only позы проверять реальный muzzle ray/cone до спуска и описать ограничение вертикального наведения; полноценную поправку вводить только в согласованный штатный оружейный тракт, сохранив единственного Animator writer и Binding pose writer. Не возвращать второй writer кисти/оружия. Приёмка должна включать цель сбоку во время поворота, выше/ниже muzzle, попадание/урон и ShotSequence/ledger.

### P1 для текущего asset-кандидата: безоружное перемещение закрыто до обязательного rebuild

Текущий `Assets/Resources/Bots/BotUnarmed.controller:12-16,61` содержит только Idle. `Assets/Resources/Bots/BotCombatAssets.asset:43` начинает массив motions, в котором присутствуют только 16 Pistol/Rifle записей и отсутствует Unarmed Move. Новый gate `BotCombatDriver.cs:45-47` требует одновременно состояние Move и положительную калиброванную скорость; `BotNavigator.cs:46-49` запрещает навигацию без этого gate. При потере/изъятии оружия `BotCombatDriver.cs:91-96` отключает combat proxy и останавливает навигатор. Бот вне зоны базы не сможет вернуться домой без оружия на нынешних сериализованных ассетах.

В исходниках путь восстановления уже есть: `BotCombatSetup.cs:48-57` добавляет Unarmed Move, а `BotClipComposition.cs:307-330` калибрует его и сохраняет motions. Поэтому это конкретная обязательная зависимость rebuild, а не утверждение, что generator сломан. До preview требуется выполнить обе сборки, проверить сохранённое Move, 18 motion records (2 модели × (4+4+1)), и движение без оружия после strip/drop. Существующие тесты не менять до пользовательской приёмки.

## Подтверждённые архитектурные свойства

- BotBody переносит navigation proxy на root; Runtime готовит root в Update, Binding следует socket в LateUpdate(10000), Runtime проверяет LOS/исполняет intent в LateUpdate(11000).
- Intent ограничен кадром, instance тела, Binding epoch, экземпляром оружия и текущей целью. Фаза дополнительно проверяется по живому stage/WeaponsEnabled.
- Binding claim сохраняется при временно отсутствующем owner; Root recoil policy остаётся запретом при активном claim. Teardown/strip/drop идут через TryDrop; LooseItems защищает активный claim.
- Snapshot анимации включает body/session/sequence/category, current/next state и phase; remote допускает только текущее живое AI-тело с соответствующей сессией. Отдельный root NetworkTransform сохранён; дочерние transports и VR writers отключены.
- Unsupported skin не подменяется молча: factory отказывает без variant. Heavy исключён. Авторинг отключает grabbers/VR/IK до runtime.

Это результаты чтения кода, а не доказательство исполнения или сетевой корректности.

## Фактический scope и пределы

Прочитаны Readme и armed-development-composition; полностью/по соответствующим веткам BotBody, BotCombatDriver, BotNavigator, BotRuntimeController, BotGunner, BotClipBody, BotAvatarSpawnRegistry, WeaponEquipmentBinding, BotEquipmentLifecycle, BotCombatAssets; scoped ветки AvatarManager, AvatarTeardown, EquipmentStrip, PlayerLoadoutManager, LooseItems, BotDirector; BotCombatSetup и авторинг/калибровка BotClipComposition; текущие serialized BotCombatAssets/Unarmed controller; штатный SDK shot direction.

Новых критических дефектов claim/identity/stale-intent по прочитанным веткам не выявлено. Отсутствие полного weapon-system resolver/readiness host — уже известная внешняя зависимость, не новое замечание. SDK70 `c01d8ea2999f2ccd2da72043222bf44dcf1dacf2` рассматривается только как отдельный точный candidate; его наличие/применение в live SDK здесь не подтверждено.

Не проводились compilation, automated checks, Unity runtime, ServerOnly+2 clients, late join/reconnect, Quest или пользовательская приёмка. В частности snapshots/blend, актуальность bones/socket/muzzle на отдельном клиенте, реальные попадания и порядок teardown требуют живой проверки. Итог ревью не означает acceptance или готовность accepted commit.
