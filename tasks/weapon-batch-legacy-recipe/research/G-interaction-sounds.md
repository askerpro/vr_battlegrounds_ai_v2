# G. Звуки взаимодействия: анчоры, карманы, падение (исследование 2026-10-23)

Только чтение: разбор YAML префабов и заголовков аудиофайлов скриптами
`tasks/weapon-batch-legacy-recipe/reports/scan_sounds.py`, `audio_info.py` и `lib_scan.py`. Unity не запускался.
Результаты лежат в `reports/scan_sounds.txt`, `audio_info.tsv` и `lib_candidates.tsv`. Папка `reports/` не попадает в git.

Звуки паков ThirdParty в этом worktree — LFS-указатели. Длительности и форматы прочитаны из основного checkout
`F:/UnityProjects/Vr_Battlegrounds_ai`. Внешняя библиотека — `F:/UnityProjects/_SoundLibrary/Universal Sound FX`
(см. `Docs/sound-library.md`). Подбор сделан по названию и длительности. Качество проверяется только на слух в Unity или шлеме.

## 1. Что звучит сейчас

Кто отвечает за звук в коде:

- `AnchorSound` (`Assets/Scripts/Interaction/AnchorSound.cs`) висит на якоре-приёмнике.
  - Вставка: событие `Placed`, клип — `clip` источника. В Unity 6 он сериализован как `m_Resource` AudioSource. Если поле пустое, играет `MagazineAnchorSoundDefaults.InsertClip`.
  - Доставание: событие `Removed` или `UxrMagazinePocket.ItemExtracted`, клип — `_takeOutClip`.
- `ImpactSound` (`Assets/Scripts/Interaction/ImpactSound.cs`) висит на корне предмета и реагирует на `OnCollisionEnter`.
  - Порог 1,2 м/с, полная громкость с 5 м/с, пауза 0,2 с.
  - Предмет в руке молчит.
- `UxrAudioManipulation` (SDK) — Grab/Place/Release на самом предмете. Остался у всех магазинов, но почти везде с пустыми клипами.
- `InteractionFeedback` (`Assets/Resources/InteractionFeedbackConfig.asset`) звука не даёт.
  - `_itemGrab`, `_itemPlace` и `_itemRelease` пусты.
  - `Feedback_GrabReady.prefab` содержит только `HapticPlayer`.
- Штатного звука хвата или анчора в UltimateXR нет. «Дефолт SDK» — это только остатки `UxrAudioManipulation`, перенесённые из сэмплов.
  Самое заметное: `AR15_Magazine.prefab` → `_audioOnGrab = Assets/Audio/SFX/Universal/Magazine_attach.mp3`.

«Дефолтный» звук, который слышит пользователь, — одна общая пара клипов. Её расставил `AnchorSoundInstaller`
(`Assets/Editor/VR_Battlegrounds/Gameplay/AnchorSoundInstaller.cs`, константы `PlaceClip`/`TakeClip`) **на все якоря
без разбора роли**: слоты арсенала, спину, бедро и карман магазинов.

- Вставка — `Assets/Audio/SFX/Universal/Landing_2_Amm.mp3`.
  - Копия из Hands pack `Sounds/7Landing_Amm`: приземление персонажа с бряцанием амуниции.
  - mp3 128k, стерео, 0,44 с.
- Доставание — `Assets/Audio/SFX/Universal/Weapon_Select.flac`.
  - Копия из Hands pack `Sounds/14Weapon_Select`: FPS-звук смены оружия.
  - FLAC, стерео, 48 кГц, 0,50 с.
  - В `.meta` стоит `DefaultImporter` (2019 года), настроек импорта не видно.
  - Тесты `AnchorSoundCoverageTests` зелёные (прогон `bots-validation/reports/tails858`), значит Unity клип загружает.
  - Force To Mono и Vorbis на нём не настроены — проверить в Unity.

