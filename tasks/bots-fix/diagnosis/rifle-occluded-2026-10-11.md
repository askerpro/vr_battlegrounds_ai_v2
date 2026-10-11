# AK105: Occluded при «видимой» цели (прогон B), 2026-10-11

Граница: read-only разбор исходников и отчётов `reports/armed-runtime-b/runtime/`; Unity/MCP не запускались.

## Причина

Диагностический `muzzleRay` и gate `Sees()` проверяют разные лучи.

- `Assets/Editor/VR_Battlegrounds/Bots/BotStandRecorder.cs:89`: `RaycastAll` из `ShotSource.position`
  в направлении `bounds.center - ShotSource.position` (к центру твёрдых коллайдеров цели), маска
  `CollisionLayerMask`, `QueryTriggerInteraction.Ignore`. Это проверка линии видимости из точки дула.
  Ориентация ствола в ней не участвует.
- `Assets/Scripts/Bots/BotGunner.cs:441-450`: `Raycast` по `ShotSource.forward`, т.е. в реальном направлении
  выстрела SDK. Эта проверка правильная: она соблюдает инвариант «выстрел только по реальному лучу дула».

Поэтому `muzzleRay` первым видит `Hitbox_Torso_Lower` в 36–38 из 38 кадров **во всех** сочетаниях, включая
PPK-кадры с `muzzleSeesTarget=false`. Это поле не различает «цель закрыта» и «ствол смотрит мимо».

`Occluded` для AK105 означает, что ствол смотрит мимо цели. Socket-ориентации в
`Assets/Resources/Bots/BotCombatAssets.asset:30-42` побайтно совпадают с калибровкой 2026-10-09
(`reports/calibration-rebuild/calibration.json:36`, `reports/clip-calibration-preview/manifest.json`).
Из той же калибровки взят `muzzle.forward` в Idle:

| | pitch вверх | yaw |
|---|---|---|
| MEF PPK | 6.21° | −9.14° |
| Cyborg PPK | 16.57° | −1.22° |
| MEF AK105 | 23.58° | −17.58° |
| Cyborg AK105 | 36.25° | −13.59° |

На типичной дистанции 6.6–9 м допуск по корпусу цели примерно ±2° по горизонтали и +4° вверх.
У AK105 луч уходит выше цели на 2.9–5 м и в сторону на 2–2.8 м. У PPK отклонение меньше, а клипы
`*_with_aim` раскачивают ствол, поэтому луч периодически пересекает цель. Отсюда частичный проход PPK:
`sees` 5/10/6/14 кадров из 38, Fired 8/11/11/14 при Occluded 28–38 в каждом кейсе. AK105: `sees` 0/0/0/2.
Пять выстрелов MEF T07 пришлись на `Rifle_IdleAiming` nt≈0.11, кадры 4338–4360.

Коррекция дула в Idle есть в исходнике input (`BotClipComposition.cs:284-302`, поворот вокруг wrist
до `npc.forward`), и B1 в worker выполнил `BuildCalibration`, который записывает её в `library.sockets` (:333).
Поправка principal (2026-10-11): прежний вывод «в asset прогона её нет» по mtime файлов worktree неверен —
authoring шёл в worker по коду input; R413 не изменил BotCombatAssets.asset, т.е. sockets input уже содержали
коррекцию. Углы 23–36° выше взяты из превью 2026-10-09 до коррекции и для прогона B **не измерены**.
Подтверждено только: forward-луч дула не видит цель (Occluded), луч к центру цели — видит. Фактические углы
дула в Shoot/Rifle_IdleAiming/Walk* runtime неизвестны — нужен замер forward-луча в recorder.

## Проверенные гипотезы

