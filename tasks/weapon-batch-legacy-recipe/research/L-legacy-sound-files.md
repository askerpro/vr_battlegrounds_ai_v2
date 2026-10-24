# L. Легаси-звуки вне конвенций (2026-10-24)

Read-only инвентаризация. Область: все аудиофайлы `Assets/**` кроме `Assets/ThirdParty/**` — 129 файлов, и все они уже
лежат в `Assets/Audio/SFX/` (вне `Audio/SFX` — в `Prefabs/`, `Scenes/`, `Art/` и т. п. — аудио нет). Кириллицы в именах нет;
транслит — только `Universal/Zatvor*.mp3`. Generic-имён вида `1.mp3`, `New Audio`, `sound (1)` нет.

Метод: `.meta` → GUID и настройки импорта; поиск GUID по `*.prefab|*.asset|*.unity|*.controller|*.overrideController|
*.mat|*.playable|*.anim|*.json` в `Assets` (включая ThirdParty) и `ProjectSettings`; поиск строковых путей и имён в
`*.cs|*.py|*.json|*.js` (`Assets`, `Tools`); WAV — `wave` (каналы, частота, длина, пик, доля пика в первых 10 мс, время до
5 % пика); mp3 — заголовок кадра (каналы, частота), длительность mp3/flac — из отчёта K (audaspace); sha256 — для дублей,
в том числе сравнение с LFS-oid файлов паков в ThirdParty (в этом worktree паки — LFS-указатели). Подробный аудит тишины
в начале — [K-leading-silence.md](K-leading-silence.md); здесь его выводы только цитируются.

## Конвенции, по которым сверялось

Из `Docs/sound-library.md`, `HandsPackAudio`/`KinemationAudio`, `SfxImportSettings`, `Docs/tasks/T-39-*`:

- Папка: `Assets/Audio/SFX/<категория>/`. Фактические категории: `Ambience`, `Arsenal`, `Casings`, `Handling`, `Impacts`,
  `Lobby`, `UI`, `Weapons/<пак>/<ствол>/`. Папки `Universal` в конвенции нет — это бывшая `Weapons/Universal`
  (переименование 2026-09-27, `30d08d614`).
- Имя: из библиотеки/пака — имя источника без изменений плюс суффикс каналов `_mono`/`_stereo`
  (`IMPACT_Metal_Cling_Bright_mono`, `S_Aim_In_mono`); куски билдеров стволов — `<Ствол>_<Событие>.wav`
  (`BoltBack`, `BoltForward`, `MagIn`, `MagOut`, `Shot`, `Load`); результат нарезки рецептом — рядом с суффиксом `_Cut`,
  исходник остаётся.
- Формат: WAV PCM (уровень для preflight `WeaponSystemAuthoring` читается из WAV через `SfxClipLevel`; mp3 проверяется только
  через `GetData` распакованного клипа; `Tools/Audio/audio_cut.py` режет только PCM WAV).
- Импорт: `SfxImportSettings` — Force To Mono, Vorbis 0,7; Load Type — Decompress On Load (< 1 с), иначе Compressed In
  Memory; Android приводится к тем же значениям. Исключение — стерео-амбиенты/музыка.
- Перенос — с сохранением GUID: внутри Unity `AssetDatabase.MoveAsset`, снаружи — файл вместе с `.meta`.

Обозначения импорта ниже: DoL — Decompress On Load, CiM — Compressed In Memory, q — качество Vorbis.

## 1. Главная группа: ручная нарезка ранних паков (`Audio/SFX/Universal/`, 7 файлов)

Все 7 — git-tracked, не LFS (`*.mp3`/`*.flac` в `.gitattributes` — `binary`), импорт ни у одного не по конвенции
(`forceToMono: 0`, q 1,0 — то есть 100 %, не через `SfxImportSettings`). Владелец папки по `expert.json` — weapon-system.

| файл | формат | GUID | происхождение (sha256 / LFS-oid) | кто ссылается (ассеты) | строковые пути в коде | используется? |
|---|---|---|---|---|---|---|
| `Universal/Zatvor.mp3` | mp3, 2 кан., 44,1 кГц, 9 434 Б | `daa3c5973b6c4084d9ca3fed1e31f6e5` | побайтно = пак Hands `Sounds/10Shotgun_Set/Zatvor.mp3` | нет | нет (строки `…Zatvor.mp3` в `HandsPackWeaponBuilder.cs:180,303` и `HandsPackLegacyRebuild.cs:48` — пути **пака**: `HandsPackAudio.Pack + "8Pistol/Zatvor.mp3"` и `"10Shotgun_Set/Zatvor.mp3"`) | **нет** |
| `Universal/Zatvor1.mp3` | mp3, 2 кан., 44,1 кГц, 6 727 Б, ~0,359 с (K) | `dc32b8fc851caab4ab723c3a3bd08340` | своя нарезка, побайтного совпадения с паком нет | `Data/WeaponSystem/SniperFeedbackDefaults.asset` (`_actionForwardChambered`, `_actionForwardEmpty`); `Prefabs/Weapons/SniperRifle/SniperRifle.prefab` ×2; `Prefabs/Weapons/SniperRifleAuto/SniperRifleAuto.prefab` ×2 | `WeaponSystemAuthoring.cs:134` (Forward у `DefaultsSpec` Sniper); `HandsPackWeaponBuilder.cs:352` (`SlideForwardAudio` AX-50) | да — затвор вперёд снайперских |
| `Universal/Zatvor2.mp3` | mp3, 2 кан., 44,1 кГц, 4 955 Б, ~0,216 с (K) | `52517715cb195da41940f3fbf3a7092f` | своя нарезка | `SniperFeedbackDefaults.asset` (`_actionBack`); `SniperRifle.prefab`, `SniperRifleAuto.prefab` (`_actionBack`) | `WeaponSystemAuthoring.cs:133`; `HandsPackWeaponBuilder.cs:352` (`SlideBackAudio`) | да — затвор назад снайперских |
| `Universal/Magazine_attach.mp3` | mp3, 2 кан., 44,1 кГц, 12 828 Б | `3279f9a811e68ee419c458ad8034f351` | побайтно = пак `Sounds/8Pistol/Zatvor.mp3` (= `9Run_Set/Zatvor.mp3`) — **имя вводит в заблуждение: это затвор пистолета, не вставка магазина** | нет | `AnchorSoundInstaller.cs:40` `MagInClip` — запасной клип гнезда магазина, если у роли нет дефолта вставки (`:119`) | ассетами нет; только запасной путь установщика |
| `Universal/Magazine_drop.mp3` | mp3, 2 кан., 44,1 кГц, 7 986 Б | `cc4466c2801308840b00f782707d7d06` | своя нарезка (появился в `30d08d614`) | нет | `AnchorSoundInstaller.cs:41` `MagOutClip` — запасной `_takeOutClip` гнезда (`:127`) | ассетами нет; только запасной путь |
| `Universal/Landing_2_Amm.mp3` | mp3, 2 кан., 44,1 кГц, 6 977 Б | `7389ab3e6588eb34e8f637230ab4cca5` | побайтно = пак `Sounds/7Landing_Amm/Landing_2_Amm.mp3` | нет (снят `AnchorSoundRoleDefaultsCleanup`) | `AnchorSoundInstaller.cs:38` `PlaceClip` (запасной, `:119`); `AnchorSoundRoleDefaultsCleanup.cs:93` — ищет старую пару, чтобы снять | ассетами нет; код держит путь |
| `Universal/Weapon_Select.flac` | FLAC, 2 кан., 48 кГц, 0,5 с, 64 660 Б | `4076bb3823dc4c2428499ff7ab222d97` | побайтно = пак `Sounds/14Weapon_Select/Weapon_Select.flac`; `.meta` скопирован из пака (`licenseType: Store`) и содержит **`DefaultImporter`** — Unity не создаёт из него `AudioClip` | `Prefabs/Player/Equipment/Pockets/Anchor_Back.prefab` ×1, `MagazinePocket.prefab` ×2 — `AudioSource.m_Resource {fileID: 8300000}` | `AnchorSoundInstaller.cs:39` `TakeClip` (`:127`); `AnchorSoundRoleDefaultsCleanup.cs:94` (`LoadAssetAtPath<AudioClip>`) | ссылки есть, но, судя по `.meta`, ведут в пустоту (Missing) — **проверить в Unity** |