| Событие | Сейчас | Где задано | Чьё |
|---|---|---|---|
| (а) взять с анчора арсенала | `Weapon_Select.flac` | `Assets/Prefabs/Arsenal/Slots/FireArmSlotPrefab.prefab` → `WeaponAnchor` и `MagAnchor` (`AnchorSound._takeOutClip`). Используют `CommonOpenArsenalStation`, `LobbyDemoArsenalStation`, `ShelfSlotPrefab Variant` (наследует) | общий проектный дефолт (installer) |
| (б) положить на анчор арсенала | `Landing_2_Amm.mp3` | тот же слот, `AudioSource.m_Resource` якоря | общий проектный дефолт |
| (в) уронить оружие | случайно один из `Assets/Audio/SFX/Impacts/IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav` (1,48 с), `IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav` (1,20 с), `IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav` (1,94 с); громкость до 0,9 | `ImpactSound._clips` на корне каждого ствола реестра (17 префабов). Пишет `ImpactSoundInstaller.WeaponClips`, вызывается из `HandsPackWeaponBuilder` | проектный (Universal SFX, выбран по названию, не на слух) |
| (г1) магазин выпал из оружия | `Universal/Magazine_drop.mp3` (Hands pack, 0,47 с, стерео) — AR15, Browning, MP5K, PPK, Revolver, Scar, SniperRifle, Uzi, Fabarm; `Audio/SFX/Weapons/Kinemation/<W>/<W>_MagOut.wav` (моно, 0,37–0,75 с) — AK105, Herrington, MKR9, Mk14, R08, SRM12, TR15, Viper | `AnchorSound._takeOutClip` на `MagAnchor` ствола. Дефолт — `HandsPackBuildDefaults.MagazineTakeOutAudio` (константа в коде, не ассет) | проектный |
| (г2) магазин ударился о пол | `Impacts/IMPACT_Metal_Soft_Plate_01_mono.wav` (0,39 с) / `_02_mono.wav` (0,48 с); громкость до 0,6 | `ImpactSound` на корне магазина (19 префабов), `ImpactSoundInstaller.MagazineClips` | проектный |
| (г3) магазин взяли в руку | только AR15: `Magazine_attach.mp3` | `AR15_Magazine.prefab` → `UxrAudioManipulation._audioOnGrab` | остаток SDK-донора; звучит при каждом хвате, в том числе из кармана (дубль с карманом) |
| (д) достать из кармана | `Weapon_Select.flac` | `Assets/Prefabs/Player/Equipment/Pockets/Anchor_Back.prefab`, `Anchor_Hip_R.prefab`, `MagazinePocket.prefab`. MEF-аватары вкладывают их как префабы. **`PlayerControllersCyborgAvatar.prefab` хранит распакованные копии** с теми же клипами | общий проектный дефолт |
| (е) положить в карман | `Landing_2_Amm.mp3` | те же карманы, `AudioSource.m_Resource` | общий проектный дефолт |

| (ж) падение гильз и патронов | латунь (Pistol/Rifle/Sniper `_Casing` и `_Round`): случайно `Audio/SFX/Arsenal/IMPACT_Metal_Cling_Bright_mono.wav` (0,68 с) или `Audio/SFX/Impacts/IMPACT_Bullet_Metal_01_mono.wav` (0,23 с); громкость 0,4/0,45. Дробь (`Shotgun_Hull`, `Shotgun_Shell`): только `IMPACT_Bullet_Metal_01_mono.wav`, громкость 0,3/0,35 — пластиковая гильза звенит металлом | `WeaponEjecta._impactClips` в `Assets/Prefabs/Weapons/Ejection/*.prefab` (8 префабов). Звучит только первое касание, со скорости 0,4 м/с. Пишет `WeaponSystemAuthoring` (`BrassImpacts`/`HullImpacts`, строки 870–872; `EjectaSpec`) — правка руками в префаб будет перезаписана | проектный (звон жетона и рикошет пули, а не гильзы) |

Для сравнения: жетон `Assets/Prefabs/Arsenal/DogTag/DogTagPanel.prefab` уже озвучен своей парой — вставка
`Audio/SFX/Arsenal/LOCK_Metal_Clasp_01_mono.wav`, снятие `IMPACT_Metal_Cling_Bright_mono.wav`.

Карманов на аватаре три: `Anchor_Back` (основное, спина), `Anchor_Hip_R` (второе, бедро) и `MagazinePocket`.
Плечевого кармана нет.

## 2. Кандидаты