- (a) Trigger-хитбоксы: опровергнуто. Тот же `QueryTriggerInteraction.Ignore` в recorder попадает в `Hitbox_*`.
- (b) Маска: опровергнуто. `m_Bits: 4105` у обоих (`AK105.prefab:1041-1043`, `PPK.prefab:2106-2108`); recorder использует ту же маску.
- (c) Старт внутри своего коллайдера: не подтверждено. Луч к цели из той же точки не задевает своё тело.
  Исключение — `Cover_Hard` 0.07–0.68 м у укрытия. Что бьёт forward-луч, не записано.
- (d) `GetComponentInParent<UxrActor>`: опровергнуто. Actor тот же «Контрольная цель (Remote)», и с PPK проверка проходит.
- (e) Наклон и поворот дула: наиболее вероятная причина, но величина в runtime не измерена (см. поправку выше).

## Зоны и минимальное исправление

1. Avatar-grip/bots (socket-калибровка): перегенерировать sockets через существующую калибровку с muzzle correction.
   Ассет — владение bots, качество хвата — avatar-grip. После этого снять yaw/pitch muzzle в Idle,
   `Rifle_IdleAiming`, Walk* и Shoot. Полное решение для наклонов вне Idle — aim-слой в Animator
   (единственный writer костей), а не правка оружия.
2. Bots/стенд (`BotStandRecorder.cs:89`): записывать в кадр также forward-луч, как в `Sees()`
   (первое попадание и actor), и углы `ShotSource.forward` к направлению на `bounds.center` (pitch/yaw).
   Текущий `muzzleRay` переименовать в line-of-sight.
3. Weapon-system: не требуется — маска, ShotSource и источник выстрела одинаковы.

`Sees()` не ослаблять: замена на луч к цели разрешила бы выстрелы мимо ствола.

## Неизвестно

- Реальные pitch/yaw дула в runtime-клипах прогона B: углы в отчёте есть только для превью Idle.
- Во что упирается forward-луч AK105 (небо, стена или своё тело).
- Хватит ли Idle-коррекции для `Rifle_WalkForward`/`WalkLeft`.
- Боковой сдвиг ствола (~0.11 м от оси root) относительно ширины хитбокса.

## Замер B2 (ticket415, 2026-10-11)

Input `fd193835` (B + единый луч `BotGunner.TryGetMuzzleForwardHit`, запись forward-луча и углов в стенде,
исправленный отброс событий наблюдателя). Finish DONE, result `a5ab2487` accepted=true, unexpected 0;
идентичность ClipBody до/после Play — PASS. Отчёты `reports/armed-runtime-b2/` (b-verdict.json).

Таймлайн восстановлен: выстрелы подтверждены SDK-событиями (source-emitted = ledger Shot Emitted =
ShotSequence = расход патронов): mef-ak105 6, mef-ppk 12 (T07), cyborg-ak105 0. Gate и замер совпадают 100%.

Углы `ShotSource.forward` относительно направления на центр цели (медиана, + вверх / вправо):
- MEF AK105: Idle +4.3/+2.1, Shoot +4.5/+1.9, Move +7.1, Left +11.8.
- Cyborg AK105: Idle +5.2/+1.7, Shoot +5.4/+1.7, Move +9.9, Left +13.3, Right +11.1.
- MEF PPK: Idle +4.1/+1.8, Shoot раскачка +4…+29 (клип с отдачей), поэтому периодически проходит.
Forward-луч винтовки упирается в стены/стенды за целью; попадания MEF — в `Hitbox_UpperArm_L`.

Вывод: калибровка Idle выравнивает дуло по горизонтальному `npc.forward`, а цель ниже линии плеча и
ствол смещён вбок от оси корня — остаётся постоянная ошибка ≈4.5–5.5° вверх и ≈2° вправо в Idle/Shoot
(на 7–9 м это 0.6–0.8 м над центром корпуса), в ходьбе/стрейфе больше (7–13°). Статическая поправка
не наводит ствол на цель на разной дистанции/высоте: нужно наведение (yaw — BotBody как единственный
автор корня с учётом смещения дула; pitch — позой Animator). SDK/weapon-system не причастны.
