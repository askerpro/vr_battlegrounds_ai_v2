# Клиповая композиция NPC: инженерная проверка

История измерений и ограничений доказательности. Текущий статус: [Readme.md](Readme.md).

План `Docs/plans/2026-10-07-bots-full-clips.md` одобрен независимым агентом,
включая уточнение pre-Awake variants. Это промежуточный отчёт, не приёмка полного перехода.
Heavy исключён. Обычный BotDirector/Gunner ещё используют прежний путь.

## Подтверждённые причины отказов

- `UxrStandardAvatarController.Awake` создаёт BodyIK; `UxrBodyIK.Initialize` выносит общий
  корень bones в Dummy Forward вне видимого Animator. Позднее выключение компонента
  не возвращает структуру. Runtime190: MEF_Rig содержит10 transforms, без bones;
  при этом Avatar содержит30 finger mappings, SDK указывает на реальные пальцы.
- Созданные NPC prefab variants выключают IK flags/controller/tracking/input до Awake.
  Они используют тот же видимый render skeleton; source→variant является производным
  authoring binding, а не новым владельцем выбора скина.
- Cyborg source Avatar не содержит Humanoid fingers. NPC Avatar включает все30 реальных
  пальцев и их skeleton ancestors. Имя клипа и наличие muscle curves сами по себе
  не доказывают соответствие видимой модели.
- Во full-body снимке192 Cyborg поднят над root: исходный VR Avatar назначает Hips
  на floor root CyborgRig. Прямое Hips=Pelvis невалидно: spine и pelvis являются соседями.
  Ticket194 остановился до Play и освобождён; ticket195 проверил normalization общего
  невзвешенного hips pivot без изменения мировых poses children. Mapping2/2 PASS,
  руки без растяжения, однако стопы Cyborg остаются1.61–1.71м выше body root. Срез ещё
  не принят визуально. Следующая правка нормализует также frame Animator к body floor,
  сравнивает weighted matrices/BakeMesh vertices и переносит localBounds в новый rootBone frame.

## Прогон192

Input `e718a623766200c7b332c1bc990cda17666ad64d`, result
`62679142478daf91ac1553723aabdeb6dfaaf958`. Аренда завершена, результат получен.
Отчёт: `tmp/bots-refactor-192/reports/visual.json`, изображения рядом в `images/`.

| Модель | Замеры | Длительность категории | Изменение плеча / предплечья | IK / SDK controller |
|---|---:|---:|---:|---:|
| MEF Black |60|30с Pistol +30с Rifle|0.000775 /0.000566мм|0 /0|
| Cyborg |60|30с Pistol +30с Rifle|0.000745 /0.000834мм|0 /0|

В обеих моделях Blaze использует тот же Animator, что AnimationManager, после re-enable;
Animator на navigation proxy отсутствует. Потеря внешнего source выключает brain/nav.
Все восемь фаз idle/move/strafe/category пройдены. Прогон не содержит оружия: жёсткий
socket, контакт хвата, readiness и projectile events им не подтверждены.

AndroidCompileGate PASS. Существующие Bots +AvatarLoadoutTests +PrefabCompositionTests:
105 завершены,97 прошли,8 отказов. Четыре отказа требуют старый LegsAnimator/мост,
три относятся к MagazinePocket compatible tags AK105/R08, один — invalid header
LightingData.asset TestMap2. Проверка тех же suites на чистой принятой базе ожидается;
Ticket193 проверил ту же группу105 тестов на чистом origin/dev2c845870: все8 имён
и сообщений отказа совпали дословно; новых отказов относительно базы нет. Result
`75a11cbce5f9f941576ec174d36c8a247eb3785f`, аренда завершена и результат получен.
Эти ожидания/ассеты не исправлялись обходом в задаче клиповой композиции. Тесты не переписывались.

## Прогон200: reference pose и сохранённые NPC variants

Input `6cedde901d392b53784bb6f0fb8adf41e7be677f`, result
`f31d9dff42a1e8470105625937f8a8c24a0b7f7c`. Аренда завершена, результат получен.
Отчёты: `tmp/bots-refactor-200/reports/{composition,visual,android}.json`.

После нормализации frame прогон196 устранил подъём Cyborg, но руки остались за корпусом.
Сравнение197 опровергло гипотезу yaw: source и NPC Avatar на одном скелете давали одинаковый
отрицательный wrist→torso forward. Пробный authoring199 установил причину: исходный VR Avatar
не имеет корректной reference T-pose для full-body retarget. Подготовка T-pose штатным editor
AvatarSetupTool устранила ошибку. Это authoring NPC Avatar, не runtime запись bones.

В200 сохраняется Avatar с подготовленной reference pose, а prefab — с восстановленным geometry rest.
До записи assets проверены weighted matrices, BakeMesh и перенесённые bounds. MEF использует
исходный native Avatar; human source prefabs/Avatar не изменялись. Cyborg referencePoseError
456.886→0, mapped fingers30/30, максимальное отличие rest vertices0.000645мм.

| Модель | Замеры | Изменение плеча / предплечья | Wrist forward, минимум | IK / SDK controller |
|---|---:|---:|---:|---:|
| MEF Black |60|0.000625 /0.000686мм|+0.03799м|0 /0|
| Cyborg |60|0.000700 /0.000685мм|+0.06641м|0 /0|

У Cyborg стопы0.0676–0.1698м, голова1.5438–1.7137м над body root; ошибочный подъём1.6м исчез.
Сохранённые variants после нового Instantiate прошли Pistol/Rifle, движение, повороты и strafe.
Blaze/AnimationManager используют один source после re-enable, proxy не содержит Animator;
потеря внешнего source прекращает brain/nav. На просмотренных актуальных кадрах Cyborg руки
перед корпусом: `images/1/frame-11250-front.png`, `frame-13932-front.png`.
AndroidCompileGate PASS. Изображения и замеры не являются приёмкой контакта хвата с оружием:
в этом прогоне оружие отсутствует. После200 добавлена защита authoring от NaN/Infinity,
удалено перезаписываемое skeleton assignment; отдельная локальная компиляция указана в ledger.

## Остаток работы

2026-10-08 контракт equipment явно получен и прочитан weapon-system; он согласовал API и границы.
Binding реализован отдельным срезом без production consumers. SOURCE APPROVED после ревью;
локальная компиляция прошла. Runtime/Mirror serialization/Android этого нового среза пока не проверены:
объявлено обслуживание инфраструктуры, worker не используется. Подготовлены standalone probes;
queued207 — baseline input до SDK61, не зелёный результат. Владельцу передан source API в
`.agent-state/coordination/bots-equipment-binding-to-weapon-system.md`. UseContext/Resolve/readiness
реализует он; следующий factory/Gunner cutover зависит от его проверенного adapter.

Применить factory/registry,
equipment/use context, animation transport и teardown атомарно; удалить старые wrist/IK
путь и SDK grabs у ботов. Новый equipment contract передан настоящему weapon-system owner,
его новый ACK получен2026-10-08; readiness/Weapons.Core/WeaponSystem в этом срезе не изменялись.
SDK60 held resistance/root recoil ещё не реализован; generic equipment availability записан как SDK61.
Remote/Quest/пользовательский хват
не приняты. Обычных коммитов/push нет.