Пометки: **[в проекте]** — уже в `Assets/Audio`; **[KTSP]** — `Assets/ThirdParty/KINEMATION/TacticalShooterPack/SFX/`;
**[HWAP]** — `Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/`; **[USFX]** — внешняя библиотека
Universal Sound FX (моно, 44,1 кГц, 16 бит). Файлы KTSP — стерео 48 кГц и длинные. Их нужно резать (`Tools/Audio/audio_cut.py`
работает только с PCM WAV) и сводить в моно, как уже нарезаны `Audio/SFX/Weapons/Kinemation/*`. HWAP — стерео mp3,
`audio_cut.py` их не режет.

### (а) Взять оружие со стойки или слота арсенала
1. [USFX] `FOLEY/STORAGE_CASE/STORAGE_CASE_Open_Latch_01_mono.wav` (0,62 с) / `_03_mono.wav` (0,49 с) — защёлка отпускает. Перекликается с засовом стены (`LOCK_*`).
2. [KTSP] `General/S_Pickup.WAV` (1,56 с, стерео) — подъём предмета из шутерного набора; нарезать до ~0,5 с.
3. [KTSP] начало `TR15/Actions/S_TR15_Draw.WAV` (1,98 с) или `AK105/Actions/S_AK105_Draw.WAV` (1,95 с) — оружейная фактура «снял».
4. [USFX] `MOVEMENT/Fabrics_Metal_Rattles/MOVEMENT_Fabrics_and_Metal_Rattles_09_mono.wav` (1,02 с) — бряцание при подъёме; подрезать.

### (б) Положить оружие на анчор арсенала
1. [в проекте] `Assets/Audio/SFX/Arsenal/LOCK_Metal_Clasp_01_mono.wav` (0,92 с) — зажим стойки; уже звучит у жетона, стена получится цельной.
2. [USFX] `FOLEY/STORAGE_CASE/STORAGE_CASE_Close_Latch_01_mono.wav` (0,59 с) — короткий щелчок фиксации.
3. [USFX] `IMPACTS/Metal/IMPACT_Metal_Medium_Hollow_Single_mono.wav` (0,48 с) или `THUDS_THUMPS/THUD_Medium_01_mono.wav` (0,44 с) — «поставил на полку».
4. [UXR sample] `Assets/ThirdParty/UltimateXR/Samples/FullScene/Audio/MagPlace.wav` (0,36 с, стерео 24 бит) — нейтральный щелчок; свести в моно.

### (в) Уронить оружие
1. [USFX] `IMPACTS/Metal/IMPACT_Metal_Objects_Drop_Hard_Surface_02_mono.wav` (2,02 с) — второй вариант нынешнего семейства, просто разнообразие.
2. [USFX] `THUDS_THUMPS/THUD_Dark_01_mono.wav` (0,97 с), `THUD_Dark_02_mono.wav` (0,89 с) — глухой тяжёлый удар корпуса. Нынешние «Tool/Rod Drop» звенят как металлолом.
3. [USFX] `IMPACTS/Metal/IMPACT_Metal_Medium_Box_Rattle_mono.wav` (0,73 с), `IMPACT_Metal_Rattle_Dirty_mono.wav` (0,30 с) — дребезг деталей; хороши вторым слоем или для пистолетов.
4. [HWAP] `4Grenade_Set/Grenade_Drop.mp3` (0,31 с, стерео) — падение небольшого металлического предмета; вариант для пистолетов.

### (г) Уронить магазин
Выпадение из оружия (снятие с гнезда):
1. [USFX] `WEAPONS/Firearms/Handling/HANDLING_B_FS92_9mm_Remove_Loaded_Magazine_mono.wav` (0,76 с) / `..._Remove_Empty_Magazine_mono.wav` (0,94 с) — настоящий сброс магазина. Подходит стволам с общим `Magazine_drop.mp3`, в первую очередь пистолетам.
2. Kinemation `*_MagOut.wav` уже свои и моно. Остаются как есть.