Тишина в начале (K, onset_30 — первое окно ≥ 30 % пика): `Zatvor1` 100 мс («сильная»), `Zatvor2` 90 мс, `Weapon_Select`
80 мс; для остальных mp3 замера нет (на них нет ссылок ассетов).

### Предложения

| файл | действие | цель | что обновить вместе с переносом |
|---|---|---|---|
| `Zatvor.mp3` | **не используется — кандидат на удаление, решает пользователь** | — (копия файла пака, остаётся в ThirdParty) | ничего |
| `Zatvor2.mp3` | конвертировать в моно WAV PCM16 44,1 кГц, GUID сохранить; затем срезать ~90 мс тишины рецептом `audio_cut.py` → `_Cut` | `Assets/Audio/SFX/Handling/HandsPack_Sniper_BoltBack_mono.wav` (+ `…_BoltBack_mono_Cut.wav`) | `WeaponSystemAuthoring.cs:133`, `HandsPackWeaponBuilder.cs:352`; затем `MigrateWeapons` пишет префабы и `SniperFeedbackDefaults` (руками в префаб не ставить) |
| `Zatvor1.mp3` | то же, срез ~100 мс | `Handling/HandsPack_Sniper_BoltForward_mono.wav` (+ `_Cut`) | `WeaponSystemAuthoring.cs:134`, `HandsPackWeaponBuilder.cs:352` |
| `Weapon_Select.flac` | сначала подтвердить в Unity, что ссылки карманов Missing; затем конвертировать в моно WAV 48 кГц с тем же GUID (замена файла и `.meta` → импортёр станет `AudioImporter`, `fileID 8300000` начнёт резолвиться) или снять ссылки | `Handling/HandsPack_Weapon_Select_mono.wav` | `AnchorSoundInstaller.cs:39` (`TakeClip`; `AnchorSoundRoleDefaultsCleanup` берёт ту же константу). Внимание: если клип оживёт, в карманах зазвучит звук доставания, которого сейчас, вероятно, нет — это изменение поведения, нужна приёмка на слух |
| `Landing_2_Amm.mp3` | ассетами не используется; держится кодом запасного пути и очистки старой пары. Варианты: удалить вместе с запасным путём и `AnchorSoundRoleDefaultsCleanup` (**решает пользователь**, зона manipulation) или перенести | `Handling/HandsPack_Landing_2_Amm_mono.wav` | `AnchorSoundInstaller.cs:38` |
| `Magazine_attach.mp3` | то же, что `Landing_2_Amm`; при сохранении переименовать по содержимому | `Handling/HandsPack_8Pistol_Zatvor_mono.wav` (по имени источника) | `AnchorSoundInstaller.cs:40` |
| `Magazine_drop.mp3` | то же | `Handling/HandsPack_MagazineDrop_mono.wav` | `AnchorSoundInstaller.cs:41` |

Папку `Universal/` после переноса — удалить (пустую).

## 2. Не та папка или не тот формат (вне `Universal`)