Удар о пол:
1. [USFX] `IMPACTS/Metal/IMPACT_Metal_Soft_Plate_Double_Bounce_mono.wav` (1,41 с) — отскок; добавить третьим вариантом к нынешним `Soft_Plate_01/02`.
2. [USFX] `THUDS_THUMPS/THUD_Bright_01..03_mono.wav` (0,19–0,34 с), `IMPACTS/Metal/IMPACT_Metal_Hit_Metal_mono.wav` (0,32 с) — короткий сухой стук, ближе к полимерному магазину.
3. [HWAP] `4Grenade_Set/Grenade_Drop.mp3` (0,31 с) — небольшой предмет упал.
4. [USFX] `IMPACTS/Metal/IMPACT_Metal_Sheet_Subtle_01/02_mono.wav` (1,19/1,13 с) — кандидат из `Docs/sound-library.md`.

### (д) Достать оружие или магазин из кармана
1. Бедро (пистолет): [KTSP] `General/S_DrawPistolWithHolster.WAV` (1,5 с) — именно «из кобуры»; нарезать до ~0,6 с.
2. Спина (винтовка): [KTSP] `General/S_Equip.WAV` (1,03 с) или начало `*/S_*_Draw.WAV`. Второй вариант — [USFX] `FABRIC_CLOTHING/FABRIC_Movement_Fast_01/02_mono.wav` (0,51/0,50 с): ткань и ремень, коротко, моно, готово как есть.
3. Карман магазинов: [USFX] `FABRIC_CLOTHING/FABRIC_Movement_Nylon_04_mono.wav` (0,68 с) — нейлоновый подсумок; для чередования `FABRIC_Movement_Short_RR1..RR4_mono.wav` (0,80–1,12 с).
4. Запасной: [KTSP] `General/S_Aim_In.WAV` (1,51 с) — шорох экипировки; подрезать.

### (е) Положить в карман
1. Бедро: [KTSP] `General/S_HolsterPistolWithHolster.WAV` (1,5 с), нарезать.
2. Спина: [KTSP] `General/S_UnEquip.WAV` (1,0 с) или `*/S_*_Holster.WAV` (`R08` — 0,70 с, `Mk14EBR` — 1,0 с, `Herrington` — 1,0 с).
3. Все карманы, короткий моно-вариант: [USFX] `FABRIC_CLOTHING/FABRIC_Flap_02_mono.wav` (0,45 с), `FABRIC_Flap_01/05/10` (0,67–0,68 с); для сдвига экипировки `MOVEMENT/Fabrics_Metal_Rattles/*_03_mono.wav` (1,09 с).
4. Карман магазинов: оставить `Landing_2_Amm.mp3` (амуниция бряцает) или взять [USFX] `FABRIC_Movement_Nylon_04`.

### (ж) Падение гильз
Своих звуков гильз нет ни в KINEMATION, ни в Hands. Есть только длинные `R08/S_R08_Eject_*` (2,3–2,7 с) — выброс из барабана.
В USFX есть готовая серия `WEAPONS/Firearms/Casings`: моно, на твёрдое, с вариантами RR.
1. `CASING_9mm_Hard_Surface_RR1..RR4_mono.wav` (0,56–0,94 с) — пистолеты и ПП.
2. `CASING_7-62_mm_Hard_Surface_RR1..RR3_mono.wav` (0,68–1,12 с) — винтовки и снайперки.
3. `CASING_Shotgun_Shell_Hard_Surface_RR1..RR3_mono.wav` (1,13–1,45 с) — дробовые гильзы.
4. `CASING_45_Hard_Surface_RR1/RR2` (0,90–0,94 с) и `CASING_22_*` (0,84–0,86 с) — запасные.

Владелец — `WeaponEjecta` префаба вылета. Клипы задаются в `WeaponSystemAuthoring.BrassImpacts`/`HullImpacts`
(зона weapon-system). Естественно развести латунь по калибру: в `EjectaSpec` у каждого префаба свой массив.

### Папка для прослушивания
`tasks/weapon-batch-legacy-recipe/reports/sound-candidates/` (не в git). Копии файлов без конвертации. Папки по схеме
пользователя:

| Папки | Событие |
|---|---|
| `01`/`02_weapon_anchor_take/place` | стойки и слоты арсенала |
| `03`/`04_pistol_pocket_*` | бедро |
| `05`/`06_rifle_pocket_*` | спина |
| `07`/`08_magazine_pocket_*` | карман магазинов |
| `09_magazine_detach` / `10_magazine_attach` | отщёлкнуть / присоединить магазин |
| `11_weapon_drop`, `12_magazine_drop`, `13_casing_drop` | падения |

- В каждой папке `00_current_*` — текущие звуки, `NN_<пак>_<имя>` — кандидаты, лучшие первыми.
- В `09`/`10` — звуки по стволам:
  - нарезки KINEMATION `*_MagOut`/`*_MagIn`: MagOut сейчас звучат у своих стволов, MagIn не используются — гнёзда берут общий `MagazineInsert_Default.wav`;
  - сырые перезарядки наборов Hands, из которых режется кусок своего ствола: `1Assault_Rifle_Set/Reload.mp3` — AR15, Scar, MP5K; `8Pistol/Reload_Full.mp3` — PPK, Browning; `11Sniper_Rifle_Set/Reload.mp3` — SniperRifle; `12Tommy_Gun/Tommy_Gun_Reload.mp3` — Uzi;
  - USFX handling: Remove/Insert Full/Empty Magazine.
- `index.csv` — UTF-8 с BOM, разделитель «;». Колонки: событие; ствол; файл; исходный путь; длительность; почему; оценка; метка.
- `README.txt` — как ставить отметки.

Собирает `reports/collect_candidates.py`, повторный запуск пересоздаёт папку.

### Чего в паках нет
- Отдельного «оружие упало на пол» (полимер плюс металл) нет ни в KINEMATION, ни в Hands. Есть только общие падения металла и глухие удары USFX.
- Нет липучки или подсумка (velcro, pouch). Короткие ремни или антабки (sling или strap rattle) тоже отдельно не записаны.
- Нет «повесить на крюк/стойку оружейного шкафа». Есть только защёлки кейсов и засовы.
- Оружейная фоли-фактура «рука по корпусу» есть только внутри длинных `Draw/Holster/Equip` KINEMATION, слитых с механикой.
- `Low Poly Weapons VOL.2`, `ParticlePack`, `Military Soldier Mega Bundle` и `BonelabAvatars` аудио не содержат.
  Аудио Blaze AI и Mirror (демо: голоса, шаги, 8-бит) не подходят.

Если этого не хватит, искать оружейный фоли-пак: drop, holster, draw, pouch и velcro, sling rattle, rack.

## 3. Где ставить: владельцы и тесты

**Один владелец на событие:**
- Анчоры и карманы (а, б, д, е) — `AnchorSound` якоря-приёмника.
  - Звук предмета при размещении запрещён: прозвучит дважды (`AmmoInsertSoundTests.У_боеприпасов_нет_своего_звука_размещения`).
  - `InteractionFeedback` (`_itemPlace` и т. п.) не использовать для звука: получится второй писатель того же события.
- Падение (в, г2) — `ImpactSound` предмета.
- Выпадение магазина из оружия (г1) — `AnchorSound._takeOutClip` гнезда (`TakeOutOnlyByHand=false`, звучит и от кнопки A/X).

**Где лежат клипы сейчас (точки правки):**
- Слоты арсенала — `FireArmSlotPrefab.prefab` (`WeaponAnchor`, `MagAnchor`).
- Карманы — `Pockets/Anchor_Back.prefab`, `Anchor_Hip_R.prefab`, `MagazinePocket.prefab` плюс **отдельно** копии в
  `Assets/Prefabs/Player/Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab`. Это зона avatar-ik; правка
  только базовых карманов Cyborg не изменит.
- `AnchorSoundInstaller` дописывает только пустые якоря, уже настроенные не перезаписывает. Его константы
  `PlaceClip`/`TakeClip` надо поменять вместе с префабами, иначе новые якоря получат старую пару.
- Падение — константы `ImpactSoundInstaller.WeaponClips`/`MagazineClips` (зона weapon-system). Затем меню
  `Tools/VR Battlegrounds/Gameplay/Apply Impact Sounds`, которое пересохраняет 36 префабов реестра. Сборщик пака
  применяет те же константы при пересборке.
- Снятие магазина по умолчанию — константа `HandsPackBuildDefaults.MagazineTakeOutAudio`. Свои клипы стволов — в рецептах сборщика (`TakeOutAudio`).