| файл | параметры | GUID | ссылки | код/тесты | проблема | предложение |
|---|---|---|---|---|---|---|
| `Weapons/MagazineInsert_Default.wav` | WAV **стерео** 44,1 кГц, 0,363 с; импорт mono=1, DoL, q **1,0** | `45d1b3c6ecf9f354cb8035a52e81721e` | `Resources/MagazineAnchorSoundDefaults.asset` (`_insertClip`) | нет | побайтная копия `ThirdParty/UltimateXR/Samples/FullScene/Audio/MagPlace.wav`; лежит в корне `Weapons/`, имя не источника, стерео файл | `Handling/MagPlace_mono.wav` (моно-файл, тот же GUID) + `SfxImportSettings` (q 0,7). Тишины в начале нет (первые 10 мс = 100 % пика) |
| `Weapons/UI_Error_Subtle_Deep_stereo.wav` | WAV стерео 44,1 кГц, 0,205 с; mono=1, DoL, q 0,7 | `3528b32d7ea4fb644bb6bb668e0bd775` | 4 × `Data/WeaponSystem/*FeedbackDefaults.asset` (`_refusal`) | `WeaponSystemAuthoring.cs:56` `RefusalClip` (путь); `Assets/Tests/EditMode/Weapons/WeaponSystemPilotPrefabTests.cs:34` — по **имени** `UI_Error_Subtle_Deep_stereo` | библиотечный UI-звук в `Weapons/` | перенести в `UI/` без смены имени (имя библиотеки): `UI/UI_Error_Subtle_Deep_stereo.wav`; правится только путь в `WeaponSystemAuthoring.cs:56`, тест по имени не ломается |
| `Weapons/Revolver/Revolver_Shot.wav` | моно 48 кГц, 1,5 с, CiM | `1757de17fbdc3914c98b81141ec9c387` | `Prefabs/Weapons/Revolver/Revolver.prefab` | `KinemationWeaponBuilder.cs:260` `RevolverShotPath` (+ `HandsPackWeaponBuilder.cs:333`) | ствол пака Hands со звуком из R08 — вне `Weapons/<пак>/<ствол>/` | **оставить**: вынесен намеренно — папка `Weapons/HandsPack/<ствол>/` входит в откат пересборки (`HandsPackWeaponBuilder.RebuildOutputs`), перенос туда стёр бы клип при пересборке |
| `Weapons/SniperRifleAuto/SniperRifleAuto_Shot.wav` | моно 44,1 кГц, 0,55 с, DoL | `f8efbe9c70b78534187b0369c5e1c2be` | `SniperRifleAuto.prefab` | `HandsPackWeaponBuilder.cs:384` `SniperRifleAutoShotPath` | то же | **оставить** по той же причине; при желании общей схемы — отдельная папка производных выстрелов (например, `Weapons/Derived/<ствол>/`) с правкой обеих констант |

## 3. Старые имена, уже перекодированные в этой задаче (`a5842e741`, не принято)

`Impacts/Hit_Body_01.wav`, `Hit_Head.wav`, `Hit_Head_Helmet.wav`, `Magazine_Fall.wav`, `Weapon_Fall_Pistol.wav`,
`Weapon_Fall_Rifle.wav` — бывшие mp3, перекодированы в моно WAV с **новыми** GUID, ссылки перенаправлены
(`Resources/PlayerHitEffects.prefab`, `Resources/ImpactSoundDefaults.asset`, `Data/WeaponSystem/*FeedbackDefaults.asset`).
Строковых путей в коде нет. Остаточные отличия от конвенции:

- нет суффикса `_mono` и имени источника — предложение: `Hit_Body_01_mono.wav` и т. д. (`MoveAsset`, GUID сохраняется,
  код править не нужно); можно сделать сейчас, пока изменения не приняты;
- `Weapon_Fall_Rifle.wav` (0,949 с) импортирован как CiM, а по правилу < 1 с должен быть DoL — `SfxImportSettings` после
  нарезки, видимо, не переприменялся;
- `Hit_Head.wav` — 48 кГц (остальные 44,1) — допустимо, отметка для сведения.

`Handling/Magazine_attach_mono.wav` (`1f6104b284fea4943aee34e672e46e40`, `b1cb33d33`, 4 × FeedbackDefaults
`_magazineInsert`) унаследовал имя `Universal/Magazine_attach.mp3`, который на деле — затвор пистолета из пака
(`8Pistol/Zatvor.mp3`). Если он сделан из этого mp3, имя вводит в заблуждение; происхождение проверить на слух или по
истории worker 1128.

## 4. Импорт не по конвенции (чужие зоны, имена допустимые)

| файлы | зона | наблюдение | предложение |
|---|---|---|---|
| `UI/Hud_Alert.wav`, `UI/Hud_Beep.wav` (T-46, `0298465b4`), `UI/CASH_REGISTER_Cha-ching_05_mono.wav` | ui (`Prefabs/UI/HUD/WristDisplay.prefab`) | `forceToMono: 0`, q 1,0 | `SfxImportSettings.Apply`; имена `Hud_*` — свои синтезированные тоны, допустимо добавить `_mono` (ссылки только по GUID) |
| `Lobby/Forest.wav` (стерео, 16,4 с), `Gunfire`, `Helicopter`, `SoldiersSteps`, `SoldiersVoice`, `Truck` (моно) | level-design (`Data/Audio/LobbyAmbienceProfile.asset`, `Scenes/Lobby.unity`) | общие имена без источника/суффикса; `Gunfire` и `Truck` — импорт по умолчанию (пустой `AudioImporter`), остальные mono=0, DoL; `Truck` 19 с в DoL и 1,82 с тишины в начале; `Forest` 16 с стерео в DoL | не легаси (2026-10-11), отдельное решение владельца: `SfxImportSettings` (длинные → CiM; `Forest` оставить стерео как амбиент), срез тишины `Truck` рецептом. Переименование потребует правки `LobbyDecorationAuthoring.cs:374–383, 407` (`Clip(name)` грузит `"Assets/Audio/SFX/Lobby/" + name + ".wav"`); `LobbyAmbienceProfile.cs:6` — имена enum, не пути |

## 5. Прочие неиспользуемые (ссылок ассетов нет)

| файл | почему не трогать / что сделать |
|---|---|
| `Impacts/IMPACT_Metal_Soft_Plate_01_mono.wav`, `_02_mono.wav` | ни ассетов, ни кода; кандидаты из `sound-library.md` для падения магазина, вытеснены `Magazine_Fall` — **не используется — кандидат на удаление, решает пользователь** |
| `Weapons/HandsPack/SniperRifle/SniperRifle_BoltBack.wav`, `_BoltForward.wav`; `…/SniperRifleAuto/SniperRifleAuto_BoltBack.wav`, `_BoltForward.wav` | выходы `HandsPackAudio` (пары побайтно равны между папками; пик −14,5/−14,9 dBFS — поэтому выбрана пара `Zatvor`); пересборка создаст их снова — удалять имеет смысл только вместе с отказом билдера их писать |
| `Weapons/HandsPack/Uzi/Uzi_Shot.wav` | выход билдера; 52 мс, пик −29,9 dBFS (почти тишина) — дефектный кусок, не используется |
| `Weapons/Kinemation/TR15/TR15_MagIn.wav` | выход билдера; не нужен после перехода TR15 на универсал категории (`c53aac6ec`) |
| `Weapons/Kinemation/SRM12/SRM12_BoltBack.wav`, `SRM12_BoltForward.wav` | **оставить**: исходники рецепта `Tools/Audio/recipes/srm12-bolt.json`, тест `Tools/Audio/tests/test_audio_cut.py`, `WeaponSystemWavePrefabTests.cs:35` |