**Общие ассеты дефолтов:**
- `Assets/Resources/MagazineAnchorSoundDefaults.asset` — только `_insertClip = Assets/Audio/SFX/Weapons/MagazineInsert_Default.wav`.
  Это вставка магазина в гнездо; используется, когда у гнезда пустой clip.
- `Assets/Data/WeaponSystem/{Rifle,Shotgun,Pistol,Sniper}FeedbackDefaults.asset` — только механизм (`WeaponCue`: затвор,
  сухой спуск, отказ, гильза). Хвата, кармана и падения там нет.

**Рекомендация.** Расширить `MagazineAnchorSoundDefaults` в дефолты якорей по роли: `AnchorRoleKind` уже есть
(`Assets/Scripts/Interaction/AnchorRole.cs`: Magazine/Primary/Secondary/AvatarOther/World). По тем же ролям разложен
`InteractionFeedbackConfig`.
- Правило `AnchorSound` — «свой клип → дефолт роли (вставка и доставание)», по образцу `WeaponFeedbackDefaults.Resolve`.
- Клипы из карманов, слотов арсенала и Cyborg-копий убрать. Тогда подбор на слух — правка одного ассета, без проходов по префабам.
- Цена: правка класса и ассета (зона manipulation), одноразовая очистка 5 префабов, правка `AnchorSoundInstaller`.
  После приёмки пользователем — обновление тестов.
- Дешёвый вариант без кода: заменить клипы в 5 префабах и в константах installer. Тесты останутся зелёными: они проверяют наличие клипа, а не его имя.

Для падения то же стоит сделать отдельным шагом: клипы удара — в категорию `WeaponFeedbackDefaults` (пистолет звучит
легче винтовки) или в отдельный ассет. Сейчас 36 сериализованных копий массива.

Попутно: убрать `Magazine_attach.mp3` из `AR15_Magazine.prefab/UxrAudioManipulation._audioOnGrab` — сейчас это двойной
звук при доставании магазина AR15 из кармана. Пустые `UxrAudioManipulation` на остальных магазинах — мёртвый груз.

**Тесты, закрепляющие текущие звуки.** Все проверяют наличие и конфигурацию, а не конкретный клип.
- `Assets/Tests/EditMode/Prefabs/AnchorSoundCoverageTests.cs` — у каждого якоря префабов и сцен сборки есть вставка (`InsertClip`) и `TakeOutClip`.
- `AvatarLoadoutTests.Карманы_звучат_при_укладке_и_доставании` — три кармана каждого зарегистрированного аватара проверяются по трём пунктам:
  - звук есть;
  - `TakeOutOnlyByHand=true`;
  - источник не лежит на объектах `Activate On *`.
- `AnchorActivationAudioTests` проверяет три вещи:
  - нет Play On Awake на объектах, которые включает якорь;
  - у гнезда магазина есть вставка и выпадение, `TakeOutOnlyByHand=false`;
  - `AnchorSound` ссылается на источник.
- `AmmoInsertSoundTests` проверяет три вещи:
  - есть `MagazineAnchorSoundDefaults` с `InsertClip`;
  - гнёзда магазинов без своего clip;
  - у боеприпасов нет `_audioOnPlace`.
- `WeaponFeedbackTests` — у гнезда магазина каждого спуска есть `AnchorSound` с вставкой и снятием.
- `KinemationWeaponTests.Звуки_моно_короткие_не_из_пака` — у стволов Kinemation звуки магазина не из папки пака, моно и не длиннее 2,5 с.
- `Assets/Tests/EditMode/Weapons/WeaponEjectionTests.cs` — префабы вылета из `EjectaPrefabPaths` с `WeaponEjecta` и слоем. Клипы не проверяются.
- `ImpactSoundTests` проверяет:
  - у оружия и магазинов реестра есть `ImpactSound` с клипами;
  - источник объёмный, без Play On Awake;
  - логику `ShouldPlay`/`Volume`.

Импорт новых клипов — `Tools/VR Battlegrounds/Audio/Apply SFX Import Settings` (Force To Mono, Vorbis 0,7, Load Type по
длине). По правилам проекта тесты на новые клипы или дефолты ролей появляются только после того, как пользователь их прослушает и примет.