## 6. Риски

1. **Строковые пути в коде.** `WeaponSystemAuthoring.cs:133–134` и `HandsPackWeaponBuilder.cs:352` (Zatvor1/2),
   `AnchorSoundInstaller.cs:38–41` (вся остальная `Universal`), `WeaponSystemAuthoring.cs:56` (RefusalClip). Перенос
   без правки констант: следующий проход писателя получит `null` из `LoadAssetAtPath` и **снимет** клип со стволов и
   дефолтов. Править в одном коммите с переносом.
2. **Чужие зоны.** Ссылки на `Zatvor1/2` — префабы и дефолты weapon-system; на `Weapon_Select` — карманы
   `Prefabs/Player/Equipment/Pockets` (установщик — зона manipulation); `WristDisplay` — ui; лобби и `Ambience` (5 сцен
   карт + `Environment.prefab`) — level-design. При переносе с сохранением GUID эти ассеты не меняются; при новом GUID
   (как в `a5842e741`) — меняются и требуют ревью зон. Для `Zatvor1/2` и `Weapon_Select` рекомендую сохранение GUID:
   в worker под своей арендой положить новый WAV, перенести на него старый `.meta`, удалить mp3/flac, Refresh, затем
   `SfxImportSettings.Apply` и проверить в Inspector, что ссылки резолвятся (`fileID 8300000` у AudioClip тот же).
3. **`Weapon_Select.flac` с `DefaultImporter`.** По `.meta` клип не импортируется как AudioClip, ссылки карманов,
   вероятно, Missing, а `AnchorSoundRoleDefaultsCleanup` (`LoadAssetAtPath<AudioClip>(TakeClip)` → `null`) поэтому не
   может снять старую пару с карманов — вероятная причина, почему ссылка там осталась. Требует проверки в Unity
   (Unity 6000.4.1f1).
4. **Тесты.** Прямых путей `Universal/*` в `Assets/Tests/**` нет. По имени завязаны `WeaponSystemPilotPrefabTests.cs:34`
   (`UI_Error_Subtle_Deep_stereo`) и `WeaponSystemWavePrefabTests.cs:32–35` (`SRM12`). Переименование `UI_Error…`
   ломает тест; перенос папки — нет. По правилам проекта ожидания тестов не подгоняются до приёмки пользователем.
5. **LFS.** `Assets/Audio/**` — не LFS (`*.wav|*.mp3|*.ogg binary`, `git check-attr filter` пусто), все 129 файлов
   tracked. Исходники паков в `Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/**` — LFS, в этом
   worktree — указатели (129–130 Б); для повторной нарезки из пака нужен материализованный LFS (worker).
6. **Дубли (sha256).** Внутри проекта: `HandsPack/BrowningHiPower/*_BoltBack|_BoltForward|_MagOut` = `PPK/*` того же
   события; `HandsPack/MP5K/MP5K_MagIn` = `Scar/Scar_BoltBack` = `Scar/Scar_BoltForward` (затвор Scar звучит как вставка
   магазина MP5K — подозрительно, проверить на слух); `SniperRifle/*Bolt*` = `SniperRifleAuto/*Bolt*` и `*_MagIn|_MagOut`;
   Kinemation `BoltBack` = `BoltForward` у AK105, Herrington, MKR9, Mk14, TR15, Viper (известно, предупреждение
   preflight). С паками: `Universal/Zatvor.mp3`, `Magazine_attach.mp3`, `Landing_2_Amm.mp3`, `Weapon_Select.flac`,
   `Weapons/MagazineInsert_Default.wav` — побайтные копии файлов ThirdParty.
7. **Конвертация mp3/flac.** `ffmpeg` в PATH нет (K); `audio_cut.py` читает только PCM WAV. Нужен декодер (audaspace
   Blender, как в K, или выгрузка из Unity). mp3 — уже с потерями; тишина кодера mp3 ~25 мс входит в замер K.
8. **Документация.** `Docs/sound-library.md` о `Universal` не говорит; правка Docs — только с этапом и владельцем зоны.

## Приложение. Все 129 файлов

Метрики WAV: «1-е 10 мс, % пика» — максимум |x| в первых 10 мс относительно пика файла (≥ 30 % — звук начинается
сразу); «до 5 % пика» — время до первого отсчёта ≥ 5 % пика. Для mp3/flac — только заголовок (длительность см. K).
Ссылки — число ассетов, где встречается GUID.

| файл (от `Assets/Audio/SFX/`) | байт | кан. | Гц | длина, с | ForceToMono | LoadType | Vorbis q | пик, dBFS | 1-е 10 мс, % пика | до 5 % пика, мс | ссылок (ассеты) | sha256[:12] |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `Ambience/AMBIENCE_City_Street_Calm_Day_loop_stereo.wav` | 2137926 | 2 | 44100 | 12.113 | 0 | CiM | 0.7 | -2.1 | 73 | 0.1 | 6 | ce724e0c3abc |
| `Arsenal/DRAWER_Close_03_mono.wav` | 145726 | 1 | 44100 | 1.639 | 1 | DoL | 0.7 | -0.7 | 0 | 357.7 | 2 | 94c6bb734213 |
| `Arsenal/DRAWER_Open_03_mono.wav` | 135024 | 1 | 44100 | 1.53 | 1 | DoL | 0.7 | -0.5 | 0 | 203.7 | 2 | 955ede302f1e |
| `Arsenal/GATE_Metal_Close_03_mono.wav` | 117122 | 1 | 44100 | 1.317 | 1 | DoL | 0.7 | -1.1 | 3 | 15.1 | 2 | a42ba322e30b |
| `Arsenal/GATE_Metal_Open_02_mono.wav` | 113058 | 1 | 44100 | 1.271 | 1 | DoL | 0.7 | -1.1 | 0 | 63.7 | 2 | 67c580dc7e4d |
| `Arsenal/IMPACT_Metal_Cling_Bright_mono.wav` | 59678 | 1 | 44100 | 0.676 | 1 | DoL | 0.7 | -0.5 | 100 | 0.3 | 1 | 85c071df8f45 |
| `Arsenal/LOCK_Metal_Clasp_01_mono.wav` | 81194 | 1 | 44100 | 0.92 | 1 | DoL | 0.7 | -0.5 | 0 | 58.9 | 1 | 6622938476be |
| `Arsenal/LOCK_Metal_Sliding_03_mono.wav` | 83032 | 1 | 44100 | 0.93 | 1 | DoL | 0.7 | -0.5 | 0 | 44.4 | 2 | 08fd6e745ece |
| `Casings/CASING_45_Hard_Surface_RR2_mono.wav` | 80490 | 1 | 44100 | 0.9 | 1 | DoL | 0.7 | -1.6 | 50 | 1.0 | 1 | 386bd97a865e |
| `Casings/CASING_7-62_mm_Hard_Surface_RR3_mono.wav` | 60916 | 1 | 44100 | 0.678 | 1 | DoL | 0.7 | -1.0 | 100 | 1.0 | 1 | b9e0fd179b5c |
| `Casings/CASING_9mm_Hard_Surface_RR4_mono.wav` | 50202 | 1 | 44100 | 0.556 | 1 | DoL | 0.7 | -3.2 | 100 | 0.2 | 1 | aaa889801384 |
| `Casings/CASING_Shotgun_Shell_Hard_Surface_RR1_mono.wav` | 107378 | 1 | 44100 | 1.205 | 1 | DoL | 0.7 | -1.9 | 100 | 2.2 | 1 | 6701579b489e |
| `Handling/HANDLING_B_FS92_9mm_Remove_Empty_Magazine_mono.wav` | 84272 | 1 | 44100 | 0.942 | 1 | DoL | 0.7 | -18.0 | 1 | 32.6 | 4 | e3989fc642ba |
| `Handling/Magazine_attach_mono.wav` | 69164 | 1 | 44100 | 0.784 | 1 | DoL | 0.7 | -3.1 | 0 | 107.1 | 4 | 1ad450d051b1 |
| `Handling/S_Aim_In_mono.wav` | 114636 | 1 | 48000 | 1.194 | 1 | CiM | 0.7 | -23.9 | 7 | 5.3 | 1 | 25bba162c51e |
| `Handling/S_Aim_Out_mono.wav` | 204274 | 1 | 48000 | 2.127 | 1 | CiM | 0.7 | -20.6 | 12 | 2.5 | 1 | dd70085e6ad7 |
| `Handling/S_DrawPistolWithHolster_mono.wav` | 132524 | 1 | 48000 | 1.38 | 1 | CiM | 0.7 | -33.1 | 4 | 15.0 | 1 | c533cc433708 |
| `Handling/S_Equip_mono.wav` | 78956 | 1 | 48000 | 0.822 | 1 | CiM | 0.7 | -28.8 | 15 | 4.0 | 1 | 001b1589e4c4 |
| `Handling/S_HolsterPistolWithHolster_mono.wav` | 119084 | 1 | 48000 | 1.24 | 1 | CiM | 0.7 | -31.3 | 14 | 3.6 | 1 | 4c857ccd0314 |
| `Handling/S_Jump_mono.wav` | 86444 | 1 | 48000 | 0.9 | 1 | CiM | 0.7 | -15.5 | 9 | 3.3 | 1 | 9797c427c9c3 |
| `Handling/S_Pickup_mono.wav` | 107530 | 1 | 48000 | 1.12 | 1 | CiM | 0.7 | -20.9 | 13 | 2.1 | 1 | f7f8424a5886 |
| `Handling/S_UnEquip_mono.wav` | 94066 | 1 | 48000 | 0.979 | 1 | DoL | 0.7 | -29.1 | 16 | 1.9 | 1 | dbeeb831800b |
| `Impacts/Hit_Body_01.wav` | 73268 | 1 | 44100 | 0.83 | 1 | DoL | 0.7 | -0.0 | 11 | 3.5 | 1 | b3a762019fd7 |
| `Impacts/Hit_Head.wav` | 181530 | 1 | 48000 | 1.89 | 1 | CiM | 0.7 | -0.0 | 79 | 5.0 | 1 | 0c9aacbebf25 |
| `Impacts/Hit_Head_Helmet.wav` | 39748 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -0.0 | 95 | 5.0 | 1 | ade2b6eafeab |
| `Impacts/IMPACT_Bullet_Metal_01_mono.wav` | 20636 | 1 | 44100 | 0.233 | 1 | DoL | 0.7 | -0.5 | 100 | 0.1 | 1 | 27129ae909c7 |
| `Impacts/IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav` | 170756 | 1 | 44100 | 1.936 | 1 | DoL | 0.7 | -0.5 | 1 | 85.6 | 1 | a00603bf00be |
| `Impacts/IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav` | 107200 | 1 | 44100 | 1.203 | 1 | DoL | 0.7 | -0.8 | 100 | 1.2 | 1 | 0daaa04a4c7f |
| `Impacts/IMPACT_Metal_Soft_Plate_01_mono.wav` | 34244 | 1 | 44100 | 0.388 | 1 | DoL | 0.7 | -0.5 | 100 | 5.2 | 0 | e2abf7828242 |
| `Impacts/IMPACT_Metal_Soft_Plate_02_mono.wav` | 42242 | 1 | 44100 | 0.478 | 1 | DoL | 0.7 | -0.5 | 89 | 2.8 | 0 | 811cd226a134 |
| `Impacts/IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav` | 130976 | 1 | 44100 | 1.484 | 1 | DoL | 0.7 | -0.5 | 100 | 2.3 | 1 | 2239b630ab94 |
| `Impacts/IMPACT_Wood_Stick_On_Wood_Post_01_mono.wav` | 25564 | 1 | 44100 | 0.279 | 1 | DoL | 0.7 | -1.2 | 100 | 4.5 | 1 | 9d91923fabb3 |
| `Impacts/Magazine_Fall.wav` | 92854 | 1 | 44100 | 1.052 | 1 | CiM | 0.7 | -2.8 | 1 | 15.7 | 1 | 73c19db0668c |
| `Impacts/Weapon_Fall_Pistol.wav` | 88766 | 1 | 44100 | 1.006 | 1 | CiM | 0.7 | -2.9 | 1 | 11.5 | 1 | a570a70ca9b6 |
| `Impacts/Weapon_Fall_Rifle.wav` | 83708 | 1 | 44100 | 0.949 | 1 | CiM | 0.7 | -1.5 | 1 | 11.5 | 3 | e85c51f9a4e5 |
| `Lobby/Forest.wav` | 2897926 | 2 | 44100 | 16.422 | 0 | DoL | 0 | -14.0 | 46 | 0.0 | 2 | 70a021d82ef0 |
| `Lobby/Gunfire.wav` | 176444 | 1 | 44100 | 2.0 | умолч. AudioImporter | — | — | -17.0 | 52 | 0.7 | 1 | 6fe682e94629 |
| `Lobby/Helicopter.wav` | 816410 | 1 | 44100 | 9.256 | 0 | DoL | 0 | -0.5 | 31 | 0.1 | 1 | 4bd9ecc9e74c |
| `Lobby/SoldiersSteps.wav` | 255178 | 1 | 44100 | 2.88 | 0 | DoL | 0 | -3.7 | 4 | 43.8 | 1 | 47119f8b6754 |
| `Lobby/SoldiersVoice.wav` | 43524 | 1 | 44100 | 0.493 | 0 | DoL | 0 | -0.5 | 7 | 5.8 | 1 | 7cd0ad508dd0 |
| `Lobby/Truck.wav` | 1683846 | 1 | 44100 | 19.091 | умолч. AudioImporter | — | — | -1.7 | 0 | 1822.0 | 1 | 6e4f2cb2e71b |
| `UI/CASH_REGISTER_Cha-ching_05_mono.wav` | 63924 | 1 | 44100 | 0.711 | 0 | DoL | 1 | -1.0 | 0 | 30.1 | 1 | bde44783897a |
| `UI/Hud_Alert.wav` | 35324 | 1 | 44100 | 0.4 | 0 | DoL | 1 | -6.0 | 100 | 0.3 | 1 | 8c681854cc8d |
| `UI/Hud_Beep.wav` | 13274 | 1 | 44100 | 0.15 | 0 | DoL | 1 | -6.0 | 100 | 0.3 | 1 | 7612c1e41d73 |
| `Universal/Landing_2_Amm.mp3` | 6977 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 0 | 7f079dc50bec |
| `Universal/Magazine_attach.mp3` | 12828 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 0 | 19f90cbde98f |
| `Universal/Magazine_drop.mp3` | 7986 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 0 | ec2fca952ac8 |
| `Universal/Weapon_Select.flac` | 64660 | 2 | 48000 | 0.5 | DefaultImporter (не AudioClip) | — | — | — | — | — | 2 | 7610990ac03e |
| `Universal/Zatvor.mp3` | 9434 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 0 | a73262ce6584 |
| `Universal/Zatvor1.mp3` | 6727 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 3 | ba7e18b22fef |
| `Universal/Zatvor2.mp3` | 4955 | 2 | 44100 | — | 0 | DoL | 1 | — | — | — | 3 | feea7a2c328e |
| `Weapons/HandsPack/AR15/AR15_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -5.6 | 0 | 25.8 | 1 | 0429ed0972b0 |
| `Weapons/HandsPack/AR15/AR15_Shot.wav` | 34604 | 1 | 44100 | 0.392 | 1 | DoL | 0.7 | -1.0 | 0 | 42.4 | 1 | c6780daa36a6 |
| `Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltBack.wav` | 15920 | 1 | 44100 | 0.18 | 1 | DoL | 0.7 | -27.9 | 0 | 76.3 | 1 | c87ce9eb0a10 |
| `Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltForward.wav` | 30914 | 1 | 44100 | 0.35 | 1 | DoL | 0.7 | -3.1 | 1 | 58.4 | 1 | 7683f09fcb53 |
| `Weapons/HandsPack/BrowningHiPower/BrowningHiPower_MagIn.wav` | 39500 | 1 | 44100 | 0.447 | 1 | DoL | 0.7 | -2.0 | 3 | 17.2 | 1 | 3331fa103fa7 |
| `Weapons/HandsPack/BrowningHiPower/BrowningHiPower_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -1.7 | 0 | 27.5 | 1 | 7002a9faa5cc |
| `Weapons/HandsPack/BrowningHiPower/BrowningHiPower_Shot.wav` | 52082 | 1 | 44100 | 0.59 | 1 | DoL | 0.7 | -2.8 | 0 | 90.7 | 1 | 165f8de5a36b |
| `Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltBack.wav` | 33120 | 1 | 44100 | 0.375 | 1 | DoL | 0.7 | -4.7 | 0 | 41.6 | 1 | 648188f485a2 |
| `Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltForward.wav` | 17656 | 1 | 44100 | 0.2 | 1 | DoL | 0.7 | -6.7 | 2 | 11.9 | 1 | ae0998e469b7 |
| `Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Load.wav` | 25388 | 1 | 44100 | 0.287 | 1 | DoL | 0.7 | -4.1 | 0 | 66.8 | 1 | 4b4a42393fe8 |
| `Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Shot.wav` | 48428 | 1 | 44100 | 0.549 | 1 | DoL | 0.7 | -2.3 | 0 | 64.2 | 1 | f01c4e72b63d |
| `Weapons/HandsPack/MP5K/MP5K_BoltBack.wav` | 39734 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -9.8 | 0 | 30.4 | 1 | 5a9172164fd1 |
| `Weapons/HandsPack/MP5K/MP5K_MagIn.wav` | 20078 | 1 | 44100 | 0.227 | 1 | DoL | 0.7 | -4.2 | 25 | 6.6 | 1 | 21b5a35df39b |
| `Weapons/HandsPack/MP5K/MP5K_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -4.5 | 0 | 26.3 | 1 | e03253b5a25c |
| `Weapons/HandsPack/MP5K/MP5K_Shot.wav` | 39212 | 1 | 44100 | 0.444 | 1 | DoL | 0.7 | -1.2 | 0 | 40.6 | 1 | 94a0b4706dfe |
| `Weapons/HandsPack/PPK/PPK_BoltBack.wav` | 15920 | 1 | 44100 | 0.18 | 1 | DoL | 0.7 | -27.9 | 0 | 76.3 | 1 | c87ce9eb0a10 |
| `Weapons/HandsPack/PPK/PPK_BoltForward.wav` | 30914 | 1 | 44100 | 0.35 | 1 | DoL | 0.7 | -3.1 | 1 | 58.4 | 1 | 7683f09fcb53 |
| `Weapons/HandsPack/PPK/PPK_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -1.7 | 0 | 27.5 | 1 | 7002a9faa5cc |
| `Weapons/HandsPack/PPK/PPK_Shot.wav` | 67958 | 1 | 44100 | 0.77 | 1 | DoL | 0.7 | -1.0 | 0 | 129.4 | 1 | 1ac7a5bc8171 |
| `Weapons/HandsPack/Scar/Scar_BoltBack.wav` | 20078 | 1 | 44100 | 0.227 | 1 | DoL | 0.7 | -4.2 | 25 | 6.6 | 1 | 21b5a35df39b |
| `Weapons/HandsPack/Scar/Scar_BoltForward.wav` | 20078 | 1 | 44100 | 0.227 | 1 | DoL | 0.7 | -4.2 | 25 | 6.6 | 1 | 21b5a35df39b |
| `Weapons/HandsPack/Scar/Scar_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -5.6 | 0 | 30.4 | 1 | 687e6c46d85b |
| `Weapons/HandsPack/Scar/Scar_Shot.wav` | 23084 | 1 | 44100 | 0.261 | 1 | DoL | 0.7 | -1.2 | 0 | 40.6 | 1 | 593747d2e4fb |
| `Weapons/HandsPack/SniperRifle/SniperRifle_BoltBack.wav` | 39734 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -14.5 | 3 | 27.3 | 0 | 104a314a982f |
| `Weapons/HandsPack/SniperRifle/SniperRifle_BoltForward.wav` | 39734 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -14.9 | 80 | 0.8 | 0 | 6f8babd15a6d |
| `Weapons/HandsPack/SniperRifle/SniperRifle_MagIn.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -2.8 | 28 | 7.6 | 1 | 681e141fd95a |
| `Weapons/HandsPack/SniperRifle/SniperRifle_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -8.9 | 4 | 25.3 | 1 | 8f1cb3c6e2e3 |
| `Weapons/HandsPack/SniperRifle/SniperRifle_Shot.wav` | 132344 | 1 | 44100 | 1.5 | 1 | CiM | 0.7 | -1.8 | 0 | 41.3 | 1 | 4b58ed8eb1cd |
| `Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_BoltBack.wav` | 39734 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -14.5 | 3 | 27.3 | 0 | 104a314a982f |
| `Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_BoltForward.wav` | 39734 | 1 | 44100 | 0.45 | 1 | DoL | 0.7 | -14.9 | 80 | 0.8 | 0 | 6f8babd15a6d |
| `Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_MagIn.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -2.8 | 28 | 7.6 | 1 | 681e141fd95a |
| `Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -8.9 | 4 | 25.3 | 1 | 8f1cb3c6e2e3 |
| `Weapons/HandsPack/Uzi/Uzi_MagOut.wav` | 52964 | 1 | 44100 | 0.6 | 1 | DoL | 0.7 | -5.0 | 4 | 10.2 | 1 | 49c3f705edee |
| `Weapons/HandsPack/Uzi/Uzi_Shot.wav` | 4652 | 1 | 44100 | 0.052 | 1 | DoL | 0.7 | -29.9 | 0 | 36.2 | 0 | bcb6e477def4 |
| `Weapons/Kinemation/AK105/AK105_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -2.3 | 0 | 31.2 | 2 | 46bb040a33dd |
| `Weapons/Kinemation/AK105/AK105_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -2.3 | 0 | 31.2 | 2 | 46bb040a33dd |
| `Weapons/Kinemation/AK105/AK105_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -2.2 | 1 | 21.5 | 1 | 24c6f53c5071 |
| `Weapons/Kinemation/AK105/AK105_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -2.2 | 88 | 9.1 | 1 | a229f29eecdf |
| `Weapons/Kinemation/AK105/AK105_Shot.wav` | 49964 | 1 | 48000 | 0.52 | 1 | DoL | 0.7 | -4.8 | 0 | 42.3 | 1 | 4363019eb0c5 |
| `Weapons/Kinemation/Herrington/Herrington_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.6 | 40 | 2.1 | 1 | 569b8516e460 |
| `Weapons/Kinemation/Herrington/Herrington_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.6 | 40 | 2.1 | 1 | 569b8516e460 |
| `Weapons/Kinemation/Herrington/Herrington_MagIn.wav` | 67244 | 1 | 48000 | 0.7 | 1 | DoL | 0.7 | -4.9 | 0 | 395.6 | 1 | 693e360b19f0 |
| `Weapons/Kinemation/Herrington/Herrington_MagOut.wav` | 72044 | 1 | 48000 | 0.75 | 1 | DoL | 0.7 | -48.8 | 0 | 164.5 | 1 | a77921a166c4 |
| `Weapons/Kinemation/Herrington/Herrington_Shot.wav` | 144044 | 1 | 48000 | 1.5 | 1 | CiM | 0.7 | -4.8 | 0 | 38.8 | 1 | 9e230d0bb7d5 |
| `Weapons/Kinemation/MKR9/MKR9_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -3.1 | 11 | 2.9 | 1 | b9a735b69810 |
| `Weapons/Kinemation/MKR9/MKR9_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -3.1 | 11 | 2.9 | 1 | b9a735b69810 |
| `Weapons/Kinemation/MKR9/MKR9_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -2.9 | 31 | 2.4 | 1 | fda0a3d70ca7 |
| `Weapons/Kinemation/MKR9/MKR9_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -3.4 | 1 | 278.8 | 1 | 81430e719106 |
| `Weapons/Kinemation/MKR9/MKR9_Shot.wav` | 99884 | 1 | 48000 | 1.04 | 1 | CiM | 0.7 | -3.3 | 97 | 4.0 | 1 | 9ad531de9812 |
| `Weapons/Kinemation/Mk14/Mk14_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -5.5 | 9 | 2.6 | 1 | 3ca234578d9a |
| `Weapons/Kinemation/Mk14/Mk14_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -5.5 | 9 | 2.6 | 1 | 3ca234578d9a |
| `Weapons/Kinemation/Mk14/Mk14_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -5.8 | 67 | 3.1 | 1 | 7b2e305bfadc |
| `Weapons/Kinemation/Mk14/Mk14_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -5.8 | 1 | 31.7 | 1 | 3d8fc4d27568 |
| `Weapons/Kinemation/Mk14/Mk14_Shot.wav` | 131564 | 1 | 48000 | 1.37 | 1 | CiM | 0.7 | -5.8 | 32 | 6.3 | 1 | dc2151faf900 |
| `Weapons/Kinemation/R08/R08_MagIn.wav` | 37484 | 1 | 48000 | 0.39 | 1 | DoL | 0.7 | -5.8 | 0 | 136.6 | 1 | d93978f9da7b |
| `Weapons/Kinemation/R08/R08_MagOut.wav` | 35564 | 1 | 48000 | 0.37 | 1 | DoL | 0.7 | -5.9 | 0 | 35.4 | 1 | a280fd17199b |
| `Weapons/Kinemation/R08/R08_Shot.wav` | 144044 | 1 | 48000 | 1.5 | 1 | CiM | 0.7 | -5.7 | 4 | 21.4 | 1 | 0ac40d0b9e07 |
| `Weapons/Kinemation/SRM12/SRM12_BoltBack.wav` | 38924 | 1 | 48000 | 0.405 | 1 | DoL | 0.7 | -51.3 | 2 | 153.7 | 0 | 419f6b524e2a |
| `Weapons/Kinemation/SRM12/SRM12_BoltBack_Cut.wav` | 32012 | 1 | 48000 | 0.333 | 1 | DoL | 0.7 | -4.8 | 3 | 11.0 | 1 | f5d5aa2261b8 |
| `Weapons/Kinemation/SRM12/SRM12_BoltForward.wav` | 76364 | 1 | 48000 | 0.795 | 1 | DoL | 0.7 | -4.7 | 0 | 56.0 | 0 | 6a54a4f521fb |
| `Weapons/Kinemation/SRM12/SRM12_BoltForward_Cut.wav` | 30956 | 1 | 48000 | 0.322 | 1 | DoL | 0.7 | -4.7 | 2 | 25.7 | 1 | be3aed08bcfd |
| `Weapons/Kinemation/SRM12/SRM12_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -4.8 | 8 | 9.0 | 1 | 97aad9bc2335 |
| `Weapons/Kinemation/SRM12/SRM12_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -13.5 | 0 | 27.7 | 1 | 9a0c66fff287 |
| `Weapons/Kinemation/SRM12/SRM12_Shot.wav` | 158444 | 1 | 48000 | 1.65 | 1 | CiM | 0.7 | -4.8 | 0 | 57.1 | 1 | e26ebe68a246 |
| `Weapons/Kinemation/TR15/TR15_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.5 | 70 | 0.8 | 1 | 3aff5d696fa1 |
| `Weapons/Kinemation/TR15/TR15_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.5 | 70 | 0.8 | 1 | 3aff5d696fa1 |
| `Weapons/Kinemation/TR15/TR15_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -4.7 | 7 | 4.0 | 0 | 63b56677cc6f |
| `Weapons/Kinemation/TR15/TR15_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -10.7 | 4 | 33.4 | 1 | 84cc92a83e7e |
| `Weapons/Kinemation/TR15/TR15_Shot.wav` | 49964 | 1 | 48000 | 0.52 | 1 | DoL | 0.7 | -4.8 | 0 | 27.6 | 1 | 5755d02d1cbf |
| `Weapons/Kinemation/Viper/Viper_BoltBack.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.5 | 63 | 1.2 | 2 | a3d27670b3d4 |
| `Weapons/Kinemation/Viper/Viper_BoltForward.wav` | 43244 | 1 | 48000 | 0.45 | 1 | DoL | 0.7 | -4.5 | 63 | 1.2 | 2 | a3d27670b3d4 |
| `Weapons/Kinemation/Viper/Viper_MagIn.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -4.8 | 7 | 2.8 | 1 | 8867b8b88a02 |
| `Weapons/Kinemation/Viper/Viper_MagOut.wav` | 57644 | 1 | 48000 | 0.6 | 1 | DoL | 0.7 | -21.6 | 5 | 4.4 | 1 | bac0f5189ec9 |
| `Weapons/Kinemation/Viper/Viper_Shot.wav` | 45164 | 1 | 48000 | 0.47 | 1 | DoL | 0.7 | -4.8 | 0 | 38.1 | 1 | c106b6d2f7c5 |
| `Weapons/MagazineInsert_Default.wav` | 96278 | 2 | 44100 | 0.363 | 1 | DoL | 1 | 0.0 | 100 | 0.9 | 1 | b3c3788307b1 |
| `Weapons/Revolver/Revolver_Shot.wav` | 144044 | 1 | 48000 | 1.5 | 1 | CiM | 0.7 | -5.7 | 4 | 21.4 | 1 | 24b676b45769 |
| `Weapons/SniperRifleAuto/SniperRifleAuto_Shot.wav` | 48554 | 1 | 44100 | 0.55 | 1 | DoL | 0.7 | -2.2 | 0 | 41.3 | 1 | 1107a132aa3a |
| `Weapons/UI_Error_Subtle_Deep_stereo.wav` | 37312 | 2 | 44100 | 0.205 | 1 | DoL | 0.7 | -12.7 | 100 | 0.2 | 4 | 388bdeb8fc4d |
